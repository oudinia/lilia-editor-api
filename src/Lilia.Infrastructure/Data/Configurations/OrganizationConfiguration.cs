using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(o => o.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(o => o.Slug).HasColumnName("slug").HasMaxLength(255).IsRequired();
        builder.Property(o => o.Logo).HasColumnName("logo");
        builder.Property(o => o.Metadata).HasColumnName("metadata");
        builder.Property(o => o.PaymentsCustomerId).HasColumnName("payments_customer_id").HasMaxLength(255);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(o => o.Slug).IsUnique();
    }
}
