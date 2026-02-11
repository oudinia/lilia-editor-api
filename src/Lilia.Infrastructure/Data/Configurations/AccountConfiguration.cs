using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(a => a.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(a => a.AccountId).HasColumnName("account_id").HasMaxLength(255).IsRequired();
        builder.Property(a => a.ProviderId).HasColumnName("provider_id").HasMaxLength(255).IsRequired();
        builder.Property(a => a.AccessToken).HasColumnName("access_token");
        builder.Property(a => a.RefreshToken).HasColumnName("refresh_token");
        builder.Property(a => a.IdToken).HasColumnName("id_token");
        builder.Property(a => a.AccessTokenExpiresAt).HasColumnName("access_token_expires_at");
        builder.Property(a => a.RefreshTokenExpiresAt).HasColumnName("refresh_token_expires_at");
        builder.Property(a => a.Scope).HasColumnName("scope");
        builder.Property(a => a.Password).HasColumnName("password");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(a => a.UserId);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Accounts)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
