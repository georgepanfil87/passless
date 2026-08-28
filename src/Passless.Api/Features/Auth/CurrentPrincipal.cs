using System.Security.Claims;

namespace Passless.Api.Features.Auth;

/// <summary>
/// Reads the two identifiers every authenticated request carries.
/// </summary>
/// <remarks>
/// Inbound claim mapping is switched off in the bearer setup, so these are the
/// JWT's own names rather than the legacy WS-Federation URIs .NET substitutes by
/// default. Worth knowing when reading a token by hand: with mapping on, "sub"
/// silently becomes a claim type that is a URL.
/// </remarks>
internal static class CurrentPrincipal
{
    public const string SessionClaim = "sid";

    public static Guid? UserId(ClaimsPrincipal principal) => Read(principal, "sub");

    public static Guid? SessionId(ClaimsPrincipal principal) => Read(principal, SessionClaim);

    private static Guid? Read(ClaimsPrincipal principal, string claim) =>
        Guid.TryParse(principal.FindFirst(claim)?.Value, out var value) ? value : null;
}
