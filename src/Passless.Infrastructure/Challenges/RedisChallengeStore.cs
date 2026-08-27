using System.Text.Json;
using Passless.Core.Abstractions;
using StackExchange.Redis;

namespace Passless.Infrastructure.Challenges;

/// <summary>
/// Challenge storage backed by Redis.
/// </summary>
/// <remarks>
/// Redis rather than Postgres because the lifecycle is a perfect fit: a value
/// that must vanish on a deadline and be readable exactly once. Expiry is the
/// server's job rather than a sweeper job of ours, and GETDEL gives single-use
/// semantics without a transaction.
/// </remarks>
public sealed class RedisChallengeStore(IConnectionMultiplexer redis) : IChallengeStore
{
    private const string KeyPrefix = "passless:challenge:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StoreAsync(
        string ceremonyId,
        ChallengeTicket ticket,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ceremonyId);
        ArgumentNullException.ThrowIfNull(ticket);

        var stored = await redis.GetDatabase().StringSetAsync(
            KeyFor(ceremonyId),
            JsonSerializer.Serialize(ticket, Json),
            timeToLive,
            // NotExists: a ceremony id is single-use on the way in as well. If one
            // is ever reissued, the second write fails loudly rather than silently
            // replacing a challenge that is already in flight.
            when: When.NotExists);

        if (!stored)
        {
            throw new InvalidOperationException(
                $"A challenge is already stored for ceremony {ceremonyId}.");
        }
    }

    public async Task<ChallengeTicket?> ConsumeAsync(
        string ceremonyId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ceremonyId))
        {
            return null;
        }

        // GETDEL. One round trip, executed atomically by the single-threaded
        // command loop, so concurrent callers cannot both receive the value:
        // whichever command the server runs second sees the key already gone.
        // A GET followed by a DEL would leave exactly the window this ceremony
        // must not have.
        var value = await redis.GetDatabase().StringGetDeleteAsync(KeyFor(ceremonyId));

        // Indistinguishable outcomes on purpose. Never issued, already consumed,
        // and expired all arrive here as a null, and the caller reports one
        // failure for all three — the difference is exactly what a replay probe
        // would want to learn.
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<ChallengeTicket>(value!, Json);
    }

    private static RedisKey KeyFor(string ceremonyId) => KeyPrefix + ceremonyId;
}
