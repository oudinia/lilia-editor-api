using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

/// <summary>Bulk import's tables (21 Nov 2026): batches, files, chunks, and Mathpix's results by content.</summary>
public class BulkImportBatchConfiguration : IEntityTypeConfiguration<BulkImportBatch>
{
    public void Configure(EntityTypeBuilder<BulkImportBatch> builder)
    {
        builder.ToTable("bulk_import_batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(b => b.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(b => new { b.UserId, b.CreatedAt }).HasDatabaseName("ix_bulk_import_batches_user");
        builder.HasIndex(b => b.Status).HasDatabaseName("ix_bulk_import_batches_status");

        // An erased account takes its batches (and their files and chunks) with it.
        builder.HasOne(b => b.User).WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BulkImportFileConfiguration : IEntityTypeConfiguration<BulkImportFile>
{
    public void Configure(EntityTypeBuilder<BulkImportFile> builder)
    {
        builder.ToTable("bulk_import_files");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.BatchId).HasColumnName("batch_id");
        builder.Property(f => f.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(f => f.OriginalName).HasColumnName("original_name").HasMaxLength(500).IsRequired();
        builder.Property(f => f.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(f => f.SourceHash).HasColumnName("source_hash").HasMaxLength(64);
        builder.Property(f => f.Pages).HasColumnName("pages");
        builder.Property(f => f.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(f => f.DocumentId).HasColumnName("document_id");
        builder.Property(f => f.PreambleApplied).HasColumnName("preamble_applied");
        builder.Property(f => f.UsesStaging).HasColumnName("uses_staging");
        builder.Property(f => f.PromotedAt).HasColumnName("promoted_at");
        builder.Property(f => f.StagedPreamble).HasColumnName("staged_preamble");
        builder.Property(f => f.StagedMacros).HasColumnName("staged_macros");
        builder.Property(f => f.PromoteMs).HasColumnName("promote_ms");
        builder.Property(f => f.Error).HasColumnName("error").HasMaxLength(2000);
        builder.Property(f => f.Warnings).HasColumnName("warnings");
        builder.Property(f => f.UploadMs).HasColumnName("upload_ms");
        builder.Property(f => f.SizeBytes).HasColumnName("size_bytes");
        builder.Property(f => f.SplitMs).HasColumnName("split_ms");
        builder.Property(f => f.FirstPageMs).HasColumnName("first_page_ms");
        builder.Property(f => f.TotalMs).HasColumnName("total_ms");
        builder.Property(f => f.CompileMs).HasColumnName("compile_ms");
        builder.Property(f => f.CompileOk).HasColumnName("compile_ok");
        builder.Property(f => f.CompileEngine).HasColumnName("compile_engine").HasMaxLength(20);
        builder.Property(f => f.CompileError).HasColumnName("compile_error").HasMaxLength(500);
        builder.Property(f => f.Precision).HasColumnName("precision_score");
        builder.Property(f => f.LowPrecisionPages).HasColumnName("low_precision_pages");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(f => f.BatchId).HasDatabaseName("ix_bulk_import_files_batch");
        builder.HasIndex(f => new { f.UserId, f.Status }).HasDatabaseName("ix_bulk_import_files_user_status");
        builder.HasIndex(f => f.Status).HasDatabaseName("ix_bulk_import_files_status");
        builder.HasIndex(f => f.DocumentId).HasDatabaseName("ix_bulk_import_files_document");
        // The timings' window (GET /api/bulk-imports/stats, lilia bulk-timings).
        builder.HasIndex(f => f.CreatedAt).HasDatabaseName("ix_bulk_import_files_created");

        builder.HasOne(f => f.Batch).WithMany(b => b.Files).HasForeignKey(f => f.BatchId).OnDelete(DeleteBehavior.Cascade);
        // A deleted document leaves the file's record, without its link.
        builder.HasOne<Document>().WithMany().HasForeignKey(f => f.DocumentId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class BulkImportChunkConfiguration : IEntityTypeConfiguration<BulkImportChunk>
{
    public void Configure(EntityTypeBuilder<BulkImportChunk> builder)
    {
        builder.ToTable("bulk_import_chunks");
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.PageCount);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.FileId).HasColumnName("file_id");
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Index).HasColumnName("index");
        builder.Property(c => c.FirstPage).HasColumnName("first_page");
        builder.Property(c => c.LastPage).HasColumnName("last_page");
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Attempts).HasColumnName("attempts");
        builder.Property(c => c.ChunkKey).HasColumnName("chunk_key").HasMaxLength(500);
        builder.Property(c => c.MathpixPdfId).HasColumnName("mathpix_pdf_id").HasMaxLength(100);
        builder.Property(c => c.PagesCompleted).HasColumnName("pages_completed");
        builder.Property(c => c.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        builder.Property(c => c.ResultKey).HasColumnName("result_key").HasMaxLength(500);
        builder.Property(c => c.FromCache).HasColumnName("from_cache");
        builder.Property(c => c.ChargedPages).HasColumnName("charged_pages");
        builder.Property(c => c.PlaceholderBlockId).HasColumnName("placeholder_block_id");
        builder.Property(c => c.SessionId).HasColumnName("session_id");
        builder.Property(c => c.Error).HasColumnName("error").HasMaxLength(2000);
        builder.Property(c => c.StartedAt).HasColumnName("started_at");
        builder.Property(c => c.QueueWaitMs).HasColumnName("queue_wait_ms");
        builder.Property(c => c.MathpixMs).HasColumnName("mathpix_ms");
        builder.Property(c => c.TexZipMs).HasColumnName("tex_zip_ms");
        builder.Property(c => c.PlaceMs).HasColumnName("place_ms");
        builder.Property(c => c.PlaceParseMs).HasColumnName("place_parse_ms");
        builder.Property(c => c.PlaceFiguresMs).HasColumnName("place_figures_ms");
        builder.Property(c => c.PlaceWriteMs).HasColumnName("place_write_ms");
        builder.Property(c => c.Precision).HasColumnName("precision_score");
        builder.Property(c => c.PrecisionPages).HasColumnName("precision_pages").HasColumnType("jsonb");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => new { c.FileId, c.Index }).IsUnique().HasDatabaseName("ux_bulk_import_chunks_file_index");
        builder.HasIndex(c => new { c.UserId, c.Status }).HasDatabaseName("ix_bulk_import_chunks_user_status");
        builder.HasIndex(c => c.Status).HasDatabaseName("ix_bulk_import_chunks_status");
        builder.HasIndex(c => c.CreatedAt).HasDatabaseName("ix_bulk_import_chunks_created");

        builder.HasOne(c => c.File).WithMany(f => f.Chunks).HasForeignKey(c => c.FileId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>A chunk's blocks staged until promotion (21 Nov 2026): the import never writes into the document while it runs.</summary>
public class BulkImportStagedBlockConfiguration : IEntityTypeConfiguration<BulkImportStagedBlock>
{
    public void Configure(EntityTypeBuilder<BulkImportStagedBlock> builder)
    {
        builder.ToTable("bulk_import_staged_blocks");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.FileId).HasColumnName("file_id");
        builder.Property(b => b.ChunkIndex).HasColumnName("chunk_index");
        builder.Property(b => b.Order).HasColumnName("order_in_chunk");
        builder.Property(b => b.Type).HasColumnName("block_type").HasMaxLength(50).IsRequired();
        builder.Property(b => b.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(b => b.Depth).HasColumnName("depth");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(b => new { b.FileId, b.ChunkIndex, b.Order }).HasDatabaseName("ix_bulk_import_staged_blocks_file_order");
        // A file erased (its batch, its author) takes its staging with it.
        builder.HasOne(b => b.File).WithMany().HasForeignKey(b => b.FileId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>A chunk's files (figures' images) staged until promotion, kept under <c>bulk-staging/{fileId}/</c>.</summary>
public class BulkImportStagedAssetConfiguration : IEntityTypeConfiguration<BulkImportStagedAsset>
{
    public void Configure(EntityTypeBuilder<BulkImportStagedAsset> builder)
    {
        builder.ToTable("bulk_import_staged_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.FileId).HasColumnName("file_id");
        builder.Property(a => a.ChunkIndex).HasColumnName("chunk_index");
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(a => a.OriginalPath).HasColumnName("original_path").HasMaxLength(500).IsRequired();
        builder.Property(a => a.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.Size).HasColumnName("size_bytes");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => new { a.FileId, a.Name }).IsUnique().HasDatabaseName("ux_bulk_import_staged_assets_file_name");
        builder.HasIndex(a => new { a.FileId, a.ChunkIndex }).HasDatabaseName("ix_bulk_import_staged_assets_file_chunk");
        builder.HasOne(a => a.File).WithMany().HasForeignKey(a => a.FileId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MathpixChunkCacheConfiguration : IEntityTypeConfiguration<MathpixChunkCache>
{
    public void Configure(EntityTypeBuilder<MathpixChunkCache> builder)
    {
        builder.ToTable("mathpix_chunk_cache");
        builder.HasKey(c => c.Hash);
        builder.Property(c => c.Hash).HasColumnName("hash").HasMaxLength(64);
        builder.Property(c => c.ResultKey).HasColumnName("result_key").HasMaxLength(500).IsRequired();
        builder.Property(c => c.Pages).HasColumnName("pages");
        builder.Property(c => c.PrecisionPages).HasColumnName("precision_pages").HasColumnType("jsonb");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
    }
}
