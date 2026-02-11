using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class VerificationConfiguration : IEntityTypeConfiguration<Verification>
{
    public void Configure(EntityTypeBuilder<Verification> builder)
    {
        builder.ToTable("verifications");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(v => v.Identifier).HasColumnName("identifier").IsRequired();
        builder.Property(v => v.Value).HasColumnName("value").IsRequired();
        builder.Property(v => v.ExpiresAt).HasColumnName("expires_at");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(v => v.Identifier);
    }
}
