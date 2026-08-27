using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // Fixed-width bytea. There is no column for the token itself, and the
        // entity has no property that could hold one.
        builder.Property(t => t.TokenHash)
            .HasColumnType("bytea")
            .IsRequired();

        // Unique, and the only lookup path: a presented token is hashed and the
        // digest matched here.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasIndex(t => t.FamilyId);

        // No EF relationship for ReplacedById. The constraint is real but has to
        // be DEFERRABLE, because deleting a family cascades to every token in it
        // and an immediate check would trip on rows that point at siblings
        // already removed in the same statement. EF cannot model deferrable
        // constraints, so it is added as SQL in the migration.
        builder.HasIndex(t => t.ReplacedById);

        builder.HasOne<TokenFamily>()
            .WithMany()
            .HasForeignKey(t => t.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
