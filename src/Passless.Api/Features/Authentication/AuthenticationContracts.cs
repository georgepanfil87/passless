namespace Passless.Api.Features.Authentication;

/// <param name="Username">
/// Optional. Omitted for a usernameless ceremony, where the authenticator
/// resolves the account from its discoverable credentials and tells us which
/// user it was through the user handle.
/// </param>
public sealed record BeginAuthenticationRequest(string? Username);

/// <remarks>
/// Carries no tokens. Rotation lands next; until then a caller receives proof
/// that a session exists and nothing that could be mistaken for a credential.
/// </remarks>
public sealed record CompleteAuthenticationResponse(Guid SessionId);
