using System.Net.Http.Json;
using Fido2NetLib;
using Passless.IntegrationTests.Registration;

namespace Passless.IntegrationTests.Authentication;

internal sealed record AssertionStart(AssertionOptions Options, string CeremonyCookie);

internal static class AuthenticationDriver
{
    /// <param name="username">Null for a usernameless ceremony.</param>
    public static async Task<AssertionStart> BeginAsync(HttpClient client, string? username)
    {
        var response = await client.PostAsJsonAsync("/login/options", new { username });
        response.EnsureSuccessStatusCode();

        var options = await response.Content.ReadFromJsonAsync<AssertionOptions>();
        Assert.NotNull(options);
        Assert.NotEmpty(options!.Challenge);

        return new AssertionStart(options, ExtractCeremonyCookie(response));
    }

    /// <summary>Begins a ceremony and returns the raw response, for shape comparisons.</summary>
    public static Task<HttpResponseMessage> BeginRawAsync(HttpClient client, string? username) =>
        client.PostAsJsonAsync("/login/options", new { username });

    public static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client,
        string ceremonyCookie,
        AuthenticatorAssertionRawResponse assertion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/login/verify")
        {
            Content = JsonContent.Create(assertion),
        };

        request.Headers.Add("Cookie", $"{RegistrationDriver.CookieName}={ceremonyCookie}");
        return client.SendAsync(request);
    }

    private static string ExtractCeremonyCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(RegistrationDriver.CookieName, StringComparison.Ordinal));

        return setCookie[(RegistrationDriver.CookieName.Length + 1)..setCookie.IndexOf(';', StringComparison.Ordinal)];
    }
}
