using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Entities;
using Passless.Infrastructure;

namespace Passless.IntegrationTests.Registration;

[Collection(PasslessCollection.Name)]
public sealed class RegistrationTests(PasslessFixture fixture)
{
    private static string NewUsername() => $"user-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Registration_succeeds_and_persists_the_authenticator_metadata()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var username = NewUsername();
        var authenticator = new SoftwareAuthenticator
        {
            BackupEligible = true,
            BackupState = false,
        };

        var start = await RegistrationDriver.BeginAsync(client, username);
        var attestation = authenticator.Attest(start.Options, PasslessApiFactory.Origin);

        var response = await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var user = await db.Users.SingleAsync(u => u.NormalizedUsername == User.NormalizeUsername(username));
        var credential = await db.Credentials.SingleAsync(c => c.UserId == user.Id);

        Assert.Equal(authenticator.CredentialId, credential.CredentialId);
        Assert.NotEmpty(credential.PublicKey);

        // The four fields that let the UI tell a device-bound passkey from a
        // synced one. Backup eligible but not backed up is exactly the state a
        // security key reports, and it must survive the round trip intact.
        Assert.Equal(authenticator.Aaguid, credential.Aaguid);
        Assert.True(credential.BackupEligible);
        Assert.False(credential.BackupState);
        Assert.Contains("internal", credential.Transports, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hybrid", credential.Transports, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registration_writes_an_audit_trail()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var username = NewUsername();

        var start = await RegistrationDriver.BeginAsync(client, username);
        var attestation = new SoftwareAuthenticator().Attest(start.Options, PasslessApiFactory.Origin);
        await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var user = await db.Users.SingleAsync(u => u.NormalizedUsername == User.NormalizeUsername(username));
        var events = await db.AuditEvents.Where(e => e.UserId == user.Id).ToListAsync();

        Assert.Contains(events, e => e.Type == AuditEventType.UserRegistered);

        var registered = Assert.Single(events, e => e.Type == AuditEventType.CredentialRegistered);
        Assert.Equal("true", registered.Metadata["backup_eligible"]);

        // No secret, key or challenge may reach a row nobody can redact.
        Assert.All(registered.Metadata.Values, value =>
            Assert.DoesNotContain("BEGIN", value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Replayed_challenge_is_rejected()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var authenticator = new SoftwareAuthenticator();

        var start = await RegistrationDriver.BeginAsync(client, NewUsername());
        var attestation = authenticator.Attest(start.Options, PasslessApiFactory.Origin);

        var first = await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Byte-for-byte the same submission, with the same ceremony handle. The
        // challenge is gone, so this is indistinguishable from one that expired.
        var replay = await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        await AssertGenericFailureAsync(replay);
        await AssertRejectionAuditedAsync("challenge_not_found");
    }

    [Fact]
    public async Task Expired_challenge_is_rejected()
    {
        // A dedicated host over the same containers, with a TTL short enough to
        // outlive. Deleting the key instead would test the store's absence
        // handling, not Redis expiry.
        await using var shortLived = fixture.CreateApi(new Dictionary<string, string>
        {
            ["WebAuthn:ChallengeTimeToLive"] = "00:00:01",
        });

        using var client = shortLived.CreateCeremonyClient();
        var start = await RegistrationDriver.BeginAsync(client, NewUsername());
        var attestation = new SoftwareAuthenticator().Attest(start.Options, PasslessApiFactory.Origin);

        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        var response = await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertGenericFailureAsync(response);
    }

    [Fact]
    public async Task Mismatched_origin_is_rejected()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        var start = await RegistrationDriver.BeginAsync(client, NewUsername());

        // A cryptographically perfect ceremony run from somewhere else. This is
        // the phishing case: everything verifies except where it happened.
        var attestation = new SoftwareAuthenticator().Attest(start.Options, "https://passless.evil.example");

        var response = await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertGenericFailureAsync(response);
        await AssertRejectionAuditedAsync("origin_not_allowed", AuditSeverity.Critical);
    }

    [Fact]
    public async Task Loopback_address_is_a_different_origin_from_localhost()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        var start = await RegistrationDriver.BeginAsync(client, NewUsername());

        // Same machine, same interface, same server. Different origin, because
        // the browser compares the host as text and never resolves it.
        var attestation = new SoftwareAuthenticator().Attest(start.Options, "https://127.0.0.1:4200");

        var response = await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_credential_id_is_rejected_without_revealing_the_owner()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        // One authenticator, deliberately reused across two accounts.
        var authenticator = new SoftwareAuthenticator();

        var first = await RegistrationDriver.BeginAsync(client, NewUsername());
        var firstResponse = await RegistrationDriver.VerifyAsync(
            client, first.CeremonyCookie, authenticator.Attest(first.Options, PasslessApiFactory.Origin));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var second = await RegistrationDriver.BeginAsync(client, NewUsername());
        var duplicate = await RegistrationDriver.VerifyAsync(
            client, second.CeremonyCookie, authenticator.Attest(second.Options, PasslessApiFactory.Origin));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        // The response must not distinguish "already yours" from "already
        // someone else's" -- or from any other failure.
        var expired = await StaleCeremonyResponseAsync(client);
        Assert.Equal(
            await expired.Content.ReadAsStringAsync(),
            await duplicate.Content.ReadAsStringAsync());

        await AssertRejectionAuditedAsync("credential_already_registered");
    }

