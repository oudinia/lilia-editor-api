using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>
/// The bulk import timings over a window (21 Nov 2026, staff only: <c>GET /api/bulk-imports/stats?days=7</c>; the same
/// numbers as <c>lilia bulk-timings</c>): every file uploaded in the last N days and its chunks, and per stage how many
/// were measured, p50, p90, min and max. Percentiles interpolate as PostgreSQL's <c>percentile_cont</c> does, so the CLI's
/// SQL and this agree. Only the measured columns are read (a few ints a row).
/// </summary>
public static class BulkImportStats
{
    public const int MaxDays = 365;

    public static async Task<BulkImportStatsDto> ComputeAsync(LiliaDbContext db, int days, DateTime now, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var from = now.AddDays(-days);
        var files = await db.BulkImportFiles.AsNoTracking()
            .Where(f => f.CreatedAt >= from && f.CreatedAt <= now)
            .Select(f => new
            {
                f.Id, f.Status, f.Pages, f.UploadMs, f.SplitMs, f.FirstPageMs, f.TotalMs, f.PromoteMs, f.CompileMs, f.CompileOk,
                f.Precision, f.LowPrecisionPages,
            })
            .ToListAsync(ct);
        var chunks = await db.BulkImportChunks.AsNoTracking()
            .Where(c => c.File!.CreatedAt >= from && c.File.CreatedAt <= now)
            .Select(c => new
            {
                c.FileId, c.Status, Pages = c.LastPage - c.FirstPage + 1, c.FromCache, c.QueueWaitMs, c.MathpixMs, c.TexZipMs,
                c.PlaceMs, c.PlaceParseMs, c.PlaceFiguresMs, c.PlaceWriteMs,
            })
            .ToListAsync(ct);

        var placedPages = chunks.Where(c => c.Status == BulkImportStatuses.Done).GroupBy(c => c.FileId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Pages));
        var read = chunks.Where(c => !c.FromCache && c.MathpixMs is not null).ToList();

        var stages = new List<BulkImportStageStatsDto>
        {
            Stage("upload", "ms", files.Select(f => f.UploadMs)),
            Stage("split", "ms", files.Select(f => f.SplitMs)),
            Stage("queueWait", "ms", chunks.Select(c => c.QueueWaitMs)),
            Stage("mathpix", "ms", read.Select(c => c.MathpixMs)),
            Stage("mathpixPerPage", "ms/page", read.Select(c => (double?)c.MathpixMs!.Value / Math.Max(1, c.Pages))),
            Stage("texZip", "ms", read.Select(c => c.TexZipMs)),
            Stage("place", "ms", chunks.Select(c => c.PlaceMs)),
            Stage("placeParse", "ms", chunks.Select(c => c.PlaceParseMs)),
            Stage("placeFigures", "ms", chunks.Select(c => c.PlaceFiguresMs)),
            Stage("placeWrite", "ms", chunks.Select(c => c.PlaceWriteMs)),
            Stage("firstPage", "ms", files.Select(f => f.FirstPageMs)),
            Stage("promote", "ms", files.Select(f => f.PromoteMs)),
            Stage("total", "ms", files.Select(f => f.TotalMs)),
            Stage("compile", "ms", files.Select(f => f.CompileMs)),
            Stage("pagesPerMinute", "pages/min", files.Select(f =>
                f.TotalMs is > 0 && placedPages.TryGetValue(f.Id, out var p) && p > 0 ? p / (f.TotalMs.Value / 60000.0) : (double?)null)),
            Stage("precision", "0-1", files.Select(f => f.Precision)),
        };

        return new BulkImportStatsDto(
            days, from, now,
            files.Count,
            files.Count(f => f.Status is BulkImportStatuses.Done or BulkImportStatuses.Partial or BulkImportStatuses.Failed),
            chunks.Count,
            chunks.Count(c => c.FromCache),
            files.Sum(f => f.Pages),
            files.Count(f => f.CompileOk == true),
            files.Count(f => f.CompileOk == false),
            files.Sum(f => f.LowPrecisionPages),
            stages);
    }

    private static BulkImportStageStatsDto Stage(string stage, string unit, IEnumerable<int?> values) =>
        Stage(stage, unit, values.Select(v => (double?)v));

    private static BulkImportStageStatsDto Stage(string stage, string unit, IEnumerable<double?> values)
    {
        var sorted = values.Where(v => v is not null).Select(v => v!.Value).Order().ToArray();
        if (sorted.Length == 0) return new BulkImportStageStatsDto(stage, unit, 0, null, null, null, null);
        return new BulkImportStageStatsDto(stage, unit, sorted.Length,
            Round(Percentile(sorted, 0.5)), Round(Percentile(sorted, 0.9)), Round(sorted[0]), Round(sorted[^1]));
    }

    /// <summary>The <paramref name="p"/> percentile of sorted values, interpolated between the two nearest (percentile_cont).</summary>
    internal static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 1) return sorted[0];
        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    private static double Round(double v) => Math.Round(v, 3);
}
