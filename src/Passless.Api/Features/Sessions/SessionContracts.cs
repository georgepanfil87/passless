namespace Passless.Api.Features.Sessions;

/// <param name="UserAgent">
/// The raw header, kept alongside the derived label so nothing is lost to a
/// parser that guessed wrong.
/// </param>
/// <param name="Location">
/// Coarse and derived at read time — city and country at most, resolved from the
/// address rather than stored beside it.
/// </param>
public sealed record SessionView(
    Guid Id,
    string DeviceLabel,
    string? UserAgent,
    string? City,
    string? Country,
    string Location,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent);

public sealed record RevokeOthersResponse(int Revoked);
