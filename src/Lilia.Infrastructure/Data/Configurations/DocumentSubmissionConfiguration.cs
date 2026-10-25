using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentSubmissionConfiguration : IEntityTypeConfiguration<DocumentSubmission>
{
    public void Configure(EntityTypeBuilder<DocumentSubmission> builder)
    {
        builder.ToTable("document_submissions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.DocumentId).HasColumnName("document_id");
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(255).IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(s => s.Profile).HasColumnName("profile").HasMaxLength(64);
        builder.Property(s => s.ClassVersion).HasColumnName("class_version").HasMaxLength(120);
        builder.Property(s => s.MainTex).HasColumnName("main_tex").IsRequired();
        builder.Property(s => s.ReferencesBib).HasColumnName("references_bib");
        builder.Property(s => s.MainTexSha256).HasColumnName("main_tex_sha256").HasMaxLength(64).IsRequired();
        builder.HasIndex(s => new { s.DocumentId, s.CreatedAt }).HasDatabaseName("ix_document_submissions_document");
        builder.HasOne<Document>().WithMany().HasForeignKey(s => s.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
