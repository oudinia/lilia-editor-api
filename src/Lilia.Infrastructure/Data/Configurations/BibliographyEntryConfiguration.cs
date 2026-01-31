using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class BibliographyEntryConfiguration : IEntityTypeConfiguration<BibliographyEntry>
{
    public void Configure(EntityTypeBuilder<BibliographyEntry> builder)
    {
        builder.ToTable("bibliography_entries");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(b => b.CiteKey).HasColumnName("cite_key").HasMaxLength(100).IsRequired();
        builder.Property(b => b.EntryType).HasColumnName("entry_type").HasMaxLength(50).IsRequired();
        builder.Property(b => b.Data).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        builder.Property(b => b.FormattedText).HasColumnName("formatted_text");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(b => b.DocumentId);
        builder.HasIndex(b => new { b.DocumentId, b.CiteKey }).IsUnique();

        builder.HasOne(b => b.Document)
            .WithMany(d => d.BibliographyEntries)
            .HasForeignKey(b => b.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
