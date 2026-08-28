namespace Passless.Api.Features.Tokens;

/// <summary>
/// Carries the refresh token to the browser and back.
/// </summary>
/// <remarks>
/// A cookie rather than the response body, because the body would have to be
/// stored somewhere by the client, and every place a single-page application
/// can store a string is readable by any script running on the page. HttpOnly
/// takes the token out of reach of cross-site scripting entirely — script can
/// still *use* the session by making requests, but it cannot exfiltrate a
/// long-lived credential.
///
/// The short-lived access token goes in the body instead, to be held in memory.
/// </remarks>
internal static class RefreshTokenCookie
{
    public const string Name = "__Host-passless-refresh";

    public static void Issue(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,

        // The __Host- prefix requires Path=/, which is broader than the /token
        // scoping this would otherwise get. Taken deliberately: the prefix stops
        // a subdomain — including one an attacker owns after a takeover — from
        // writing a cookie of this name at all, and that is worth more than
        // narrowing the path of a cookie that is already HttpOnly and Strict.
        Path = "/",
        Expires = expiresAt,
        IsEssential = true,
    };
}
