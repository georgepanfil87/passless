using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Entities;
using Passless.Infrastructure;
using Passless.IntegrationTests.Authentication;
using Passless.IntegrationTests.Registration;

namespace Passless.IntegrationTests.Tokens;

internal sealed record SignInResult(
    Guid UserId,
    Guid SessionId,
    Guid FamilyId,
    string RefreshToken,
    string ResponseBody,
    string Username,
    SoftwareAuthenticator Authenticator,
    uint SignCount);

/// <summary>Registers an account and signs it in, returning the first token pair.</summary>
internal sealed class TokenTestHarness(PasslessFixture fixture)
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    public async Task<SignInResult> SignInAsync(HttpClient client, string? userAgent = null)
    {
        UseUserAgent(client, userAgent ?? DefaultUserAgent);

        var authenticator = new SoftwareAuthenticator();
        var username = $"user-{Guid.NewGuid():N}@example.test";

        var registration = await RegistrationDriver.BeginAsync(client, username);
        using (var registered = await RegistrationDriver.VerifyAsync(
                   client,
                   registration.CeremonyCookie,
                   authenticator.Attest(registration.Options, PasslessApiFactory.Origin)))
        {
            Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        }

        var userId = await UserIdAsync(username);
        return await LogInAsync(client, username, userId, authenticator, signCount: 1);
    }

    /// <summary>
    /// Signs the same account in again from a different device, producing a
    /// second session and a second token family.
    /// </summary>
    public async Task<SignInResult> SignInAgainAsync(
        HttpClient client,
        SignInResult existing,
        string userAgent)
    {
        UseUserAgent(client, userAgent);

        // The counter has to advance or the assertion is refused as a possible
        // clone, which is the rule working rather than the test misbehaving.
        return await LogInAsync(
            client,
            existing.Username,
            existing.UserId,
            existing.Authenticator,
            existing.SignCount + 1);
    }

    public async Task<TokenFamily> FamilyAsync(Guid familyId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        return await db.TokenFamilies.AsNoTracking().SingleAsync(f => f.Id == familyId);
    }

    public async Task<List<Session>> SessionsAsync(Guid familyId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        return await db.Sessions.AsNoTracking().Where(s => s.FamilyId == familyId).ToListAsync();
    }

    public async Task<List<AuditEvent>> AuditAsync(Guid userId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        return await db.AuditEvents.AsNoTracking().Where(e => e.UserId == userId).ToListAsync();
    }

    private async Task<SignInResult> LogInAsync(
        HttpClient client,
        string username,
        Guid userId,
        SoftwareAuthenticator authenticator,
        uint signCount)
    {
        var login = await AuthenticationDriver.BeginAsync(client, username);
        using var response = await AuthenticationDriver.VerifyAsync(
            client,
            login.CeremonyCookie,
            authenticator.Assert(login.Options, PasslessApiFactory.Origin, userId, signCount));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var sessionId = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("sessionId").GetGuid();

        return new SignInResult(
            userId,
            sessionId,
            await FamilyIdAsync(sessionId),
            TokenDriver.ExtractRefreshToken(response),
            body,
            username,
            authenticator,
            signCount);
    }

    private static void UseUserAgent(HttpClient client, string userAgent)
    {
        client.DefaultRequestHeaders.Remove("User-Agent");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    }

    private async Task<Guid> UserIdAsync(string username)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        return (await db.Users.AsNoTracking()
            .SingleAsync(u => u.NormalizedUsername == User.NormalizeUsername(username))).Id;
    }

    private async Task<Guid> FamilyIdAsync(Guid sessionId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        return (await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).FamilyId;
    }
}
