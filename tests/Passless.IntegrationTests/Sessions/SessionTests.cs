using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Entities;
using Passless.Infrastructure;
using Passless.IntegrationTests.Tokens;

namespace Passless.IntegrationTests.Sessions;

[Collection(PasslessCollection.Name)]
public sealed class SessionTests(PasslessFixture fixture)
{
    private readonly TokenTestHarness _harness = new(fixture);

    [Fact]
    public async Task Listing_shows_the_current_session_and_its_derived_device_label()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await SignInAsync(client, "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
            + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36");

        var sessions = await ListAsync(client, signedIn);
        var current = Assert.Single(sessions);

        Assert.Equal(signedIn.SessionId, current.GetProperty("id").GetGuid());
        Assert.True(current.GetProperty("isCurrent").GetBoolean());

        // Derived for humans, raw kept alongside it.
        Assert.Equal("Chrome 141 on macOS", current.GetProperty("deviceLabel").GetString());
        Assert.Contains("AppleWebKit", current.GetProperty("userAgent").GetString()!, StringComparison.Ordinal);

        // Coarse at most, and never coordinates.
        Assert.False(string.IsNullOrEmpty(current.GetProperty("location").GetString()));
        var raw = current.ToString();
        Assert.DoesNotContain("latitude", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Revoking_a_session_invalidates_its_refresh_family()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await SignInAsync(client);

        using var response = await Authorized(client, signedIn, HttpMethod.Delete, $"/sessions/{signedIn.SessionId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var family = await _harness.FamilyAsync(signedIn.FamilyId);
        Assert.True(family.IsInvalidated);
        Assert.Equal(TokenFamilyInvalidationReason.SessionRevoked, family.InvalidationReason);

        // And the refresh token it held is worthless, which is the half a
        // revocation would miss if it only touched the session row.
        using var refresh = await TokenDriver.RefreshAsync(client, signedIn.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task A_revoked_sessions_access_token_stops_working()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        var signedIn = await SignInAsync(client);

        using (var revoke = await Authorized(client, signedIn, HttpMethod.Delete, $"/sessions/{signedIn.SessionId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        // The same token that worked a moment ago. It is still validly signed
        // and still inside its lifetime -- the revocation cache is what stops it.
        using var afterwards = await Authorized(client, signedIn, HttpMethod.Get, "/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task Revoke_others_leaves_the_current_session_alive()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        // Three devices on one account: sign in once, then twice more with the
        // same credential from different user agents.
        var first = await SignInAsync(client, "Mozilla/5.0 (Macintosh) Chrome/141.0.0.0 Safari/537.36");
        // Chained, not both from `first`: each sign-in must present a counter
        // above the last one the server stored, or the assertion is refused as a
        // possible clone.
        var second = await SignInAgainAsync(client, first, "Mozilla/5.0 (iPhone) Version/17.0 Safari/605.1");
        var third = await SignInAgainAsync(client, second, "Mozilla/5.0 (X11; Linux) Firefox/114.0");

        Assert.Equal(3, (await ListAsync(client, third)).Count);

        using var response = await Authorized(client, third, HttpMethod.Post, "/sessions/revoke-others");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("revoked").GetInt32());

        var remaining = await ListAsync(client, third);
        var survivor = Assert.Single(remaining);
        Assert.Equal(third.SessionId, survivor.GetProperty("id").GetGuid());
        Assert.True(survivor.GetProperty("isCurrent").GetBoolean());

        // The others are properly gone, families and all.
        Assert.True((await _harness.FamilyAsync(first.FamilyId)).IsInvalidated);
        Assert.True((await _harness.FamilyAsync(second.FamilyId)).IsInvalidated);
        Assert.False((await _harness.FamilyAsync(third.FamilyId)).IsInvalidated);
    }

    [Fact]
    public async Task Revoking_someone_elses_session_is_refused_without_revealing_whether_it_exists()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        var mine = await SignInAsync(client);
        var theirs = await SignInAsync(client);

        // A real session id that belongs to another account.
        using var stranger = await Authorized(client, mine, HttpMethod.Delete, $"/sessions/{theirs.SessionId}");

        // An id that has never existed.
        using var imaginary = await Authorized(client, mine, HttpMethod.Delete, $"/sessions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, imaginary.StatusCode);
        Assert.Equal(
            await stranger.Content.ReadAsStringAsync(),
            await imaginary.Content.ReadAsStringAsync());

        // And the other account is untouched.
        Assert.False((await _harness.FamilyAsync(theirs.FamilyId)).IsInvalidated);
    }

    [Fact]
    public async Task Revocations_are_audited_with_their_scope()
    {
        using var client = fixture.Api.CreateCeremonyClient();

        var first = await SignInAsync(client);
        var second = await SignInAgainAsync(client, first, "Mozilla/5.0 (iPhone) Version/17.0 Safari/605.1");

        using (var others = await Authorized(client, second, HttpMethod.Post, "/sessions/revoke-others"))
        {
            Assert.Equal(HttpStatusCode.OK, others.StatusCode);
        }

        using (var self = await Authorized(client, second, HttpMethod.Delete, $"/sessions/{second.SessionId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, self.StatusCode);
        }

        var events = (await _harness.AuditAsync(second.UserId))
            .Where(e => e.Type == AuditEventType.SessionRevoked)
            .ToList();

        Assert.Contains(events, e => e.Metadata["scope"] == "all_others");
        Assert.Contains(events, e => e.Metadata["scope"] == "self");
        Assert.DoesNotContain(events, e => e.Metadata["scope"] == "other_device");
    }

    [Fact]
    public async Task Sessions_endpoints_require_a_token()
    {
        using var client = fixture.Api.CreateCeremonyClient();
        using var response = await client.GetAsync("/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<List<JsonElement>> ListAsync(HttpClient client, SignInResult signedIn)
    {
        using var response = await Authorized(client, signedIn, HttpMethod.Get, "/sessions");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().ToList();
    }

    private static Task<HttpResponseMessage> Authorized(
        HttpClient client,
        SignInResult signedIn,
        HttpMethod method,
        string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessTokenOf(signedIn));
        return client.SendAsync(request);
    }

    private static string AccessTokenOf(SignInResult signedIn) =>
        JsonSerializer.Deserialize<JsonElement>(signedIn.ResponseBody).GetProperty("accessToken").GetString()!;

    private Task<SignInResult> SignInAsync(HttpClient client, string? userAgent = null) =>
        _harness.SignInAsync(client, userAgent);

    private Task<SignInResult> SignInAgainAsync(HttpClient client, SignInResult existing, string userAgent) =>
        _harness.SignInAgainAsync(client, existing, userAgent);
}
