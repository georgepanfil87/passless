using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Configurations;

internal sealed class TokenFamilyConfiguration : IEntityTypeConfiguration<TokenFamily>
{
    public void Configure(EntityTypeBuilder<TokenFamily> builder)
    {
        builder.ToTable("token_families");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        // Stored by name. An integer-backed enum would let a future reordering
        // turn every historical "TokenReuseDetected" into "UserSignedOut".
        builder.Property(f => f.InvalidationReason)
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.HasIndex(f => f.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
