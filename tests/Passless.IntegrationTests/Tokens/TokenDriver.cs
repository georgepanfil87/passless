using System.Net.Http.Json;
using System.Text.Json;
using Passless.IntegrationTests.Authentication;
using Passless.IntegrationTests.Registration;

namespace Passless.IntegrationTests.Tokens;

internal sealed record SignedIn(
    string Username,
    Guid UserId,
    Guid SessionId,
    Guid FamilyId,
    string RefreshToken,
    SoftwareAuthenticator Authenticator);

internal static class TokenDriver
{
    public const string CookieName = "__Host-passless-refresh";

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/token/refresh");
        request.Headers.Add("Cookie", $"{CookieName}={refreshToken}");
        return client.SendAsync(request);
    }

    public static string ExtractRefreshToken(HttpResponseMessage response)
    {
        var setCookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CookieName, StringComparison.Ordinal));

        // The attributes are the protection, so assert them rather than trusting
        // that they were set once and never regressed.
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        return setCookie[(CookieName.Length + 1)..setCookie.IndexOf(';', StringComparison.Ordinal)];
    }

    public static async Task<string> AccessTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    /// <summary>Registers an account, signs in, and returns the first token pair.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client,
        SoftwareAuthenticator authenticator,
        string username,
        Guid userId,
        uint signCount)
    {
        var start = await AuthenticationDriver.BeginAsync(client, username);
        var assertion = authenticator.Assert(
            start.Options, PasslessApiFactory.Origin, userId, signCount);

        return await AuthenticationDriver.VerifyAsync(client, start.CeremonyCookie, assertion);
    }
}
