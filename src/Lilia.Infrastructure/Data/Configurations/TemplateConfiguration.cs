using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.Category).HasColumnName("category").HasMaxLength(50);
        builder.Property(t => t.Thumbnail).HasColumnName("thumbnail");
        builder.Property(t => t.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.IsPublic).HasColumnName("is_public").HasDefaultValue(false);
        builder.Property(t => t.IsSystem).HasColumnName("is_system").HasDefaultValue(false);
        builder.Property(t => t.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => t.Category);
        builder.HasIndex(t => t.IsPublic);
        builder.HasIndex(t => t.IsSystem);

        builder.HasOne(t => t.User)
            .WithMany(u => u.Templates)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
