using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class SnippetConfiguration : IEntityTypeConfiguration<Snippet>
{
    public void Configure(EntityTypeBuilder<Snippet> builder)
    {
        builder.ToTable("snippets");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(s => s.Description).HasColumnName("description");
        builder.Property(s => s.LatexContent).HasColumnName("latex_content").IsRequired();
        builder.Property(s => s.BlockType).HasColumnName("block_type").HasMaxLength(50).IsRequired();
        builder.Property(s => s.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        builder.Property(s => s.RequiredPackages).HasColumnName("required_packages").HasColumnType("jsonb");
        builder.Property(s => s.Preamble).HasColumnName("preamble");
        builder.Property(s => s.Tags).HasColumnName("tags").HasColumnType("jsonb");
        builder.Property(s => s.IsFavorite).HasColumnName("is_favorite").HasDefaultValue(false);
        builder.Property(s => s.IsSystem).HasColumnName("is_system").HasDefaultValue(false);
        builder.Property(s => s.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.Category);
        builder.HasIndex(s => s.IsSystem);
        builder.HasIndex(s => new { s.UserId, s.Category });

        builder.HasOne(s => s.User)
            .WithMany(u => u.Snippets)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
