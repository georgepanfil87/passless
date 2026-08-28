using Microsoft.Extensions.Logging;
using Passless.Core.Abstractions;
using StackExchange.Redis;

namespace Passless.Infrastructure.Sessions;

public sealed class RedisSessionRevocationCache(
    IConnectionMultiplexer redis,
    ILogger<RedisSessionRevocationCache> logger) : ISessionRevocationCache
{
    private const string KeyPrefix = "passless:revoked-session:";

    public async Task RevokeAsync(
        Guid sessionId,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        // Not guarded. A revocation that fails to reach the cache must surface
        // as an error the caller sees, because the alternative is telling
        // somebody their device is signed out when it is not.
        await redis.GetDatabase().StringSetAsync(KeyFor(sessionId), "1", window);
    }

    public async Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await redis.GetDatabase().KeyExistsAsync(KeyFor(sessionId));
        }
        catch (RedisException exception)
        {
            // Fail open, deliberately.
            //
            // Failing closed would turn a cache outage into a total
            // authentication outage: every request in the product would be
            // rejected because one optional component is unreachable. Failing
            // open costs a bounded amount — a revoked session keeps working
            // until its access token expires, at most the access-token
            // lifetime — and that is the same guarantee the system would offer
            // if this cache did not exist at all.
            //
            // Logged at warning rather than swallowed, because the window this
            // opens is real and somebody should know it opened.
            logger.LogWarning(
                exception,
                "Session revocation cache unreachable; honouring the access token. "
                + "Revoked sessions remain usable until their tokens expire.");

            return false;
        }
    }

    private static RedisKey KeyFor(Guid sessionId) => KeyPrefix + sessionId.ToString("N");
}
