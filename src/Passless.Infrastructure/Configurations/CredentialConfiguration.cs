using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Configurations;

internal sealed class CredentialConfiguration : IEntityTypeConfiguration<Credential>
{
    public void Configure(EntityTypeBuilder<Credential> builder)
    {
        builder.ToTable("credentials");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.CredentialId).HasColumnType("bytea").IsRequired();
        builder.Property(c => c.PublicKey).HasColumnType("bytea").IsRequired();

        // WebAuthn signCount is an unsigned 32-bit integer and PostgreSQL has no
        // unsigned types. Mapped to bigint explicitly: Npgsql's default mapping
        // for uint is oid, which would silently become a 32-bit signed column
        // and wrap on any authenticator that passes 2^31.
        builder.Property(c => c.SignatureCounter)
            .HasConversion(v => (long)v, v => (uint)v)
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(c => c.Aaguid).IsRequired();

        // text[] rather than a delimited string: transports are a set, and a
        // delimiter is one authenticator away from being part of a value.
        builder.Property(c => c.Transports).HasColumnType("text[]").IsRequired();

        builder.Property(c => c.FriendlyName).HasMaxLength(128);

        // The constraint that matters. Global, not per user: the same credential
        // resolving to two accounts would let an assertion pick whichever one
        // the lookup happened to return first.
        builder.HasIndex(c => c.CredentialId).IsUnique();

        builder.HasIndex(c => c.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
