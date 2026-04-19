using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IValidationCacheService
{
    /// <summary>Compute a stable content hash for a block's current state.</summary>
    string ComputeHash(Block block);

    /// <summary>
    /// Look up a cached validation. Returns null on miss. The caller should
    /// compile on miss and persist via <see cref="PersistAsync"/>.
    /// </summary>
    Task<BlockValidation?> GetAsync(Guid blockId, string contentHash, string validator = "pdflatex", CancellationToken ct = default);

    /// <summary>Persist a freshly-computed validation. Conflict = no-op.</summary>
    Task PersistAsync(BlockValidation validation, CancellationToken ct = default);

    /// <summary>
    /// Document-level rollup: count of valid/warning/error validations for the
    /// latest cached result per block. Uses a single aggregate SQL statement.
    /// </summary>
    Task<DocumentValidationRollup> GetDocumentRollupAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Invalidate stale cache rows for a block — keeps the table small and avoids
    /// returning out-of-date hashes after repeated edits.
    /// </summary>
    Task InvalidateOlderThanAsync(Guid blockId, string keepHash, string ruleVersion, CancellationToken ct = default);
}

public record DocumentValidationRollup(
    Guid DocumentId,
    int TotalBlocks,
    int CachedBlocks,
    int ValidBlocks,
    int WarningBlocks,
    int ErrorBlocks);

public class ValidationCacheService : IValidationCacheService
{
    public const string RuleVersion = "v1";

    private readonly LiliaDbContext _context;
    private readonly BulkInsertHelper _bulk;
    private readonly ILogger<ValidationCacheService> _logger;

    public ValidationCacheService(LiliaDbContext context, BulkInsertHelper bulk, ILogger<ValidationCacheService> logger)
    {
        _context = context;
        _bulk = bulk;
        _logger = logger;
    }

    public string ComputeHash(Block block)
    {
        // Normalise: (Type | block.Content canonical JSON). Sorting keys + no
        // whitespace means trivial re-serialisations don't break the cache.
        var canonical = CanonicalizeJson(block.Content.RootElement);
        var input = $"{block.Type}\u0001{canonical}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<BlockValidation?> GetAsync(Guid blockId, string contentHash, string validator = "pdflatex", CancellationToken ct = default)
    {
        return await _context.BlockValidations
            .AsNoTracking()
            .FirstOrDefaultAsync(v =>
                v.BlockId == blockId &&
                v.ContentHash == contentHash &&
                v.Validator == validator &&
                v.RuleVersion == RuleVersion,
                ct);
    }

    public async Task PersistAsync(BlockValidation validation, CancellationToken ct = default)
    {
        try
        {
            await _bulk.BulkInsertBlockValidationsAsync(new[] { validation }, ct);
        }
        catch (Npgsql.PostgresException pg) when (pg.SqlState == "23505")
        {
            // Unique-violation: another request cached the same
            // (BlockId, ContentHash, RuleVersion) concurrently. Fine — both
            // observers converge on the same result.
            _logger.LogDebug("Validation cache conflict for block {BlockId} hash {Hash} — ignoring", validation.BlockId, validation.ContentHash);
        }
    }

    public async Task<DocumentValidationRollup> GetDocumentRollupAsync(Guid documentId, CancellationToken ct = default)
    {
        // One aggregate query. DISTINCT ON latest validation per block.
        const string sql = @"
WITH latest AS (
    SELECT DISTINCT ON (block_id) block_id, status
    FROM block_validations
    WHERE document_id = @p0 AND rule_version = @p1
    ORDER BY block_id, validated_at DESC
)
SELECT
    (SELECT COUNT(*) FROM blocks WHERE document_id = @p0)           AS total_blocks,
    COUNT(*)                                                        AS cached_blocks,
    COUNT(*) FILTER (WHERE status = 'valid')                        AS valid_blocks,
    COUNT(*) FILTER (WHERE status = 'warning')                      AS warning_blocks,
    COUNT(*) FILTER (WHERE status = 'error')                        AS error_blocks
FROM latest;";

        var conn = (Npgsql.NpgsqlConnection)_context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p0", documentId);
        cmd.Parameters.AddWithValue("p1", RuleVersion);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
        {
            return new DocumentValidationRollup(
                documentId,
                TotalBlocks: r.GetInt32(0),
                CachedBlocks: (int)r.GetInt64(1),
                ValidBlocks: (int)r.GetInt64(2),
                WarningBlocks: (int)r.GetInt64(3),
                ErrorBlocks: (int)r.GetInt64(4));
        }
        return new DocumentValidationRollup(documentId, 0, 0, 0, 0, 0);
    }

    public async Task InvalidateOlderThanAsync(Guid blockId, string keepHash, string ruleVersion, CancellationToken ct = default)
    {
        // Scoped to rule-version only: both typst + pdflatex rows for
        // the current hash are preserved; older hashes (any validator)
        // for this block are purged together.
        await _context.BlockValidations
            .Where(v => v.BlockId == blockId && v.RuleVersion == ruleVersion && v.ContentHash != keepHash)
            .ExecuteDeleteAsync(ct);
    }

    // ─── JSON canonicaliser ─────────────────────────────────────────────

    private static string CanonicalizeJson(JsonElement element)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(element, w);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteCanonical(JsonElement e, Utf8JsonWriter w)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                // Sort keys for stable hashes across serialisation variations.
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    w.WritePropertyName(p.Name);
                    WriteCanonical(p.Value, w);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in e.EnumerateArray())
                    WriteCanonical(item, w);
                w.WriteEndArray();
                break;
            case JsonValueKind.String:
                w.WriteStringValue(e.GetString());
                break;
            case JsonValueKind.Number:
                w.WriteRawValue(e.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True: w.WriteBooleanValue(true); break;
            case JsonValueKind.False: w.WriteBooleanValue(false); break;
            case JsonValueKind.Null: w.WriteNullValue(); break;
            default: w.WriteRawValue(e.GetRawText(), skipInputValidation: true); break;
        }
    }
}
