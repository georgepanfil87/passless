namespace Passless.Core.Entities;

/// <summary>
/// One issued refresh token, stored only as a hash.
///
/// There is deliberately no plaintext property anywhere on this type. The token
/// exists in cleartext exactly once, in the response that hands it to the
/// client; after that only its digest is retained. A property holding the token
/// would be serialised into logs, exception dumps and debugger output for free,
/// so the way to keep it out of those places is for it not to exist.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>SHA-256. Fixed here so a shorter digest cannot be stored by mistake.</summary>
    public const int TokenHashLength = 32;

    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid familyId,
        byte[] tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        if (tokenHash.Length != TokenHashLength)
        {
            throw new ArgumentException(
                $"Token hash must be {TokenHashLength} bytes; got {tokenHash.Length}. " +
                "This constructor takes a digest, never a token.",
                nameof(tokenHash));
        }

        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException("Expiry must be after issue.", nameof(expiresAt));
        }

        Id = id;
        FamilyId = familyId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid FamilyId { get; private set; }

    /// <summary>
    /// The digest of the presented token is looked up through a unique index,
    /// which is not a constant-time comparison.
    ///
    /// Named as an exception rather than passed over: the ordinary reason to
    /// compare secrets in constant time is that an attacker can iterate, learning
    /// the value a byte at a time from how long the comparison runs. That attack
    /// needs control over the preimage. Here the token is 256 bits of CSPRNG
    /// output and the comparison happens against its SHA-256 digest, so an
    /// attacker cannot steer the compared bytes and has nothing to iterate
    /// toward. Password and challenge comparisons get no such exemption.
    /// </summary>
    public byte[] TokenHash { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>The token minted when this one was rotated away.</summary>
    public Guid? ReplacedById { get; private set; }

    public bool IsConsumed => ConsumedAt is not null;

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// Marks this token spent. Refuses to run twice: a second call would erase
    /// the evidence that the token had already been used, which is the single
    /// fact reuse detection depends on.
    /// </summary>
    public void Consume(Guid replacedById, DateTimeOffset at)
    {
        if (IsConsumed)
        {
            throw new InvalidOperationException(
                $"Refresh token {Id} was already consumed at {ConsumedAt:O}.");
        }

        ConsumedAt = at;
        ReplacedById = replacedById;
    }
}
