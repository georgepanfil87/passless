using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        // Application-generated, so a whole object graph can be built before it
        // reaches the database. Stated explicitly rather than left to EF's
        // default for Guid keys, because it is a decision, not an accident.
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Username).HasMaxLength(320).IsRequired();
        builder.Property(u => u.NormalizedUsername).HasMaxLength(320).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();

        builder.Property(u => u.CreatedAt).IsRequired();

        builder.HasIndex(u => u.NormalizedUsername).IsUnique();
    }
}
