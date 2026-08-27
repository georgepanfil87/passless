using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Passless.Api.Features.WebAuthn;
using Passless.Core.Abstractions;
using Passless.Core.Entities;
using Passless.Infrastructure;

namespace Passless.Api.Features.Registration;

internal readonly record struct RegistrationOutcome(
    bool Succeeded,
    Guid CredentialId,
    RegistrationFailure? Failure)
{
    public static RegistrationOutcome Success(Guid credentialId) => new(true, credentialId, null);

    public static RegistrationOutcome Failed(RegistrationFailure failure) => new(false, default, failure);
}

internal sealed class RegistrationService(
    IFido2 fido2,
    IChallengeStore challenges,
    IAuditLog audit,
    PasslessDbContext db,
    IOptions<WebAuthnOptions> webAuthn,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>State carried across the two halves of the ceremony.</summary>
    private sealed record PendingRegistration(
        Guid UserId,
        string Username,
        string DisplayName,
        string OptionsJson);

    public async Task<CredentialCreateOptions> BeginAsync(
        BeginRegistrationRequest request,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var options = webAuthn.Value;

        // Minted now, persisted only if the ceremony completes. Nothing is
        // written at this point, so an abandoned registration leaves no
        // half-created account behind.
        var prospectiveUserId = Guid.NewGuid();

        // Deliberately issued even when the username is already taken. Refusing
        // here would answer "does this account exist?" in one cheap request;
        // failing at verification instead makes the same question cost a full
        // ceremony. It raises the price of enumeration rather than removing it —
        // the real fix is verifying ownership of the address before the account
        // becomes usable, which this project's non-goals currently exclude.
        var createOptions = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = prospectiveUserId.ToByteArray(),
                Name = request.Username,
                DisplayName = request.DisplayName,
            },
            ExcludeCredentials = [],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            // We record the AAGUID but do not verify an attestation chain, so
            // asking for one would be theatre: an unverified attestation
            // statement is a claim, not evidence. Requesting none also avoids
            // the privacy prompt some platforms show.
            AttestationPreference = AttestationConveyancePreference.None,
        });

        var ceremonyId = CeremonyCookie.Issue(http.Response, options.ChallengeTimeToLive);

        var pending = new PendingRegistration(
            prospectiveUserId,
            request.Username,
            request.DisplayName,
            JsonSerializer.Serialize(createOptions, Json));

        await challenges.StoreAsync(
            ceremonyId,
            new ChallengeTicket(ChallengeKind.Registration, JsonSerializer.Serialize(pending, Json), time.GetUtcNow()),
            options.ChallengeTimeToLive,
            cancellationToken);

        await audit.WriteAsync(
            new AuditEntry(
                AuditEventType.ChallengeIssued,
                AuditSeverity.Info,
                Metadata: new Dictionary<string, string> { ["ceremony"] = "registration" },
                Ip: http.Connection.RemoteIpAddress,
                UserAgent: http.Request.Headers.UserAgent.ToString()),
            cancellationToken);

        return createOptions;
    }

    public async Task<RegistrationOutcome> CompleteAsync(
        AuthenticatorAttestationRawResponse attestation,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var options = webAuthn.Value;

        var ceremonyId = CeremonyCookie.Read(http.Request);
        if (string.IsNullOrEmpty(ceremonyId))
        {
            return await FailAsync(RegistrationFailure.CeremonyMissing, http, cancellationToken);
        }

        // Cleared before the outcome is known. A ceremony gets one attempt
        // whether it succeeds or not, so leaving the cookie in place on failure
        // would invite retrying against a challenge that is already gone.
        CeremonyCookie.Clear(http.Response);

        var ticket = await challenges.ConsumeAsync(ceremonyId, cancellationToken);
        if (ticket is null || ticket.Kind != ChallengeKind.Registration)
        {
            return await FailAsync(RegistrationFailure.ChallengeNotFound, http, cancellationToken);
        }

        var pending = JsonSerializer.Deserialize<PendingRegistration>(ticket.Payload, Json)!;

        var verdict = ClientDataInspector.Inspect(
            attestation.Response.ClientDataJson,
            expectedType: "webauthn.create",
            options.Origins);

        if (verdict != ClientDataVerdict.Ok)
        {
            return await FailAsync(verdict switch
            {
                ClientDataVerdict.Malformed => RegistrationFailure.ClientDataMalformed,
                ClientDataVerdict.WrongCeremonyType => RegistrationFailure.WrongCeremonyType,
                _ => RegistrationFailure.OriginNotAllowed,
            }, http, cancellationToken, pending.UserId);
        }

        var originalOptions = JsonSerializer.Deserialize<CredentialCreateOptions>(pending.OptionsJson, Json)!;

        var duplicateCredential = false;
        RegisteredPublicKeyCredential credential;

        try
        {
            credential = await fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = attestation,
                    OriginalOptions = originalOptions,
                    IsCredentialIdUniqueToUserCallback = async (args, token) =>
                    {
                        // Global, not scoped to the prospective user. The same
                        // credential resolving to two accounts is the condition
                        // that makes an assertion ambiguous.
                        var exists = await db.Credentials
                            .AsNoTracking()
                            .AnyAsync(c => c.CredentialId == args.CredentialId, token);

                        duplicateCredential |= exists;
                        return !exists;
                    },
                },
                cancellationToken);
        }
        catch (Fido2VerificationException)
        {
            return await FailAsync(
                duplicateCredential
                    ? RegistrationFailure.CredentialAlreadyRegistered
                    : RegistrationFailure.AttestationInvalid,
                http,
                cancellationToken,
                pending.UserId);
        }

        var normalized = User.NormalizeUsername(pending.Username);
        if (await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedUsername == normalized, cancellationToken))
        {
            return await FailAsync(RegistrationFailure.UsernameUnavailable, http, cancellationToken, pending.UserId);
        }

        var now = time.GetUtcNow();
        var user = new User(pending.UserId, pending.Username, pending.DisplayName, now);
        var stored = new Credential(
            Guid.NewGuid(),
            user.Id,
            credential.Id,
            credential.PublicKey,
            credential.SignCount,
            credential.AaGuid,
            credential.Transports?.Select(t => t.ToString()) ?? [],
            credential.IsBackupEligible,
            credential.IsBackedUp,
            friendlyName: null,
            now);

        db.Users.Add(user);
        db.Credentials.Add(stored);

        // Enlisted, not written separately: the audit rows and the account they
        // describe commit in one transaction or not at all.
        audit.Enlist(new AuditEntry(
            AuditEventType.UserRegistered,
            AuditSeverity.Info,
            user.Id,
            Ip: http.Connection.RemoteIpAddress,
            UserAgent: http.Request.Headers.UserAgent.ToString()));

        audit.Enlist(new AuditEntry(
            AuditEventType.CredentialRegistered,
            AuditSeverity.Info,
            user.Id,
            // The internal id, not the WebAuthn credential id: enough to
            // correlate, and it discloses nothing about the authenticator.
            Metadata: new Dictionary<string, string>
            {
                ["credential"] = stored.Id.ToString(),
                ["aaguid"] = stored.Aaguid.ToString(),
                ["backup_eligible"] = stored.BackupEligible ? "true" : "false",
                ["backup_state"] = stored.BackupState ? "true" : "false",
                ["transports"] = string.Join(',', stored.Transports),
            },
            Ip: http.Connection.RemoteIpAddress,
            UserAgent: http.Request.Headers.UserAgent.ToString()));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (UniqueViolation(e) is { } constraint)
        {
            // The pre-checks above lose to a concurrent registration often enough
            // to matter. The unique indexes are what actually decide it.
            var failure = constraint.Contains("credential_id", StringComparison.Ordinal)
                ? RegistrationFailure.CredentialAlreadyRegistered
                : RegistrationFailure.UsernameUnavailable;

            // The context is holding entities that failed to insert; anything
            // saved through it now would retry them. Drop them before auditing.
            db.ChangeTracker.Clear();
            return await FailAsync(failure, http, cancellationToken, pending.UserId);
        }

        return RegistrationOutcome.Success(stored.Id);
    }

    private async Task<RegistrationOutcome> FailAsync(
        RegistrationFailure failure,
        HttpContext http,
        CancellationToken cancellationToken,
        Guid? userId = null)
    {
        // The prospective user id is recorded only when one exists, and it never
        // reaches the response. It correlates the attempt in the audit log
        // without telling the caller anything.
        await audit.WriteAsync(
            new AuditEntry(
                AuditEventType.RegistrationRejected,
                failure.Severity(),
                UserId: null,
                Metadata: BuildFailureMetadata(failure, userId),
                Ip: http.Connection.RemoteIpAddress,
                UserAgent: http.Request.Headers.UserAgent.ToString()),
            cancellationToken);

        return RegistrationOutcome.Failed(failure);
    }

    private static Dictionary<string, string> BuildFailureMetadata(RegistrationFailure failure, Guid? userId)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reason"] = failure.Reason(),
        };

        if (userId is { } id)
        {
            metadata["prospective_user"] = id.ToString();
        }

        return metadata;
    }

    private static string? UniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName ?? string.Empty
            : null;
}
