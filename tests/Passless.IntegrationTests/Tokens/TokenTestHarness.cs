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
    string ResponseBody);

/// <summary>Registers an account and signs it in, returning the first token pair.</summary>
internal sealed class TokenTestHarness(PasslessFixture fixture)
{
    public async Task<SignInResult> SignInAsync(HttpClient client)
    {
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

        var login = await AuthenticationDriver.BeginAsync(client, username);
        using var response = await AuthenticationDriver.VerifyAsync(
            client,
            login.CeremonyCookie,
            authenticator.Assert(login.Options, PasslessApiFactory.Origin, userId, signCount: 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var sessionId = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("sessionId").GetGuid();

        return new SignInResult(
            userId,
            sessionId,
            await FamilyIdAsync(sessionId),
            TokenDriver.ExtractRefreshToken(response),
            body);
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
