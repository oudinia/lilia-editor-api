using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.TeamId).HasColumnName("team_id");
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(255).HasDefaultValue("Untitled");
        builder.Property(d => d.Language).HasColumnName("language").HasMaxLength(10).HasDefaultValue("en");
        builder.Property(d => d.PaperSize).HasColumnName("paper_size").HasMaxLength(50).HasDefaultValue("a4");
        builder.Property(d => d.FontFamily).HasColumnName("font_family").HasMaxLength(100).HasDefaultValue("serif");
        builder.Property(d => d.FontSize).HasColumnName("font_size").HasDefaultValue(12);
        builder.Property(d => d.IsPublic).HasColumnName("is_public").HasDefaultValue(false);
        builder.Property(d => d.ShareLink).HasColumnName("share_link").HasMaxLength(100);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.LastOpenedAt).HasColumnName("last_opened_at");
        builder.Property(d => d.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.TeamId);
        builder.HasIndex(d => d.ShareLink).IsUnique();

        builder.HasOne(d => d.Owner)
            .WithMany(u => u.OwnedDocuments)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Team)
            .WithMany(t => t.Documents)
            .HasForeignKey(d => d.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(d => d.DeletedAt == null);
    }
}
