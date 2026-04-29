using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class LatexInsertionEventConfiguration : IEntityTypeConfiguration<LatexInsertionEvent>
{
    public void Configure(EntityTypeBuilder<LatexInsertionEvent> builder)
    {
        builder.ToTable("latex_insertion_events", t =>
        {
            // Closed vocabularies — match the editor's surface enum.
            // New surfaces require a migration to add the value here.
            t.HasCheckConstraint("ck_insertion_event_source",
                "source IN ('panel','palette','slash','package-modal')");
            // Mirror the kind check from latex_tokens so analytic joins stay consistent.
            t.HasCheckConstraint("ck_insertion_event_kind",
                "token_kind IN ('command','environment','declaration','length','counter')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.TokenName).HasColumnName("token_name").HasMaxLength(120).IsRequired();
        builder.Property(e => e.TokenKind).HasColumnName("token_kind").HasMaxLength(20).IsRequired();
        builder.Property(e => e.TokenPackageSlug).HasColumnName("token_package_slug").HasMaxLength(80);
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(e => e.DocumentId).HasColumnName("document_id");
        builder.Property(e => e.WrappedSelection).HasColumnName("wrapped_selection").HasDefaultValue(false);

        // The two queries we'll actually run:
        //   "top tokens by hits in last N days"  → uses (token_name, token_kind, token_package_slug, created_at)
        //   "source-mix per day for trends"      → uses (source, created_at)
        //   "engagement per user"                → uses (user_id, created_at)
        builder.HasIndex(e => new { e.TokenName, e.TokenKind, e.TokenPackageSlug, e.CreatedAt })
            .HasDatabaseName("ix_insertion_event_token_recent")
            .IsDescending(false, false, false, true);
        builder.HasIndex(e => new { e.Source, e.CreatedAt })
            .HasDatabaseName("ix_insertion_event_source_recent")
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("ix_insertion_event_user_recent")
            .HasFilter("user_id IS NOT NULL")
            .IsDescending(false, true);
    }
}
