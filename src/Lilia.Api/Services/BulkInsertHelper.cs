using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lilia.Api.Services;

/// <summary>
/// Bulk writer for import-staging tables. Uses Npgsql's binary COPY protocol so
/// parser output lands in the database without EF tracking N rows in app memory.
/// See lilia-docs/docs/guidelines/import-export-db-first.md.
/// </summary>
public class BulkInsertHelper
{
    private readonly LiliaDbContext _context;

    public BulkInsertHelper(LiliaDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// COPY-stream ImportBlockReview rows. Caller supplies an IEnumerable so the
    /// parser can yield rows lazily instead of materialising a List.
    /// </summary>
    public async Task<int> BulkInsertBlockReviewsAsync(
        IEnumerable<ImportBlockReview> rows,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string copy = @"COPY import_block_reviews
            (id, session_id, block_index, block_id, status,
             original_content, original_type,
             current_content, current_type,
             confidence, warnings, sort_order, depth)
            FROM STDIN BINARY";

        await using var writer = await conn.BeginBinaryImportAsync(copy, ct);
        var count = 0;
        foreach (var r in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(r.Id == Guid.Empty ? Guid.NewGuid() : r.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(r.SessionId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(r.BlockIndex, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(r.BlockId, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(r.Status, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(r.OriginalContent.RootElement.GetRawText(), NpgsqlDbType.Jsonb, ct);
            await writer.WriteAsync(r.OriginalType, NpgsqlDbType.Varchar, ct);
            await WriteNullableJsonAsync(writer, r.CurrentContent, ct);
            await WriteNullableStringAsync(writer, r.CurrentType, NpgsqlDbType.Varchar, ct);
            await WriteNullableIntAsync(writer, r.Confidence, ct);
            await WriteNullableJsonAsync(writer, r.Warnings, ct);
            await writer.WriteAsync(r.SortOrder, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(r.Depth, NpgsqlDbType.Integer, ct);
            count++;
        }
        await writer.CompleteAsync(ct);
        return count;
    }

    /// <summary>
    /// COPY-stream ImportDiagnostic rows. Same shape as block reviews — no
    /// change tracking, single network round-trip regardless of row count.
    /// </summary>
    public async Task<int> BulkInsertDiagnosticsAsync(
        IEnumerable<ImportDiagnostic> rows,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string copy = @"COPY import_diagnostics
            (id, session_id, block_id, element_path,
             source_line_start, source_line_end, source_col_start, source_col_end,
             source_snippet, category, severity, code, message,
             suggested_action, auto_fix_applied, docs_url,
             dismissed, dismissed_by, dismissed_at, created_at)
            FROM STDIN BINARY";

        await using var writer = await conn.BeginBinaryImportAsync(copy, ct);
        var count = 0;
        var now = DateTime.UtcNow;
        foreach (var d in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(d.Id == Guid.Empty ? Guid.NewGuid() : d.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(d.SessionId, NpgsqlDbType.Uuid, ct);
            await WriteNullableStringAsync(writer, d.BlockId, NpgsqlDbType.Varchar, ct);
            await WriteNullableStringAsync(writer, d.ElementPath, NpgsqlDbType.Varchar, ct);
            await WriteNullableIntAsync(writer, d.SourceLineStart, ct);
            await WriteNullableIntAsync(writer, d.SourceLineEnd, ct);
            await WriteNullableIntAsync(writer, d.SourceColStart, ct);
            await WriteNullableIntAsync(writer, d.SourceColEnd, ct);
            await WriteNullableStringAsync(writer, d.SourceSnippet, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(d.Category, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(d.Severity, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(d.Code, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(d.Message, NpgsqlDbType.Text, ct);
            await WriteNullableStringAsync(writer, d.SuggestedAction, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(d.AutoFixApplied, NpgsqlDbType.Boolean, ct);
            await WriteNullableStringAsync(writer, d.DocsUrl, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(d.Dismissed, NpgsqlDbType.Boolean, ct);
            await WriteNullableStringAsync(writer, d.DismissedBy, NpgsqlDbType.Varchar, ct);
            await WriteNullableTimestampAsync(writer, d.DismissedAt, ct);
            await writer.WriteAsync(d.CreatedAt == default ? now : d.CreatedAt, NpgsqlDbType.TimestampTz, ct);
            count++;
        }
        await writer.CompleteAsync(ct);
        return count;
    }

    private static async Task WriteNullableStringAsync(NpgsqlBinaryImporter w, string? v, NpgsqlDbType t, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v, t, ct);
    }

    private static async Task WriteNullableIntAsync(NpgsqlBinaryImporter w, int? v, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v.Value, NpgsqlDbType.Integer, ct);
    }

    private static async Task WriteNullableTimestampAsync(NpgsqlBinaryImporter w, DateTime? v, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v.Value, NpgsqlDbType.TimestampTz, ct);
    }

    private static async Task WriteNullableJsonAsync(NpgsqlBinaryImporter w, JsonDocument? v, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v.RootElement.GetRawText(), NpgsqlDbType.Jsonb, ct);
    }

    /// <summary>
    /// COPY-stream BlockValidation rows — cache hits avoid N re-compiles,
    /// cache writes on compile success go through here too.
    /// </summary>
    public async Task<int> BulkInsertBlockValidationsAsync(
        IEnumerable<BlockValidation> rows,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string copy = @"COPY block_validations
            (id, block_id, document_id, content_hash, status,
             error_message, warnings, rule_version, validated_at)
            FROM STDIN BINARY";

        await using var writer = await conn.BeginBinaryImportAsync(copy, ct);
        var count = 0;
        var now = DateTime.UtcNow;
        foreach (var v in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(v.Id == Guid.Empty ? Guid.NewGuid() : v.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.BlockId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.DocumentId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(v.ContentHash, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(v.Status, NpgsqlDbType.Varchar, ct);
            await WriteNullableStringAsync(writer, v.ErrorMessage, NpgsqlDbType.Text, ct);
            await WriteNullableJsonAsync(writer, v.Warnings, ct);
            await writer.WriteAsync(v.RuleVersion, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(v.ValidatedAt == default ? now : v.ValidatedAt, NpgsqlDbType.TimestampTz, ct);
            count++;
        }
        await writer.CompleteAsync(ct);
        return count;
    }
}
