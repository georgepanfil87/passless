using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Entities;
using Passless.Infrastructure;
using Passless.IntegrationTests.Registration;

namespace Passless.IntegrationTests.Authentication;

[Collection(PasslessCollection.Name)]
public sealed class AuthenticationTests(PasslessFixture fixture)
{
    private sealed record Account(string Username, Guid UserId, SoftwareAuthenticator Authenticator);

    [Fact]
    public async Task Valid_assertion_succeeds_and_opens_a_session()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client);

        var start = await AuthenticationDriver.BeginAsync(client, account.Username);

        // The authenticator was registered at counter 0, so any advance is fine.
        var assertion = account.Authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 1);

        var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = body.GetProperty("sessionId").GetGuid();

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var session = await db.Sessions.SingleAsync(s => s.Id == sessionId);
        Assert.Equal(account.UserId, session.UserId);
        Assert.False(session.IsRevoked);

        // The family exists and is tied one-to-one to the session, so the next
        // step has a lineage to rotate tokens within.
        var family = await db.TokenFamilies.SingleAsync(f => f.Id == session.FamilyId);
        Assert.Equal(account.UserId, family.UserId);
        Assert.False(family.IsInvalidated);

        // An access token comes back in the body; the refresh token does not,
        // because it travels only as an HttpOnly cookie.
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.False(body.TryGetProperty("refreshToken", out _));
        Assert.DoesNotContain("plrt_", body.ToString(), StringComparison.Ordinal);

        var credential = await db.Credentials.SingleAsync(c => c.UserId == account.UserId);
        Assert.NotNull(credential.LastUsedAt);
        Assert.Equal(1u, credential.SignatureCounter);
    }

    [Fact]
    public async Task Usernameless_assertion_succeeds()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client);

        var start = await AuthenticationDriver.BeginAsync(client, username: null);

        // No allowCredentials: the authenticator picks a discoverable credential
        // for this RP and identifies the account through the user handle.
        Assert.Empty(start.Options.AllowCredentials);

        var assertion = account.Authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 1);

        var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Regressed_sign_counter_is_rejected_and_audited_as_critical()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        // Registered at 10, so this authenticator has demonstrated that it counts.
        var account = await RegisterAsync(client, new SoftwareAuthenticator { SignCount = 10 });

        var start = await AuthenticationDriver.BeginAsync(client, account.Username);

        // A perfectly valid signature presenting a counter that has not moved.
        // Two authenticators are answering for one credential.
        var assertion = account.Authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 10);

        var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertGenericFailureAsync(response);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var regression = await db.AuditEvents
            .Where(e => e.UserId == account.UserId && e.Type == AuditEventType.SignCounterRegression)
            .SingleAsync();

        Assert.Equal(AuditSeverity.Critical, regression.Severity);
        Assert.Equal("10", regression.Metadata["stored_counter"]);
        Assert.Equal("10", regression.Metadata["presented_counter"]);

        // Rejected means rejected: no session, and the stored counter untouched.
        Assert.False(await db.Sessions.AnyAsync(s => s.UserId == account.UserId));
        var credential = await db.Credentials.SingleAsync(c => c.UserId == account.UserId);
        Assert.Equal(10u, credential.SignatureCounter);
    }

    [Fact]
    public async Task Advancing_sign_counter_is_accepted()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client, new SoftwareAuthenticator { SignCount = 10 });

        var start = await AuthenticationDriver.BeginAsync(client, account.Username);
        var assertion = account.Authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 11);

        var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);

        // The positive control for the test above: the rule rejects regressions,
        // not counters in general.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticator_that_never_counts_is_accepted_repeatedly()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client, new SoftwareAuthenticator { SignCount = 0 });

        // The synced-passkey case, and the one that matters most in practice:
        // iCloud Keychain and Google Password Manager report zero on every
        // assertion because the credential deliberately exists on several
        // devices. A strict monotonic rule would lock out most real users.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var start = await AuthenticationDriver.BeginAsync(client, account.Username);
            var assertion = account.Authenticator.Assert(
                start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 0);

            var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        Assert.Equal(3, await db.Sessions.CountAsync(s => s.UserId == account.UserId));
    }

    [Fact]
    public async Task Assertion_for_an_unknown_credential_is_rejected()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        await RegisterAsync(client);

        var start = await AuthenticationDriver.BeginAsync(client, username: null);

        // Never registered anywhere. Also the shape an asserted decoy takes.
        var stranger = new SoftwareAuthenticator();
        var assertion = stranger.Assert(
            start.Options, PasslessApiFactory.Origin, Guid.NewGuid(), signCount: 1);

        var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertGenericFailureAsync(response);
    }

    [Fact]
    public async Task Replayed_assertion_is_rejected()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client);

        var start = await AuthenticationDriver.BeginAsync(client, account.Username);
        var assertion = account.Authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 1);

        var first = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // The identical signature, captured and sent again. Single-use challenge
        // consumption is what stops it; the signature itself is still valid.
        var replay = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await AssertGenericFailureAsync(replay);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        Assert.Equal(1, await db.Sessions.CountAsync(s => s.UserId == account.UserId));
    }

    [Fact]
    public async Task Mismatched_origin_is_rejected()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client);

        var start = await AuthenticationDriver.BeginAsync(client, account.Username);
        var assertion = account.Authenticator.Assert(
            start.Options, "https://passless.evil.example", account.UserId, signCount: 1);

        var response = await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Options_are_structurally_identical_for_known_and_unknown_usernames()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client);

        using var known = await AuthenticationDriver.BeginRawAsync(client, account.Username);
        using var unknown = await AuthenticationDriver.BeginRawAsync(client, $"nobody-{Guid.NewGuid():N}@example.test");

        Assert.Equal(known.StatusCode, unknown.StatusCode);

        var knownBody = await known.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>();

        // Same keys, in the same order.
        Assert.Equal(PropertyNames(knownBody), PropertyNames(unknownBody));

        // And the part that would otherwise give the game away: a real account
        // returns descriptors, so an invented one must too. An empty list here
        // would answer "does this account exist?" to anybody who asked.
        var knownAllow = knownBody.GetProperty("allowCredentials");
        var unknownAllow = unknownBody.GetProperty("allowCredentials");

        Assert.NotEmpty(knownAllow.EnumerateArray());
        Assert.NotEmpty(unknownAllow.EnumerateArray());
        Assert.Equal(
            PropertyNames(knownAllow.EnumerateArray().First()),
            PropertyNames(unknownAllow.EnumerateArray().First()));

        // Credential ids must be the same length, or the length is the oracle.
        Assert.Equal(
            knownAllow.EnumerateArray().First().GetProperty("id").GetString()!.Length,
            unknownAllow.EnumerateArray().First().GetProperty("id").GetString()!.Length);
    }

    [Fact]
    public async Task Decoys_for_the_same_unknown_username_are_stable()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var username = $"nobody-{Guid.NewGuid():N}@example.test";

        using var first = await AuthenticationDriver.BeginRawAsync(client, username);
        using var second = await AuthenticationDriver.BeginRawAsync(client, username);

        var firstIds = await CredentialIdsAsync(first);
        var secondIds = await CredentialIdsAsync(second);

        // Random decoys would differ between calls, and asking twice would sort
        // the invented accounts from the real ones immediately.
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task Every_failure_returns_the_same_body()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var account = await RegisterAsync(client);

        var unknownCredential = await FailWithUnknownCredentialAsync(client);
        var staleChallenge = await FailWithStaleChallengeAsync(client, account);
        var missingCeremony = await AuthenticationDriver.VerifyAsync(
            client,
            "not-a-real-handle",
            account.Authenticator.Assert(
                (await AuthenticationDriver.BeginAsync(client, account.Username)).Options,
                PasslessApiFactory.Origin,
                account.UserId,
                signCount: 2));

        var bodies = await Task.WhenAll(
            unknownCredential.Content.ReadAsStringAsync(),
            staleChallenge.Content.ReadAsStringAsync(),
            missingCeremony.Content.ReadAsStringAsync());

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Equal("""{"error":"authentication_failed"}""", bodies[0]);
        Assert.Single(new[] { unknownCredential.StatusCode, staleChallenge.StatusCode, missingCeremony.StatusCode }
            .Distinct());
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToList();

    private static async Task<List<string>> CredentialIdsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("allowCredentials")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToList();
    }

    private async Task<HttpResponseMessage> FailWithUnknownCredentialAsync(HttpClient client)
    {
        var start = await AuthenticationDriver.BeginAsync(client, username: null);
        var stranger = new SoftwareAuthenticator();
        return await AuthenticationDriver.VerifyAsync(
            client,
            start.CeremonyCookie,
            stranger.Assert(start.Options, PasslessApiFactory.Origin, Guid.NewGuid(), signCount: 1));
    }

    private async Task<HttpResponseMessage> FailWithStaleChallengeAsync(HttpClient client, Account account)
    {
        var start = await AuthenticationDriver.BeginAsync(client, account.Username);
        var assertion = account.Authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, account.UserId, signCount: 1);

        await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
        return await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
    }

    private static async Task AssertGenericFailureAsync(HttpResponseMessage response)
    {
        Assert.Equal("""{"error":"authentication_failed"}""", await response.Content.ReadAsStringAsync());
    }

    private async Task<Account> RegisterAsync(HttpClient client, SoftwareAuthenticator? authenticator = null)
    {
        authenticator ??= new SoftwareAuthenticator();
        var username = $"user-{Guid.NewGuid():N}@example.test";

        var start = await RegistrationDriver.BeginAsync(client, username);
        var response = await RegistrationDriver.VerifyAsync(
            client, start.CeremonyCookie, authenticator.Attest(start.Options, PasslessApiFactory.Origin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .SingleAsync(u => u.NormalizedUsername == User.NormalizeUsername(username));

        return new Account(username, user.Id, authenticator);
    }
}
