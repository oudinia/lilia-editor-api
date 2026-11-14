using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Connector operations bought in packs (the owner, 14 Nov 2026): $5 for 300 operations, never expiring, the quantity
/// chosen at checkout, and a bonus for bigger buys: 375 each from 4 packs, 450 each from 10. A pack pays for the
/// heavy work through the connector (a check, an export, an import) once a Free account's daily allowance is used;
/// reads and edits stay free. The balance is the sum of <see cref="OperationCredit"/> rows.
/// </summary>
public sealed class OperationPacks(LiliaDbContext db, ILogger<OperationPacks> logger)
{
    public const int PerPack = 300;
    public const int MaxPacks = 20;

    /// <summary>The connector tools a pack pays for: a compile, a file, a parse.</summary>
    public static readonly IReadOnlySet<string> PaidTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "check_document", "export_document", "import_document",
    };

    /// <summary>Operations for a purchase of <paramref name="packs"/> packs, with the bonus tiers.</summary>
    public static int OperationsFor(int packs) => packs <= 0 ? 0 : packs * (packs >= 10 ? 450 : packs >= 4 ? 375 : PerPack);

    public async Task<int> BalanceAsync(string userId, CancellationToken ct = default) =>
        await db.OperationCredits.AsNoTracking().Where(c => c.UserId == userId).SumAsync(c => (int?)c.Delta, ct) ?? 0;

    /// <summary>Spends one operation for <paramref name="tool"/>; the spend's id, or null when the balance is empty.</summary>
    public async Task<Guid?> TrySpendAsync(string userId, string tool, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One spend at a time per account, so two calls at once can't take the last operation twice.
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"ops:" + userId}))", ct);
        var balance = await db.OperationCredits.Where(c => c.UserId == userId).SumAsync(c => (int?)c.Delta, ct) ?? 0;
        if (balance <= 0) return null;
        var spend = new OperationCredit { Id = Guid.NewGuid(), UserId = userId, Delta = -1, Reason = "spend", Tool = tool, CreatedAt = DateTime.UtcNow };
        db.OperationCredits.Add(spend);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return spend.Id;
    }

    /// <summary>Gives back a spend whose call failed (once: a spend has at most one refund).</summary>
    public async Task RefundAsync(Guid spendId)
    {
        try
        {
            var spend = await db.OperationCredits.AsNoTracking().FirstOrDefaultAsync(c => c.Id == spendId && c.Reason == "spend");
            if (spend is null) return;
            db.OperationCredits.Add(new OperationCredit
            {
                Id = Guid.NewGuid(), UserId = spend.UserId, Delta = -spend.Delta, Reason = "refund", Tool = spend.Tool,
                RefundOf = spendId, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "[Packs] Refund of {SpendId} not written (already refunded?)", spendId);
        }
    }

    /// <summary>
    /// Credits a purchase once per Checkout session: false when that session was already credited (a webhook
    /// delivered twice), true when credited now.
    /// </summary>
    public async Task<bool> GrantPurchaseAsync(string userId, int packs, string stripeSessionId, CancellationToken ct = default)
    {
        if (packs is < 1 or > MaxPacks) throw new ArgumentOutOfRangeException(nameof(packs), packs, $"1 to {MaxPacks} packs");
        if (await db.OperationCredits.AnyAsync(c => c.StripeSessionId == stripeSessionId, ct)) return false;
        db.OperationCredits.Add(new OperationCredit
        {
            Id = Guid.NewGuid(), UserId = userId, Delta = OperationsFor(packs), Reason = "purchase",
            StripeSessionId = stripeSessionId, Note = $"{packs} pack{(packs == 1 ? "" : "s")}", CreatedAt = DateTime.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // The same session, credited by a webhook delivered at the same moment; anything else is a real failure.
            if (await db.OperationCredits.AsNoTracking().AnyAsync(c => c.StripeSessionId == stripeSessionId, ct)) return false;
            throw;
        }
    }
}
