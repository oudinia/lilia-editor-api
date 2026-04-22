using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lilia.Api.Services;

public interface IDocumentSizeService
{
    /// <summary>
    /// Compute the footprint of a document: block JSON bytes + sum of asset
    /// file sizes + a derived "unusually large" flag. Pure SQL aggregates;
    /// no rows transit the app layer.
    /// </summary>
    Task<DocumentSizeDto> GetSizeAsync(Guid documentId, CancellationToken ct = default);
}

public record DocumentSizeDto(
    Guid DocumentId,
    int BlockCount,
    int AssetCount,
    long BlockBytes,
    long AssetBytes,
    long TotalBytes,
    bool IsUnusuallyLarge,
    string? Notice);

public class DocumentSizeService : IDocumentSizeService
{
    // Heuristic: over 25 MB is unusual for a single document. Most thesis /
    // CV / research docs land <5 MB even with figures. Over 25 MB usually
    // signals a single large embedded image (or a handful of unoptimised ones).
    private const long UnusualThresholdBytes = 25L * 1024 * 1024;

    private readonly LiliaDbContext _context;

    public DocumentSizeService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentSizeDto> GetSizeAsync(Guid documentId, CancellationToken ct = default)
    {
        // Single round-trip aggregate. `octet_length(content::text)` is the
        // Postgres idiom for jsonb payload size; length depends on
        // serialisation but is stable enough for a "how big is this doc" signal.
        const string sql = @"
SELECT
    (SELECT COUNT(*) FROM blocks WHERE document_id = @p0)                                              AS block_count,
    (SELECT COALESCE(SUM(octet_length(content::text))::bigint, 0) FROM blocks WHERE document_id = @p0) AS block_bytes,
    (SELECT COUNT(*) FROM assets WHERE document_id = @p0)                                              AS asset_count,
    (SELECT COALESCE(SUM(file_size), 0)::bigint FROM assets WHERE document_id = @p0)                   AS asset_bytes;";

        // Use a dedicated pooled connection rather than the DbContext's managed
        // one — sharing it with EF caused "Connection is busy" (Sentry
        // LILIA-API-Q). A fresh NpgsqlConnection is cheap; Npgsql's pool
        // reuses the same underlying socket when idle.
        var connStr = _context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No connection string configured for LiliaDbContext");
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p0", documentId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
        {
            return new DocumentSizeDto(documentId, 0, 0, 0, 0, 0, false, null);
        }

        var blockCount = r.GetInt32(0);
        var blockBytes = r.GetInt64(1);
        var assetCount = r.GetInt32(2);
        var assetBytes = r.GetInt64(3);
        var total = blockBytes + assetBytes;

        var unusual = total > UnusualThresholdBytes;
        string? notice = null;
        if (unusual)
        {
            notice = assetBytes > blockBytes * 4
                ? $"Assets account for {assetBytes / (1024 * 1024)} MB — consider removing or re-optimising large images."
                : $"Document footprint {total / (1024 * 1024)} MB is above the {UnusualThresholdBytes / (1024 * 1024)} MB soft ceiling.";
        }

        return new DocumentSizeDto(documentId, blockCount, assetCount, blockBytes, assetBytes, total, unusual, notice);
    }
}
