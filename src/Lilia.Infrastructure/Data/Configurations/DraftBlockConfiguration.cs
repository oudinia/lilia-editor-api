using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DraftBlockConfiguration : IEntityTypeConfiguration<DraftBlock>
{
    public void Configure(EntityTypeBuilder<DraftBlock> builder)
    {
        builder.ToTable("draft_blocks");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(d => d.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(d => d.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.Category).HasColumnName("category").HasMaxLength(50);
        builder.Property(d => d.Tags).HasColumnName("tags").HasColumnType("jsonb");
        builder.Property(d => d.IsFavorite).HasColumnName("is_favorite").HasDefaultValue(false);
        builder.Property(d => d.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => new { d.UserId, d.Type });
        builder.HasIndex(d => new { d.UserId, d.Category });
        builder.HasIndex(d => new { d.UserId, d.IsFavorite });

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
