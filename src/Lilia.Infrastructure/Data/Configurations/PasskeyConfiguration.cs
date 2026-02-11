using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class PasskeyConfiguration : IEntityTypeConfiguration<Passkey>
{
    public void Configure(EntityTypeBuilder<Passkey> builder)
    {
        builder.ToTable("passkeys");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(p => p.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(p => p.PublicKey).HasColumnName("public_key").IsRequired();
        builder.Property(p => p.CredentialId).HasColumnName("credential_id").IsRequired();
        builder.Property(p => p.Counter).HasColumnName("counter");
        builder.Property(p => p.DeviceType).HasColumnName("device_type").HasMaxLength(255).IsRequired();
        builder.Property(p => p.BackedUp).HasColumnName("backed_up");
        builder.Property(p => p.Transports).HasColumnName("transports");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.Aaguid).HasColumnName("aaguid").HasMaxLength(255);

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.CredentialId);

        builder.HasOne(p => p.User)
            .WithMany(u => u.Passkeys)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
