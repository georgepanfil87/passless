using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Entities;

namespace Passless.IntegrationTests.Tokens;

/// <summary>
/// The behaviour this repository exists to demonstrate.
/// </summary>
[Collection(PasslessCollection.Name)]
public sealed class ReuseDetectionTests(PasslessFixture fixture)
{
    private readonly TokenTestHarness _harness = new(fixture);

    [Fact]
    public async Task Reuse_of_a_consumed_token_invalidates_the_entire_family()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        // Rotate a few times so there is a lineage to destroy, and so the token
        // being replayed is demonstrably an old one rather than the current one.
        var current = signedIn.RefreshToken;
        var stolen = signedIn.RefreshToken;

        for (var round = 0; round < 3; round++)
        {
            using var rotated = await TokenDriver.RefreshAsync(client, current);
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            current = TokenDriver.ExtractRefreshToken(rotated);
        }

        Assert.False((await _harness.FamilyAsync(signedIn.FamilyId)).IsInvalidated);

        // The first token, long since spent, comes back. There is no way to tell
        // the legitimate client replaying from a thief using a stolen copy, so
        // the whole lineage goes.
        using var replay = await TokenDriver.RefreshAsync(client, stolen);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // And the response gives nothing away: a caller cannot tell that they
        // just tripped the alarm rather than presenting something merely stale.
        Assert.Equal("""{"error":"refresh_failed"}""", await replay.Content.ReadAsStringAsync());

        var family = await _harness.FamilyAsync(signedIn.FamilyId);
        Assert.True(family.IsInvalidated);
        Assert.Equal(TokenFamilyInvalidationReason.TokenReuseDetected, family.InvalidationReason);

        // The token that was still live is dead too. That is the entire point:
        // refusing only the replayed request would leave whoever holds the newer
        // token signed in, and if that is the thief, the theft succeeded.
        using var afterwards = await TokenDriver.RefreshAsync(client, current);
        Assert.Equal(HttpStatusCode.BadRequest, afterwards.StatusCode);
    }

    [Fact]
    public async Task Reuse_detection_revokes_the_sessions_in_the_family()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        using (var rotated = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        }

        Assert.All(await _harness.SessionsAsync(signedIn.FamilyId), s => Assert.False(s.IsRevoked));

        using var replay = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // Invalidating the family without revoking its sessions would leave the
        // device signed in with a live access token and no way to refresh --
        // degraded, but not signed out.
        var sessions = await _harness.SessionsAsync(signedIn.FamilyId);
        Assert.NotEmpty(sessions);
        Assert.All(sessions, s => Assert.True(s.IsRevoked));
    }

    [Fact]
    public async Task Reuse_detection_writes_a_critical_audit_trail()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        using (var rotated = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        }

        using var replay = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        var events = await _harness.AuditAsync(signedIn.UserId);

        var detection = Assert.Single(events, e => e.Type == AuditEventType.RefreshTokenReuseDetected);
        Assert.Equal(AuditSeverity.Critical, detection.Severity);
        Assert.Equal(signedIn.FamilyId.ToString(), detection.Metadata["family"]);
        Assert.Equal("1", detection.Metadata["sessions_revoked"]);

        var invalidated = Assert.Single(events, e => e.Type == AuditEventType.TokenFamilyInvalidated);
        Assert.Equal(AuditSeverity.Critical, invalidated.Severity);
        Assert.Equal(nameof(TokenFamilyInvalidationReason.TokenReuseDetected), invalidated.Metadata["reason"]);

        Assert.Contains(events, e => e.Type == AuditEventType.SessionRevoked);

        // Nothing token-shaped may reach a table nobody can redact.
        Assert.All(events.SelectMany(e => e.Metadata.Values), value =>
            Assert.DoesNotContain("plrt_", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_refresh_with_the_same_token_yields_exactly_one_success()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        // Both requests carry the same live token and are in flight before
        // either response returns. The family row lock serialises them and the
        // conditional UPDATE decides the winner.
        var responses = await Task.WhenAll(
            TokenDriver.RefreshAsync(client, signedIn.RefreshToken),
            TokenDriver.RefreshAsync(client, signedIn.RefreshToken));

        var statuses = responses.Select(r => r.StatusCode).ToArray();

        Assert.Single(statuses, HttpStatusCode.OK);
        Assert.Single(statuses, HttpStatusCode.BadRequest);

        // The loser presented an already-consumed token, which under the strict
        // rule is indistinguishable from theft -- so the family dies even though
        // both requests came from the same honest client. This is the cost of the
        // rule, recorded here rather than hidden: a double-submitting client
        // signs itself out. A short grace window on the immediately preceding
        // token is the usual mitigation.
        var family = await _harness.FamilyAsync(signedIn.FamilyId);
        Assert.True(family.IsInvalidated);
        Assert.Equal(TokenFamilyInvalidationReason.TokenReuseDetected, family.InvalidationReason);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Concurrent_refresh_never_issues_two_live_tokens()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        var responses = await Task.WhenAll(
            TokenDriver.RefreshAsync(client, signedIn.RefreshToken),
            TokenDriver.RefreshAsync(client, signedIn.RefreshToken));

        // The property that matters even more than the status codes: the
        // original token was consumed once, so at most one replacement exists.
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Passless.Infrastructure.PasslessDbContext>();

        var issued = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(db.RefreshTokens.Where(t => t.FamilyId == signedIn.FamilyId));

        Assert.Equal(2, issued.Count);
        Assert.Equal(1, issued.Count(t => t.ConsumedAt is null));

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
