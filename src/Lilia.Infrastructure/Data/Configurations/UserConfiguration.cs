using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(u => u.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(u => u.Image).HasColumnName("image");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(u => u.Username).HasColumnName("username").HasMaxLength(255);
        builder.Property(u => u.DisplayUsername).HasColumnName("display_username").HasMaxLength(255);
        builder.Property(u => u.Role).HasColumnName("role").HasMaxLength(50);
        builder.Property(u => u.Banned).HasColumnName("banned").HasDefaultValue(false);
        builder.Property(u => u.BanReason).HasColumnName("ban_reason");
        builder.Property(u => u.BanExpires).HasColumnName("ban_expires");
        builder.Property(u => u.TwoFactorEnabled).HasColumnName("two_factor_enabled").HasDefaultValue(false);
        builder.Property(u => u.OnboardingComplete).HasColumnName("onboarding_complete");
        builder.Property(u => u.PaymentsCustomerId).HasColumnName("payments_customer_id").HasMaxLength(255);
        builder.Property(u => u.Locale).HasColumnName("locale").HasMaxLength(10);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.Username).IsUnique();
    }
}
