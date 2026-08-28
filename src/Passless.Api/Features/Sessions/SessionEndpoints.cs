using Passless.Api.Features.Auth;
using Passless.Api.Features.Tokens;

namespace Passless.Api.Features.Sessions;

internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sessions").WithTags("Sessions").RequireAuthorization();

        group.MapGet("/", async (
            SessionService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!TryIdentify(http, out var userId, out var sessionId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await service.ListAsync(userId, sessionId, cancellationToken));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            SessionService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!TryIdentify(http, out var userId, out var sessionId))
            {
                return Results.Unauthorized();
            }

            var revoked = await service.RevokeAsync(userId, sessionId, id, cancellationToken);

            if (!revoked)
            {
                // Somebody else's session and a session that was never issued
                // return the same 404. Distinguishing them would turn this
                // endpoint into a way to test whether a session id is real.
                return Results.NotFound();
            }

            if (id == sessionId)
            {
                // Signing yourself out here: take the refresh cookie with it, so
                // the browser is not left holding a credential for a lineage
                // that has just been invalidated.
                RefreshTokenCookie.Clear(http.Response);
            }

            return Results.NoContent();
        });

        group.MapPost("/revoke-others", async (
            SessionService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            if (!TryIdentify(http, out var userId, out var sessionId))
            {
                return Results.Unauthorized();
            }

            var revoked = await service.RevokeOthersAsync(userId, sessionId, cancellationToken);
            return Results.Ok(new RevokeOthersResponse(revoked));
        });

        return app;
    }

    private static bool TryIdentify(HttpContext http, out Guid userId, out Guid sessionId)
    {
        var user = CurrentPrincipal.UserId(http.User);
        var session = CurrentPrincipal.SessionId(http.User);

        userId = user ?? Guid.Empty;
        sessionId = session ?? Guid.Empty;

        return user is not null && session is not null;
    }
}
