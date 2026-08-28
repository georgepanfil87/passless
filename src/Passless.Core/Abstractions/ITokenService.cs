namespace Passless.Core.Abstractions;

/// <param name="RefreshToken">
/// The only moment this value exists in cleartext. It is handed to the caller,
/// set as a cookie, and forgotten; the database keeps a digest.
/// </param>
public sealed record IssuedTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public enum RefreshFailure
{
    /// <summary>Malformed, unknown id, or a secret that did not match.</summary>
    UnknownToken,

    Expired,

    /// <summary>The family was already invalidated, by reuse or by sign-out.</summary>
    FamilyInvalidated,

    /// <summary>A consumed token came back. The family is treated as stolen.</summary>
    ReuseDetected,
}

public readonly record struct RefreshResult(IssuedTokens? Tokens, RefreshFailure? Failure)
{
    public static RefreshResult Rotated(IssuedTokens tokens) => new(tokens, null);

    public static RefreshResult Refused(RefreshFailure failure) => new(null, failure);

    public bool Succeeded => Tokens is not null;
}

public interface ITokenService
{
    /// <summary>
    /// Adds the first refresh token of a new family to the caller's unit of
    /// work. Not saved here: it commits with the session and token family it
    /// belongs to, so a session can never exist without its lineage.
    /// </summary>
    IssuedTokens EnlistInitialTokens(Guid userId, Guid sessionId, Guid familyId);

    Task<RefreshResult> RefreshAsync(string presentedToken, CancellationToken cancellationToken = default);
}
