using Passless.Core.Abstractions;

namespace Passless.Api.Features.Tokens;

internal static class TokenEndpoints
{
    public static IEndpointRouteBuilder MapTokens(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/token").WithTags("Tokens");

        group.MapPost("/refresh", async (
            ITokenService tokens,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var presented = RefreshTokenCookie.Read(http.Request);
            if (string.IsNullOrEmpty(presented))
            {
                return Refuse(http);
            }

            var result = await tokens.RefreshAsync(presented, cancellationToken);
            if (!result.Succeeded)
            {
                return Refuse(http);
            }

            var issued = result.Tokens!;
            RefreshTokenCookie.Issue(http.Response, issued.RefreshToken, issued.RefreshTokenExpiresAt);

            return Results.Ok(new AccessTokenResponse(issued.AccessToken, issued.AccessTokenExpiresAt));
        });

        return app;
    }

    /// <summary>
    /// One response for every refusal — expired, unknown, and "you just tripped
    /// the reuse alarm and your family is gone" are indistinguishable.
    /// </summary>
    /// <remarks>
    /// Telling a caller that reuse was detected would confirm to whoever stole
    /// the token that the theft was noticed, and tell them precisely which token
    /// was the live one. The clearing of the cookie is not a tell either: it
    /// happens on every failure.
    /// </remarks>
    private static IResult Refuse(HttpContext http)
    {
        RefreshTokenCookie.Clear(http.Response);
        return Results.BadRequest(new { error = "refresh_failed" });
    }
}
