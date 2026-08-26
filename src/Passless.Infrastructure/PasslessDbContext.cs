using Microsoft.EntityFrameworkCore;
using Passless.Core.Entities;

namespace Passless.Infrastructure;

public sealed class PasslessDbContext(DbContextOptions<PasslessDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<TokenFamily> TokenFamilies => Set<TokenFamily>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>
    /// Append and read only. <see cref="AuditEvent"/> exposes no mutator, and
    /// <see cref="GuardAuditAppendOnly"/> refuses to save an update or a delete
    /// that reached the change tracker some other way.
    /// </summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PasslessDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        GuardAuditAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fails loudly and locally. The database trigger is the real guarantee, but
    /// it surfaces as a Postgres exception from deep inside SaveChanges with no
    /// indication of which entity caused it; this names the row while the stack
    /// still points at the code that tried.
    /// </summary>
    private void GuardAuditAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"Audit events are append-only. Attempted to {entry.State.ToString().ToUpperInvariant()} " +
                    $"event {entry.Entity.Id}. Record a new event instead of changing an old one.");
            }
        }
    }
}
