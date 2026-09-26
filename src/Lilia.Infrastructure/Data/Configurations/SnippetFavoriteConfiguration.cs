using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class SnippetFavoriteConfiguration : IEntityTypeConfiguration<SnippetFavorite>
{
    public void Configure(EntityTypeBuilder<SnippetFavorite> builder)
    {
        builder.ToTable("snippet_favorites");

        builder.HasKey(f => new { f.UserId, f.SnippetId });
        builder.Property(f => f.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(f => f.SnippetId).HasColumnName("snippet_id");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(f => f.SnippetId);

        builder.HasOne(f => f.Snippet)
            .WithMany()
            .HasForeignKey(f => f.SnippetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
