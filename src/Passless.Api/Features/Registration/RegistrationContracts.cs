using System.ComponentModel.DataAnnotations;

namespace Passless.Api.Features.Registration;

public sealed record BeginRegistrationRequest(
    [property: Required, MaxLength(320)] string Username,
    [property: Required, MaxLength(256)] string DisplayName);

public sealed record CompleteRegistrationResponse(Guid CredentialId);
