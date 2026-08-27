using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.DeviceLabel).HasMaxLength(128).IsRequired();
        builder.Property(s => s.UserAgent).HasMaxLength(512);

        // inet, not text: it validates on the way in and supports subnet queries
        // when an incident needs "everything from this /24".
        builder.Property(s => s.Ip).HasColumnType("inet");

        // One family per session. A session whose refresh lineage outlived it
        // would be revocable in name only.
        builder.HasIndex(s => s.FamilyId).IsUnique();

        // Partial index: "show me my active devices" is the only listing this
        // table serves, and revoked rows are dead weight in it.
        builder.HasIndex(s => s.UserId)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_sessions_user_id_active");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TokenFamily>()
            .WithMany()
            .HasForeignKey(s => s.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
