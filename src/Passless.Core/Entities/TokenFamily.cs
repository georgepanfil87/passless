namespace Passless.Core.Entities;

/// <summary>
/// One lineage of refresh tokens, descended from a single sign-in.
///
/// The family is the unit of revocation, not the individual token. When a
/// consumed token is presented again there is no way to tell whether the caller
/// is the legitimate client replaying or the thief using a stolen copy, so the
/// only safe response is to invalidate everything descended from that sign-in
/// and make both parties authenticate again.
/// </summary>
public sealed class TokenFamily
{
    private TokenFamily()
    {
    }

    public TokenFamily(Guid id, Guid userId, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? InvalidatedAt { get; private set; }
    public TokenFamilyInvalidationReason? InvalidationReason { get; private set; }

    public bool IsInvalidated => InvalidatedAt is not null;

    /// <summary>
    /// Idempotent. A reuse-detection sweep can race with an ordinary sign-out,
    /// and the first reason recorded is the one that explains what happened;
    /// overwriting it would turn a theft into a routine logout in the audit log.
    /// </summary>
    public void Invalidate(TokenFamilyInvalidationReason reason, DateTimeOffset at)
    {
        if (IsInvalidated)
        {
            return;
        }

        InvalidatedAt = at;
        InvalidationReason = reason;
    }
}
