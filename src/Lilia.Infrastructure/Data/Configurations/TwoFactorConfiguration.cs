using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class TwoFactorConfiguration : IEntityTypeConfiguration<TwoFactor>
{
    public void Configure(EntityTypeBuilder<TwoFactor> builder)
    {
        builder.ToTable("two_factors");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(t => t.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Secret).HasColumnName("secret").IsRequired();
        builder.Property(t => t.BackupCodes).HasColumnName("backup_codes").IsRequired();

        builder.HasIndex(t => t.Secret);
        builder.HasIndex(t => t.UserId);

        builder.HasOne(t => t.User)
            .WithMany(u => u.TwoFactors)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
