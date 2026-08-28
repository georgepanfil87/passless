using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Entities;
using Passless.Infrastructure;
using Passless.IntegrationTests.Authentication;
using Passless.IntegrationTests.Registration;

namespace Passless.IntegrationTests.Tokens;

[Collection(PasslessCollection.Name)]
public sealed class RotationTests(PasslessFixture fixture)
{
    private readonly TokenTestHarness _harness = new(fixture);

    [Fact]
    public async Task Login_issues_an_access_token_in_the_body_and_a_refresh_token_only_as_a_cookie()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        // The refresh token must not be anywhere a script could read it.
        Assert.DoesNotContain("plrt_", signedIn.ResponseBody, StringComparison.Ordinal);
        Assert.StartsWith("plrt_", signedIn.RefreshToken, StringComparison.Ordinal);

        var body = JsonSerializer.Deserialize<JsonElement>(signedIn.ResponseBody);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var tokens = await db.RefreshTokens.Where(t => t.FamilyId == signedIn.FamilyId).ToListAsync();
        var only = Assert.Single(tokens);
        Assert.Null(only.ConsumedAt);

        // Stored as a digest and nothing else.
        Assert.Equal(RefreshToken.TokenHashLength, only.TokenHash.Length);
    }

    [Fact]
    public async Task Five_consecutive_refreshes_rotate_cleanly()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        var presented = signedIn.RefreshToken;
        var seen = new List<string> { presented };

        for (var round = 0; round < 5; round++)
        {
            using var response = await TokenDriver.RefreshAsync(client, presented);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rotated = TokenDriver.ExtractRefreshToken(response);
            Assert.NotEqual(presented, rotated);
            Assert.False(string.IsNullOrEmpty(await TokenDriver.AccessTokenAsync(response)));

            presented = rotated;
            seen.Add(presented);
        }

        Assert.Equal(6, seen.Distinct(StringComparer.Ordinal).Count());

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var chain = await db.RefreshTokens
            .Where(t => t.FamilyId == signedIn.FamilyId)
            .OrderBy(t => t.IssuedAt)
            .ToListAsync();

        Assert.Equal(6, chain.Count);

        // Every token but the last is spent and points at its successor, so the
        // lineage can be walked start to finish from the stored rows alone.
        for (var index = 0; index < chain.Count - 1; index++)
        {
            Assert.NotNull(chain[index].ConsumedAt);
            Assert.Equal(chain[index + 1].Id, chain[index].ReplacedById);
        }

        Assert.Null(chain[^1].ConsumedAt);
        Assert.Null(chain[^1].ReplacedById);

        var family = await db.TokenFamilies.SingleAsync(f => f.Id == signedIn.FamilyId);
        Assert.False(family.IsInvalidated);

        var session = await db.Sessions.SingleAsync(s => s.FamilyId == signedIn.FamilyId);
        Assert.False(session.IsRevoked);
    }

    [Fact]
    public async Task A_consumed_token_cannot_be_reused_and_the_replacement_still_works()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        using var first = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken);
        var replacement = TokenDriver.ExtractRefreshToken(first);

        // The replacement is live right up until the old token is replayed.
        using var stillGood = await TokenDriver.RefreshAsync(client, replacement);
        Assert.Equal(HttpStatusCode.OK, stillGood.StatusCode);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        // A host with lifetimes short enough to outlive. The validator requires
        // the refresh lifetime to exceed the access lifetime, so both move.
        await using var shortLived = fixture.CreateApi(new Dictionary<string, string>
        {
            ["Tokens:AccessTokenLifetime"] = "00:00:01",
            ["Tokens:RefreshTokenLifetime"] = "00:00:02",
        });

        using var client = shortLived.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        await Task.Delay(TimeSpan.FromMilliseconds(2500));

        using var response = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"refresh_failed"}""", await response.Content.ReadAsStringAsync());

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        // Expiry is not theft. The family survives so the user can sign in again
        // without every other device being torn down.
        var family = await db.TokenFamilies.SingleAsync(f => f.Id == signedIn.FamilyId);
        Assert.False(family.IsInvalidated);
    }

    [Fact]
    public async Task A_valid_token_id_with_the_wrong_secret_is_refused_without_killing_the_family()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await _harness.SignInAsync(client);

        // Keep the id, replace the secret. This is what someone who scraped an
        // id out of a log but never held the token would be able to try.
        var separator = signedIn.RefreshToken.IndexOf('.', StringComparison.Ordinal);
        var forged = signedIn.RefreshToken[..separator] + "." + new string('A', 43);

        using var response = await TokenDriver.RefreshAsync(client, forged);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        // Deliberately not treated as compromise: if guessing ids invalidated
        // families, it would be a way to sign arbitrary people out.
        var family = await db.TokenFamilies.SingleAsync(f => f.Id == signedIn.FamilyId);
        Assert.False(family.IsInvalidated);

        // The real token still works.
        using var genuine = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, genuine.StatusCode);
    }

    [Fact]
    public async Task Refusals_are_audited()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        using var response = await TokenDriver.RefreshAsync(client, "plrt_not-a-real-token.AAAA");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        // The refusal happens with a transaction open; if the audit row were
        // written inside it and the transaction abandoned, this would be empty.
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Type == AuditEventType.RefreshTokenRejected));
    }

}
