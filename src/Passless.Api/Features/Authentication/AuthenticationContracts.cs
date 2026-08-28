namespace Passless.Api.Features.Authentication;

/// <param name="Username">
/// Optional. Omitted for a usernameless ceremony, where the authenticator
/// resolves the account from its discoverable credentials and tells us which
/// user it was through the user handle.
/// </param>
public sealed record BeginAuthenticationRequest(string? Username);

/// <remarks>
/// The refresh token is absent on purpose — it travels as an HttpOnly cookie so
/// that no script can read it. Only the short-lived access token is returned
/// here, for the client to hold in memory.
/// </remarks>
public sealed record CompleteAuthenticationResponse(
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);
