using System.Collections.ObjectModel;
using System.Net;

namespace Passless.Core.Entities;

/// <summary>
/// An immutable record that something security-relevant happened.
///
/// The type has no mutator of any kind — no setter, no method, no collection
/// that can be added to after construction. That is the "no update path exists
/// in code" half of the guarantee. The other half is a database trigger that
/// rejects UPDATE, DELETE and TRUNCATE, because a guarantee that only holds for
/// callers who go through this assembly is not a guarantee about the data.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public AuditEvent(
        Guid id,
        Guid? userId,
        AuditEventType type,
        AuditSeverity severity,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, string>? metadata = null,
        IPAddress? ip = null,
        string? userAgent = null)
    {
        Id = id;
        UserId = userId;
        Type = type;
        Severity = severity;
        OccurredAt = occurredAt;
        Metadata = Freeze(metadata);
        Ip = ip;
        UserAgent = userAgent;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Nullable: failures worth recording happen before any account is known —
    /// an assertion for a credential that does not resolve, for instance.
    /// The foreign key restricts deletes rather than nulling them, because
    /// ON DELETE SET NULL is an UPDATE and this table refuses those.
    /// </summary>
    public Guid? UserId { get; private set; }

    public AuditEventType Type { get; private set; }
    public AuditSeverity Severity { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// Context for the event, stored as jsonb.
    ///
    /// Whatever is put here ends up in a table nobody can redact afterwards, so
    /// it must never carry a token, a challenge, a key, or a password — the
    /// append-only guarantee cuts both ways. Callers pass identifiers and
    /// descriptions; the values themselves stay out.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; private set; } =
        ReadOnlyDictionary<string, string>.Empty;

    public IPAddress? Ip { get; private set; }
    public string? UserAgent { get; private set; }

    private static IReadOnlyDictionary<string, string> Freeze(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return ReadOnlyDictionary<string, string>.Empty;
        }

        // Copied, not aliased: the caller keeps a reference to whatever it passed
        // and could otherwise mutate an "immutable" record after the fact.
        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(metadata, StringComparer.Ordinal));
    }
}
