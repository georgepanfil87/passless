using Fido2NetLib;
using Passless.Api.Features.Tokens;

namespace Passless.Api.Features.Authentication;

internal static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthentication(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/login").WithTags("Authentication");

        group.MapPost("/options", async (
            BeginAuthenticationRequest request,
            AuthenticationService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            // No validation branch on the username. An empty body is a
            // usernameless ceremony, and any non-empty value is answered the
            // same way whether or not it names an account — including the
            // decoys, which exist so that this response never varies in shape.
            var options = await service.BeginAsync(request, http, cancellationToken);
            return Results.Ok(options);
        });

        group.MapPost("/verify", async (
            AuthenticatorAssertionRawResponse assertion,
            AuthenticationService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CompleteAsync(assertion, http, cancellationToken);

            // One body for every failure. An unknown credential, a stale
            // challenge, a disabled account and a cloned authenticator are the
            // same response; only the audit log tells them apart.
            if (!outcome.Succeeded)
            {
                return Results.BadRequest(new { error = "authentication_failed" });
            }

            var issued = outcome.Tokens!;
            RefreshTokenCookie.Issue(http.Response, issued.RefreshToken, issued.RefreshTokenExpiresAt);

            return Results.Ok(new CompleteAuthenticationResponse(
                outcome.SessionId,
                issued.AccessToken,
                issued.AccessTokenExpiresAt));
        });

        return app;
    }
}
