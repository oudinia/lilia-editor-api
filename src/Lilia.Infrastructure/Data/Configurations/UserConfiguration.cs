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

        builder.HasIndex(u => u.Email).IsUnique();
    }
}
