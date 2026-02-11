using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("purchases");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").HasMaxLength(255);
        builder.Property(p => p.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(p => p.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
        builder.Property(p => p.CustomerId).HasColumnName("customer_id").HasMaxLength(255).IsRequired();
        builder.Property(p => p.SubscriptionId).HasColumnName("subscription_id").HasMaxLength(255);
        builder.Property(p => p.ProductId).HasColumnName("product_id").HasMaxLength(255).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(50);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(p => p.SubscriptionId).IsUnique();

        builder.HasOne(p => p.Organization)
            .WithMany(o => o.Purchases)
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany(u => u.Purchases)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
