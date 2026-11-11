using System.Collections.Concurrent;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Records what authors do with Lilia, weighted by complexity (<see cref="Operation"/>, <see cref="OperationKind"/>):
/// the owner, 10 Nov 2026 ("start 2B immediately": paying per operation) and 11 Nov ("categorize them… pondering the
/// tasks by complexity"). Each operation keeps the points its kind had when it was recorded. A failure to record never
/// fails what the author asked for: it is logged.
/// </summary>
public sealed class OperationLog(LiliaDbContext db, ILogger<OperationLog> logger)
{
    /// <summary>The catalogue, read at most once a minute: the admin console changes weights without a restart.</summary>
    private static readonly ConcurrentDictionary<string, OperationKind> Kinds = new(StringComparer.Ordinal);
    private static DateTime _kindsReadAt = DateTime.MinValue;

    public async Task RecordAsync(string userId, Guid? documentId, string code, string source, string? format = null, int? units = null)
    {
        try
        {
            var kind = await KindAsync(code);
            if (kind is null)
            {
                logger.LogWarning("[Operations] No operation kind {Code}: not recorded", code);
                return;
            }
            db.Operations.Add(new Operation
            {
                Id = Guid.NewGuid(), UserId = userId, DocumentId = documentId, Kind = code,
                Format = format ?? "", Source = source, Points = kind.PointsFor(units), Units = units, CreatedAt = DateTime.UtcNow,
            });
            // Not the request's token: what the author asked for is already done.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Operations] Could not record {Code} for {UserId}", code, userId);
        }
    }

    /// <summary>A file taken away: export.pdf, export.accessible_pdf, or export.file for every other format.</summary>
    public Task RecordExportAsync(string userId, Guid? documentId, string format, string source, bool accessible = false) =>
        RecordAsync(userId, documentId, ExportCode(format, accessible), source, format);

    public static string ExportCode(string format, bool accessible = false) =>
        format == "pdf" ? (accessible ? "export.accessible_pdf" : "export.pdf") : "export.file";

    /// <summary>Exports this calendar month (UTC), every source.</summary>
    public Task<int> ExportsThisMonthAsync(string userId, CancellationToken ct = default)
    {
        var start = MonthStart();
        return db.Operations.AsNoTracking().CountAsync(o => o.UserId == userId && o.Kind.StartsWith("export.") && o.CreatedAt >= start, ct);
    }

    /// <summary>Points this calendar month (UTC).</summary>
    public async Task<int> PointsThisMonthAsync(string userId, CancellationToken ct = default)
    {
        var start = MonthStart();
        return await db.Operations.AsNoTracking().Where(o => o.UserId == userId && o.CreatedAt >= start).SumAsync(o => (int?)o.Points, ct) ?? 0;
    }

    private static DateTime MonthStart() => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task<OperationKind?> KindAsync(string code)
    {
        if (DateTime.UtcNow - _kindsReadAt > TimeSpan.FromMinutes(1))
        {
            foreach (var k in await db.OperationKinds.AsNoTracking().Where(k => k.Active).ToListAsync()) Kinds[k.Code] = k;
            _kindsReadAt = DateTime.UtcNow;
        }
        return Kinds.TryGetValue(code, out var kind) && kind.Active ? kind : null;
    }

    /// <summary>For tests and the admin console: the next record reads the catalogue again.</summary>
    internal static void Forget() => _kindsReadAt = DateTime.MinValue;

    /// <summary>
    /// The kind of a connector tool call. export_document is not counted: the download is (export.*). start_import is
    /// counted when the file arrives (an import), not when the drop page opens.
    /// </summary>
    public static string? ConnectorCode(string tool, bool withBody = false) => tool switch
    {
        "search_help" or "get_help" => "help",
        "list_documents" or "get_outline" or "get_block" or "search_document" or "get_document_settings"
            or "get_lilia_latex" or "import_status" => "read",
        "add_block" or "edit_block" or "remove_block" or "reorder_blocks" or "set_title"
            or "set_document_settings" or "set_venue" => "edit.small",
        "create_document" => withBody ? "document.write" : "document.create",
        "apply_lml" or "paste_content" => "document.write",
        "check_document" => "check",
        "import_document" => "import",
        _ => null,
    };
}
