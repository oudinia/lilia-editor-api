using Lilia.Api.Services;
using Lilia.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Features.Platform.Controllers;

/// <summary>
/// <c>GET /api/admin/storage-usage</c>: the uploads store's size and this month's operations against R2's free tier
/// (21 Nov 2026, the owner wants to be told when it is not enough). Staff only: the Entitlements staff list (the
/// owner's account) or an admin. One line to read, the numbers behind it, and a warning per limit past 80%.
/// </summary>
[ApiController]
[Route("api/admin/storage-usage")]
[Authorize]
public class StorageUsageController(
    IStorageService storage,
    IOptions<EntitlementOptions> entitlements,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<StorageUsageController> logger) : ControllerBase
{
    /// <summary>R2's free tier, per month (Cloudflare's pricing page, 21 Nov 2026).</summary>
    public const long FreeStorageBytes = 10L * 1000 * 1000 * 1000;
    public const long FreeClassA = 1_000_000;
    public const long FreeClassB = 10_000_000;
    public const double WarnAt = 0.8;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var staff = userId is not null && entitlements.Value.StaffUserIds.Contains(userId, StringComparer.Ordinal);
        if (!staff && !Lilia.Api.Security.AdminPolicy.IsAdmin(User, configuration["Auth:Admins"])) return Forbid();

        StorageUsage usage;
        try
        {
            // Listing the bucket is itself a Class A operation per 1,000 files: once every 10 minutes is plenty.
            usage = (await cache.GetOrCreateAsync("storage-usage", e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return storage.GetUsageAsync(ct);
            }))!;
        }
        catch (NotSupportedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Storage] Usage could not be read");
            return StatusCode(503, new { message = "The storage could not be read." });
        }
        return Ok(Report(usage));
    }

    internal static object Report(StorageUsage u)
    {
        var storagePct = 100.0 * u.Bytes / FreeStorageBytes;
        double? aPct = u.ClassAThisMonth is { } a ? 100.0 * a / FreeClassA : null;
        double? bPct = u.ClassBThisMonth is { } b ? 100.0 * b / FreeClassB : null;
        var warnings = new List<string>();
        if (storagePct >= WarnAt * 100) warnings.Add($"Storage is at {storagePct:0.#}% of R2's free 10 GB.");
        if (aPct >= WarnAt * 100) warnings.Add($"Writes (Class A) are at {aPct:0.#}% of R2's free 1 million this month.");
        if (bPct >= WarnAt * 100) warnings.Add($"Reads (Class B) are at {bPct:0.#}% of R2's free 10 million this month.");
        var line = $"{u.Provider} {u.Location}: {u.Objects:N0} files, {Size(u.Bytes)} ({storagePct:0.##}% of 10 GB free)"
                   + (u.ClassAThisMonth is { } ca && u.ClassBThisMonth is { } cb
                       ? $"; this month {ca:N0} writes ({aPct:0.##}% of 1M), {cb:N0} reads ({bPct:0.##}% of 10M)"
                       : "")
                   + (warnings.Count == 0 ? ". Within the free tier." : ". " + string.Join(" ", warnings));
        return new
        {
            line,
            provider = u.Provider,
            location = u.Location,
            objects = u.Objects,
            bytes = u.Bytes,
            classAThisMonth = u.ClassAThisMonth,
            classBThisMonth = u.ClassBThisMonth,
            operationsCountedSince = u.OperationsCountedSince,
            freeTier = new { storageBytes = FreeStorageBytes, classAPerMonth = FreeClassA, classBPerMonth = FreeClassB },
            percent = new { storage = Math.Round(storagePct, 2), classA = aPct is { } x ? Math.Round(x, 2) : (double?)null, classB = bPct is { } y ? Math.Round(y, 2) : (double?)null },
            warnings,
        };
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1e9:0.##} GB",
        >= 1_000_000 => $"{bytes / 1e6:0.#} MB",
        >= 1_000 => $"{bytes / 1e3:0.#} kB",
        _ => $"{bytes} B",
    };
}
