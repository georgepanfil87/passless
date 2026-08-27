using System.Net;
using Passless.Core.Entities;

namespace Passless.Core.Abstractions;

/// <param name="Metadata">
/// Context only. This lands in a table nobody can redact afterwards, so it must
/// never carry a token, a challenge, a key or a password.
/// </param>
public sealed record AuditEntry(
    AuditEventType Type,
    AuditSeverity Severity,
    Guid? UserId = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IPAddress? Ip = null,
    string? UserAgent = null);

public interface IAuditLog
{
    /// <summary>
    /// Adds the entry to the caller's unit of work, so the record and the change
    /// it describes commit together. A success audit that could be lost while the
    /// registration it describes succeeded would be worse than no audit at all.
    /// </summary>
    void Enlist(AuditEntry entry);

    /// <summary>
    /// Writes the entry on its own. For failure paths, where there is no other
    /// state change to commit alongside.
    /// </summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
