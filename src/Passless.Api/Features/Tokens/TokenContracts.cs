namespace Passless.Api.Features.Tokens;

/// <remarks>
/// Carries no refresh token. That one travels only as an HttpOnly cookie, so it
/// never passes through anywhere script can read it.
/// </remarks>
public sealed record AccessTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string TokenType = "Bearer");