    [Fact]
    public async Task Concurrent_double_submit_yields_exactly_one_success()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        var start = await RegistrationDriver.BeginAsync(client, NewUsername());
        var attestation = new SoftwareAuthenticator().Attest(start.Options, PasslessApiFactory.Origin);

        var before = await CountRejectionsByReasonAsync();

        // Both requests carry the same ceremony handle and are in flight before
        // either response returns.
        var submissions = await Task.WhenAll(
            RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation),
            RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation));

        var statuses = submissions.Select(r => r.StatusCode).ToArray();

        Assert.Single(statuses, HttpStatusCode.OK);
        Assert.Single(statuses, HttpStatusCode.BadRequest);

        // One success and one failure is not enough to prove anything: a
        // non-atomic store would let both past the challenge and the loser would
        // still be refused downstream by the unique index on the credential id.
        // The reason is what distinguishes the two worlds, so assert on it --
        // the loser must have failed at the challenge and nowhere else.
        var after = await CountRejectionsByReasonAsync();

        Assert.Equal(1, Delta(before, after, "challenge_not_found"));
        Assert.Equal(0, Delta(before, after, "credential_already_registered"));
        Assert.Equal(0, Delta(before, after, "username_unavailable"));

        foreach (var submission in submissions)
        {
            submission.Dispose();
        }
    }

    private static int Delta(
        IReadOnlyDictionary<string, int> before,
        IReadOnlyDictionary<string, int> after,
        string reason) =>
        after.GetValueOrDefault(reason) - before.GetValueOrDefault(reason);

    private async Task<IReadOnlyDictionary<string, int>> CountRejectionsByReasonAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var rejections = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.Type == AuditEventType.RegistrationRejected)
            .ToListAsync();

        return rejections
            .GroupBy(e => e.Metadata["reason"])
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Registering_a_username_that_already_exists_is_refused()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var username = NewUsername();

        var first = await RegistrationDriver.BeginAsync(client, username);
        var firstResponse = await RegistrationDriver.VerifyAsync(
            client, first.CeremonyCookie, new SoftwareAuthenticator().Attest(first.Options, PasslessApiFactory.Origin));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Different authenticator, same username. Without this, anyone could
        // attach their own passkey to an existing account and sign in as its
        // owner -- the ceremony would verify perfectly and the outcome would be
        // a takeover. Adding a passkey to an account requires being signed in
        // to it, which is a separate endpoint.
        var second = await RegistrationDriver.BeginAsync(client, username.ToUpperInvariant());
        var takeover = await RegistrationDriver.VerifyAsync(
            client, second.CeremonyCookie, new SoftwareAuthenticator().Attest(second.Options, PasslessApiFactory.Origin));

        Assert.Equal(HttpStatusCode.BadRequest, takeover.StatusCode);
        await AssertRejectionAuditedAsync("username_unavailable");
    }

    [Fact]
    public async Task Verification_without_a_ceremony_cookie_is_rejected()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        var start = await RegistrationDriver.BeginAsync(client, NewUsername());
        var attestation = new SoftwareAuthenticator().Attest(start.Options, PasslessApiFactory.Origin);

        // A challenge on its own proves only that the server issued one. Without
        // the handle it was bound to, it belongs to nobody.
        var response = await RegistrationDriver.VerifyAsync(client, ceremonyCookie: "not-a-real-handle", attestation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertGenericFailureAsync(response);
    }

    private static async Task AssertGenericFailureAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("""{"error":"registration_failed"}""", body);
    }

    private async Task<HttpResponseMessage> StaleCeremonyResponseAsync(HttpClient client)
    {
        var start = await RegistrationDriver.BeginAsync(client, NewUsername());
        var attestation = new SoftwareAuthenticator().Attest(start.Options, PasslessApiFactory.Origin);
        await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);
        return await RegistrationDriver.VerifyAsync(client, start.CeremonyCookie, attestation);
    }

    private async Task AssertRejectionAuditedAsync(string reason, AuditSeverity? severity = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var rejections = await db.AuditEvents
            .Where(e => e.Type == AuditEventType.RegistrationRejected)
            .ToListAsync();

        var matching = rejections.Where(e => e.Metadata["reason"] == reason).ToList();
        Assert.NotEmpty(matching);

        if (severity is { } expected)
        {
            Assert.All(matching, e => Assert.Equal(expected, e.Severity));
        }
    }
}
