using System.Net.Http.Json;
using Fido2NetLib;

namespace Passless.IntegrationTests.Registration;

/// <summary>
/// Drives the two halves of the registration ceremony over HTTP.
/// </summary>
/// <remarks>
/// Several tests need to replay a ceremony, or submit one twice at the same
/// instant, which means holding the ceremony cookie rather than letting a
/// cookie container manage it. The cookie value is captured from Set-Cookie and
/// attached by hand so that each request's contents are explicit.
/// </remarks>
internal sealed record CeremonyStart(CredentialCreateOptions Options, string CeremonyCookie);

internal static class RegistrationDriver
{
    public const string CookieName = "__Host-passless-ceremony";

    public static async Task<CeremonyStart> BeginAsync(
        HttpClient client,
        string username,
        string displayName = "Test User")
    {
        var response = await client.PostAsJsonAsync(
            "/register/options",
            new { username, displayName });

        response.EnsureSuccessStatusCode();

        var options = await response.Content.ReadFromJsonAsync<CredentialCreateOptions>();
        Assert.NotNull(options);
        Assert.NotEmpty(options!.Challenge);

        return new CeremonyStart(options, ExtractCeremonyCookie(response));
    }

    public static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client,
        string ceremonyCookie,
        AuthenticatorAttestationRawResponse attestation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/register/verify")
        {
            Content = JsonContent.Create(attestation),
        };

        request.Headers.Add("Cookie", $"{CookieName}={ceremonyCookie}");
        return client.SendAsync(request);
    }

    private static string ExtractCeremonyCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CookieName, StringComparison.Ordinal));

        // Also assert the attributes, because they are the security property:
        // a ceremony handle that is readable from script or sent cross-site
        // would defeat the point of binding the challenge to the browser.
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        return setCookie[(CookieName.Length + 1)..setCookie.IndexOf(';', StringComparison.Ordinal)];
    }
}
