using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // By name, for the same reason as everywhere else, and with more at
        // stake: this table is the record of what happened.
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(e => e.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.Ip).HasColumnType("inet");
        builder.Property(e => e.UserAgent).HasMaxLength(512);

        builder.Property(e => e.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, MetadataJson),
                json => Deserialize(json),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => Equal(left, right),
                    value => value.Count,
                    value => Deserialize(JsonSerializer.Serialize(value, MetadataJson))))
            .IsRequired();

        // The required index: "what happened to this user, most recent first".
        builder.HasIndex(e => new { e.UserId, e.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_events_user_id_occurred_at");

        // Serves the CRITICAL filter the activity screen already offers.
        builder.HasIndex(e => new { e.Severity, e.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_audit_events_severity_occurred_at");

        // RESTRICT, not SET NULL. Nulling the column on user deletion would be an
        // UPDATE, and the append-only trigger rejects those — so the two rules
        // would deadlock at exactly the moment someone tried to erase an account.
        // Users are disabled, not deleted; this makes that the only option.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static IReadOnlyDictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, MetadataJson)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static bool Equal(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
