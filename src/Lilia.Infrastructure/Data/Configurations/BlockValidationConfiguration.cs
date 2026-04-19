using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class BlockValidationConfiguration : IEntityTypeConfiguration<BlockValidation>
{
    public void Configure(EntityTypeBuilder<BlockValidation> builder)
    {
        builder.ToTable("block_validations", t =>
        {
            t.HasCheckConstraint("ck_block_validation_status",
                "status IN ('valid','error','warning')");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.BlockId).HasColumnName("block_id").IsRequired();
        builder.Property(v => v.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(v => v.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(v => v.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("valid").IsRequired();
        builder.Property(v => v.ErrorMessage).HasColumnName("error_message");
        builder.Property(v => v.Warnings).HasColumnName("warnings").HasColumnType("jsonb");
        builder.Property(v => v.RuleVersion).HasColumnName("rule_version").HasMaxLength(10).HasDefaultValue("v1").IsRequired();
        builder.Property(v => v.ValidatedAt).HasColumnName("validated_at").HasDefaultValueSql("NOW()");

        // Unique (BlockId, ContentHash, RuleVersion) — never cache the same
        // content twice for the same block under the same pipeline version.
        builder.HasIndex(v => new { v.BlockId, v.ContentHash, v.RuleVersion })
            .IsUnique()
            .HasDatabaseName("ux_block_validation_block_hash_version");

        // Document-level rollup index.
        builder.HasIndex(v => v.DocumentId).HasDatabaseName("ix_block_validation_document");

        // Status filter for "how many errors does this document have?"
        builder.HasIndex(v => new { v.DocumentId, v.Status }).HasDatabaseName("ix_block_validation_document_status");

        builder.HasOne(v => v.Block)
            .WithMany()
            .HasForeignKey(v => v.BlockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Document)
            .WithMany()
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
