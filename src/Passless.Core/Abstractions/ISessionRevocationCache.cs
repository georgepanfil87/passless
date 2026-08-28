namespace Passless.Core.Abstractions;

/// <summary>
/// The list of sessions whose access tokens must stop being honoured.
/// </summary>
/// <remarks>
/// A signed JWT cannot be withdrawn once issued, so revoking a session cannot
/// reach the tokens already in the wild. This closes that gap by asking a cache
/// on each request whether the session behind the token is still alive.
///
/// Entries expire after the access-token lifetime, which is the point at which
/// any token naming that session has expired on its own and the entry stops
/// meaning anything. The cache therefore never needs sweeping and never grows
/// beyond the sessions revoked in the last few minutes.
/// </remarks>
public interface ISessionRevocationCache
{
    Task RevokeAsync(Guid sessionId, TimeSpan window, CancellationToken cancellationToken = default);

    Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
