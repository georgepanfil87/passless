using System.Net;

namespace Passless.Core.Entities;

/// <summary>
/// One signed-in device. Bound to a token family so that revoking the session
/// and invalidating its refresh lineage are the same act — a session that could
/// be revoked while its refresh tokens kept working would not be a revocation.
/// </summary>
public sealed class Session
{
    private Session()
    {
    }

    public Session(
        Guid id,
        Guid userId,
        Guid familyId,
        string deviceLabel,
        string? userAgent,
        IPAddress? ip,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceLabel);

        Id = id;
        UserId = userId;
        FamilyId = familyId;
        DeviceLabel = deviceLabel;
        UserAgent = userAgent;
        Ip = ip;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }

    public string DeviceLabel { get; private set; } = null!;
    public string? UserAgent { get; private set; }

    /// <summary>
    /// Personal data. Retained so a user can recognise a session they do not
    /// own; it belongs in the retention policy the threat model sets out.
    /// </summary>
    public IPAddress? Ip { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public void Touch(DateTimeOffset at)
    {
        if (IsRevoked)
        {
            throw new InvalidOperationException($"Session {Id} was revoked at {RevokedAt:O}.");
        }

        LastSeenAt = at;
    }

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}
