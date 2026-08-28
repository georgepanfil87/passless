using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Passless.Api.Features.WebAuthn;
using Passless.Core.Abstractions;
using Passless.Core.Entities;
using Passless.Infrastructure;

namespace Passless.Api.Features.Authentication;

internal readonly record struct AuthenticationOutcome(
    bool Succeeded,
    Guid SessionId,
    IssuedTokens? Tokens,
    AuthenticationFailure? Failure)
{
    public static AuthenticationOutcome Success(Guid sessionId, IssuedTokens tokens) =>
        new(true, sessionId, tokens, null);

    public static AuthenticationOutcome Failed(AuthenticationFailure failure) =>
        new(false, default, null, failure);
}

internal sealed class AuthenticationService(
    IFido2 fido2,
    IChallengeStore challenges,
    IAuditLog audit,
    ITokenService tokens,
    PasslessDbContext db,
    IOptions<WebAuthnOptions> webAuthn,
    TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record PendingAssertion(string? RequestedUsername, string OptionsJson);

    public async Task<AssertionOptions> BeginAsync(
        BeginAuthenticationRequest request,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var options = webAuthn.Value;
        string? normalizedUsername = null;
        IReadOnlyList<PublicKeyCredentialDescriptor> allowCredentials = [];

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            normalizedUsername = User.NormalizeUsername(request.Username);

            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken);

            // A disabled account takes the same branch as one that never
            // existed. Otherwise "disabled" becomes its own observable state,
            // which is a smaller leak than existence but a leak all the same.
            allowCredentials = user is { DisabledAt: null }
                ? await DescriptorsForAsync(user.Id, cancellationToken)
                : CredentialDecoys.For(normalizedUsername, options.DecoyKeyBytes);
        }

        // Empty allowCredentials is the usernameless ceremony: the authenticator
        // offers whatever discoverable credentials it holds for this RP ID and
        // tells us which account it chose through the user handle.
        var assertionOptions = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowCredentials,
            UserVerification = UserVerificationRequirement.Preferred,
        });

        var ceremonyId = CeremonyCookie.Issue(http.Response, options.ChallengeTimeToLive);

        await challenges.StoreAsync(
            ceremonyId,
            new ChallengeTicket(
                ChallengeKind.Assertion,
                JsonSerializer.Serialize(
                    new PendingAssertion(normalizedUsername, JsonSerializer.Serialize(assertionOptions, Json)),
                    Json),
                time.GetUtcNow()),
            options.ChallengeTimeToLive,
            cancellationToken);

        await audit.WriteAsync(
            new AuditEntry(
                AuditEventType.ChallengeIssued,
                AuditSeverity.Info,
                Metadata: new Dictionary<string, string> { ["ceremony"] = "assertion" },
                Ip: http.Connection.RemoteIpAddress,
                UserAgent: http.Request.Headers.UserAgent.ToString()),
            cancellationToken);

        return assertionOptions;
    }

    public async Task<AuthenticationOutcome> CompleteAsync(
        AuthenticatorAssertionRawResponse assertion,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var options = webAuthn.Value;

        var ceremonyId = CeremonyCookie.Read(http.Request);
        if (string.IsNullOrEmpty(ceremonyId))
        {
            return await FailAsync(AuthenticationFailure.CeremonyMissing, http, cancellationToken);
        }

        CeremonyCookie.Clear(http.Response);

        var ticket = await challenges.ConsumeAsync(ceremonyId, cancellationToken);
        if (ticket is null || ticket.Kind != ChallengeKind.Assertion)
        {
            return await FailAsync(AuthenticationFailure.ChallengeNotFound, http, cancellationToken);
        }

        var pending = JsonSerializer.Deserialize<PendingAssertion>(ticket.Payload, Json)!;

        var verdict = ClientDataInspector.Inspect(
            assertion.Response.ClientDataJson,
            expectedType: "webauthn.get",
            options.Origins);

        if (verdict != ClientDataVerdict.Ok)
        {
            return await FailAsync(verdict switch
            {
                ClientDataVerdict.Malformed => AuthenticationFailure.ClientDataMalformed,
                ClientDataVerdict.WrongCeremonyType => AuthenticationFailure.WrongCeremonyType,
                _ => AuthenticationFailure.OriginNotAllowed,
            }, http, cancellationToken);
        }

        var credential = await db.Credentials
            .FirstOrDefaultAsync(c => c.CredentialId == assertion.RawId, cancellationToken);

        if (credential is null)
        {
            // Also where an asserted decoy lands.
            return await FailAsync(AuthenticationFailure.UnknownCredential, http, cancellationToken);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == credential.UserId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            return await FailAsync(AuthenticationFailure.AccountUnavailable, http, cancellationToken, credential.UserId);
        }

        // A ceremony begun for one account may not be finished by another, even
        // with a credential that is perfectly valid on its own terms.
        if (pending.RequestedUsername is not null
            && !string.Equals(pending.RequestedUsername, user.NormalizedUsername, StringComparison.Ordinal))
        {
            return await FailAsync(AuthenticationFailure.UserMismatch, http, cancellationToken, user.Id);
        }

        var originalOptions = JsonSerializer.Deserialize<AssertionOptions>(pending.OptionsJson, Json)!;

        VerifyAssertionResult result;
        try
        {
            result = await fido2.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = assertion,
                    OriginalOptions = originalOptions,
                    StoredPublicKey = credential.PublicKey,

                    // Zero on purpose, and this is the load-bearing decision in
                    // this method. Passing the real stored counter would have the
                    // library reject a regression itself, before the signature is
                    // checked — which means anyone could post unsigned rubbish
                    // with a low counter and make us raise a critical "cloned
                    // authenticator" alarm about a credential they do not hold.
                    // Suppressing the library's check lets the signature, origin,
                    // RP ID hash and user presence be verified first; the counter
                    // rule is then applied below to a result we know is genuine.
                    // An alarm that anyone can trigger is not an alarm.
                    StoredSignatureCounter = 0,

                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                        Task.FromResult(HandleOwnsCredential(args, credential)),
                },
                cancellationToken);
        }
        catch (Fido2VerificationException)
        {
            return await FailAsync(AuthenticationFailure.AssertionInvalid, http, cancellationToken, user.Id);
        }

        if (IsCounterRegression(credential.SignatureCounter, result.SignCount))
        {
            return await FailAsync(
                AuthenticationFailure.SignCounterRegression,
                http,
                cancellationToken,
                user.Id,
                extraMetadata: new Dictionary<string, string>
                {
                    ["credential"] = credential.Id.ToString(),
                    ["stored_counter"] = credential.SignatureCounter.ToString(),
                    ["presented_counter"] = result.SignCount.ToString(),
                });
        }

        var now = time.GetUtcNow();
        credential.RecordUse(result.SignCount, now);

        var family = new TokenFamily(Guid.NewGuid(), user.Id, now);
        var session = new Session(
            Guid.NewGuid(),
            user.Id,
            family.Id,
            DeviceLabelFor(http),
            http.Request.Headers.UserAgent.ToString(),
            http.Connection.RemoteIpAddress,
            now);

        db.TokenFamilies.Add(family);
        db.Sessions.Add(session);

        audit.Enlist(new AuditEntry(
            AuditEventType.CredentialUsed,
            AuditSeverity.Info,
            user.Id,
            Metadata: new Dictionary<string, string>
            {
                ["credential"] = credential.Id.ToString(),
                ["sign_counter"] = result.SignCount.ToString(),
                ["backed_up"] = result.IsBackedUp ? "true" : "false",
                ["usernameless"] = pending.RequestedUsername is null ? "true" : "false",
            },
            Ip: http.Connection.RemoteIpAddress,
            UserAgent: http.Request.Headers.UserAgent.ToString()));

        audit.Enlist(new AuditEntry(
            AuditEventType.SessionCreated,
            AuditSeverity.Info,
            user.Id,
            Metadata: new Dictionary<string, string>
            {
                ["session"] = session.Id.ToString(),
                ["family"] = family.Id.ToString(),
            },
            Ip: http.Connection.RemoteIpAddress,
            UserAgent: http.Request.Headers.UserAgent.ToString()));

        // Enlisted rather than saved separately, so the first refresh token of
        // the family lands in the same transaction as the session and the family
        // itself. A session that existed without its lineage, or a lineage with
        // no first token, would both be states nothing downstream knows how to
        // recover from.
        var issued = tokens.EnlistInitialTokens(user.Id, session.Id, family.Id);

        await db.SaveChangesAsync(cancellationToken);

        return AuthenticationOutcome.Success(session.Id, issued);
    }

    /// <summary>
    /// The signature counter rule.
    /// </summary>
    /// <remarks>
    /// A counter that fails to advance means two authenticators are answering
    /// for one credential: the copy that was cloned kept the value it had when
    /// it was taken.
    ///
    /// The exemption for zero is not a loophole, it is the common case. The
    /// specification makes the counter optional, and most synced passkey
    /// providers — iCloud Keychain and Google Password Manager among them — omit
    /// it entirely and report zero on every assertion, precisely because the
    /// credential is meant to exist on several devices at once. Demanding a
    /// strictly increasing counter would lock out the majority of real passkey
    /// users while catching nobody.
    ///
    /// So: once an authenticator has demonstrated that it counts, it is held to
    /// it. One that never counted is never asked to start.
    /// </remarks>
    private static bool IsCounterRegression(uint stored, uint presented) =>
        stored > 0 && presented <= stored;

    private static bool HandleOwnsCredential(IsUserHandleOwnerOfCredentialIdParams args, Credential credential)
    {
        if (!args.CredentialId.AsSpan().SequenceEqual(credential.CredentialId))
        {
            return false;
        }

        // Absent for a non-discoverable credential, where the browser was told
        // which credential to use. Nothing is claimed, so nothing to contradict:
        // the credential id lookup already bound this to exactly one account.
        if (args.UserHandle is not { Length: 16 })
        {
            return args.UserHandle is null or { Length: 0 };
        }

        return new Guid(args.UserHandle) == credential.UserId;
    }

    private async Task<IReadOnlyList<PublicKeyCredentialDescriptor>> DescriptorsForAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var credentials = await db.Credentials
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.CredentialId, c.Transports })
            .ToListAsync(cancellationToken);

        return credentials
            .Select(c => new PublicKeyCredentialDescriptor(
                PublicKeyCredentialType.PublicKey,
                c.CredentialId,
                ParseTransports(c.Transports)))
            .ToList();
    }

    private static AuthenticatorTransport[] ParseTransports(IEnumerable<string> transports) =>
        transports
            .Select(t => Enum.TryParse<AuthenticatorTransport>(t, ignoreCase: true, out var parsed)
                ? parsed
                : (AuthenticatorTransport?)null)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToArray();

    private static string DeviceLabelFor(HttpContext http)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent)
            ? "Unknown device"
            : userAgent[..Math.Min(userAgent.Length, 128)];
    }

    private async Task<AuthenticationOutcome> FailAsync(
        AuthenticationFailure failure,
        HttpContext http,
        CancellationToken cancellationToken,
        Guid? userId = null,
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reason"] = failure.Reason(),
        };

        foreach (var (key, value) in extraMetadata ?? new Dictionary<string, string>())
        {
            metadata[key] = value;
        }

        // The user id is recorded when the failure got far enough to identify
        // one. It never reaches the response.
        await audit.WriteAsync(
            new AuditEntry(
                failure.EventType(),
                failure.Severity(),
                userId,
                metadata,
                http.Connection.RemoteIpAddress,
                http.Request.Headers.UserAgent.ToString()),
            cancellationToken);

        return AuthenticationOutcome.Failed(failure);
    }
}
