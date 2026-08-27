namespace Passless.Core.Abstractions;

/// <summary>Which ceremony a challenge was minted for.</summary>
public enum ChallengeKind
{
    Registration,
    Assertion,
}

/// <summary>
/// A challenge and the ceremony state that goes with it.
/// </summary>
/// <param name="Kind">
/// Checked on consumption. A registration challenge presented to the assertion
/// ceremony must not be accepted even if the key namespaces are ever confused —
/// the type of ceremony is part of what the challenge authorises.
/// </param>
/// <param name="Payload">
/// Opaque to the store. Holds the serialised ceremony options, which the
/// verification step needs in order to check the response against exactly what
/// was offered rather than against something reconstructed afterwards.
/// </param>
public sealed record ChallengeTicket(
    ChallengeKind Kind,
    string Payload,
    DateTimeOffset IssuedAt);

/// <summary>
/// Short-lived, single-use storage for ceremony challenges.
/// </summary>
public interface IChallengeStore
{
    /// <summary>Stores a ticket under <paramref name="ceremonyId"/> with a TTL.</summary>
    Task StoreAsync(
        string ceremonyId,
        ChallengeTicket ticket,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and destroys the ticket in one indivisible step.
    ///
    /// Single use is the whole contract. Implementations must not read and then
    /// delete: two concurrent submissions of the same challenge have to result
    /// in exactly one non-null return, and a read-then-delete leaves a window in
    /// which both callers see the ticket and both proceed.
    /// </summary>
    Task<ChallengeTicket?> ConsumeAsync(string ceremonyId, CancellationToken cancellationToken = default);
}
