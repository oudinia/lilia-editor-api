using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Records what an author takes away from Lilia (<see cref="Operation"/>), for paying per operation (the owner,
/// 10 Nov 2026) and for "this month: N exports". A failure to record never fails the download: it is logged.
/// </summary>
public sealed class OperationLog(LiliaDbContext db, ILogger<OperationLog> logger)
{
    public async Task RecordExportAsync(string userId, Guid? documentId, string format, string source)
    {
        try
        {
            db.Operations.Add(new Operation
            {
                Id = Guid.NewGuid(), UserId = userId, DocumentId = documentId, Kind = "export",
                Format = format, Source = source, CreatedAt = DateTime.UtcNow,
            });
            // Not the request's token: the file is already on its way.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Operations] Could not record a {Format} export of {DocId} for {UserId}", format, documentId, userId);
        }
    }

    /// <summary>Exports this calendar month (UTC), every source.</summary>
    public Task<int> ExportsThisMonthAsync(string userId, CancellationToken ct = default)
    {
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return db.Operations.AsNoTracking().CountAsync(o => o.UserId == userId && o.Kind == "export" && o.CreatedAt >= start, ct);
    }
}
