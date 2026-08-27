using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;

namespace Passless.Api.Features.WebAuthn;

/// <summary>
/// Binds a challenge to the browser that asked for it.
///
/// A challenge on its own proves only that the server issued it; anyone who
/// obtains one could otherwise complete the ceremony. Storing the challenge
/// under a handle that lives in an HttpOnly cookie means the browser holding
/// the cookie is the only one that can finish what it started. Before sign-in
/// there is no session to bind to, so this cookie *is* the session for the
/// length of one ceremony.
/// </summary>
internal static class CeremonyCookie
{
    /// <summary>
    /// The __Host- prefix is enforced by the browser: it refuses the cookie
    /// unless it is Secure, has Path=/ and carries no Domain attribute. That
    /// makes it impossible for a subdomain — including one an attacker controls
    /// after a subdomain takeover — to write a ceremony handle of its choosing.
    /// </summary>
    public const string Name = "__Host-passless-ceremony";

    public static string Issue(HttpResponse response, TimeSpan lifetime)
    {
        // 256 bits from a CSPRNG. The handle is a bearer value for the length of
        // one ceremony, so it needs to be unguessable, not merely unique.
        var ceremonyId = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        response.Cookies.Append(Name, ceremonyId, Options(lifetime));
        return ceremonyId;
    }

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        Secure = true,
        // Strict, not Lax: a ceremony is never legitimately started by a
        // navigation from another site, so there is no flow to break.
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = lifetime,
        IsEssential = true,
    };
}
