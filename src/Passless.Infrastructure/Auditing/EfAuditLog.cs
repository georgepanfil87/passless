using Passless.Core.Abstractions;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Auditing;

public sealed class EfAuditLog(PasslessDbContext db, TimeProvider time) : IAuditLog
{
    public void Enlist(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        db.AuditEvents.Add(ToEvent(entry));
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Enlist(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuditEvent ToEvent(AuditEntry entry) => new(
        Guid.NewGuid(),
        entry.UserId,
        entry.Type,
        entry.Severity,
        time.GetUtcNow(),
        entry.Metadata,
        entry.Ip,
        entry.UserAgent);
}
