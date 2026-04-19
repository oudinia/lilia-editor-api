using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class AiRequestConfiguration : IEntityTypeConfiguration<AiRequest>
{
    public void Configure(EntityTypeBuilder<AiRequest> builder)
    {
        builder.ToTable("ai_requests", t =>
        {
            t.HasCheckConstraint("ck_ai_request_purpose",
                "purpose IN (" +
                "'rephrase','summarise','suggest_headings','suggest_bibliography'," +
                "'fix_latex','expand_outline','review_finding','redact_pii','other'" +
                ")");
            t.HasCheckConstraint("ck_ai_request_status",
                "status IN ('pending','success','error','rate_limited','redacted_refused')");
            t.HasCheckConstraint("ck_ai_request_provider",
                "provider IN ('anthropic','openai','local')");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(r => r.DocumentId).HasColumnName("document_id");
        builder.Property(r => r.BlockId).HasColumnName("block_id").HasMaxLength(255);
        builder.Property(r => r.Purpose).HasColumnName("purpose").HasMaxLength(40).IsRequired();
        builder.Property(r => r.Provider).HasColumnName("provider").HasMaxLength(20).HasDefaultValue("anthropic").IsRequired();
        builder.Property(r => r.Model).HasColumnName("model").HasMaxLength(60).IsRequired();
        builder.Property(r => r.PromptHash).HasColumnName("prompt_hash").HasMaxLength(64).IsRequired();
        builder.Property(r => r.RedactionSummary).HasColumnName("redaction_summary").HasColumnType("jsonb");
        builder.Property(r => r.PromptTokens).HasColumnName("prompt_tokens").HasDefaultValue(0);
        builder.Property(r => r.CompletionTokens).HasColumnName("completion_tokens").HasDefaultValue(0);
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending").IsRequired();
        builder.Property(r => r.ErrorMessage).HasColumnName("error_message");
        builder.Property(r => r.LatencyMs).HasColumnName("latency_ms");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(r => r.UserId).HasDatabaseName("ix_ai_request_user");
        builder.HasIndex(r => r.DocumentId).HasDatabaseName("ix_ai_request_document");
        builder.HasIndex(r => new { r.UserId, r.CreatedAt }).HasDatabaseName("ix_ai_request_user_time");
        builder.HasIndex(r => new { r.UserId, r.Status }).HasDatabaseName("ix_ai_request_user_status");

        builder.HasOne(r => r.Document)
            .WithMany()
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
