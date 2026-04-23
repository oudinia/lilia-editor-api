using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class RevBibliographyEntryConfiguration : IEntityTypeConfiguration<RevBibliographyEntry>
{
    public void Configure(EntityTypeBuilder<RevBibliographyEntry> builder)
    {
        builder.ToTable("rev_bibliography_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.RevDocumentId).HasColumnName("rev_document_id").IsRequired();
        builder.Property(e => e.CiteKey).HasColumnName("cite_key").HasMaxLength(255).IsRequired();
        builder.Property(e => e.EntryType).HasColumnName("entry_type").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Data).HasColumnName("data").HasColumnType("jsonb");
        builder.Property(e => e.FormattedText).HasColumnName("formatted_text");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(e => e.RevDocumentId);
        builder.HasIndex(e => new { e.RevDocumentId, e.CiteKey }).IsUnique();

        builder.HasOne(e => e.RevDocument)
            .WithMany()
            .HasForeignKey(e => e.RevDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
