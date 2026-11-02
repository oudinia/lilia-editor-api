using Lilia.Infrastructure.Data;

namespace Lilia.Api.Services;

/// <summary>
/// Advance a document's version when its blocks are written outside the batch
/// save — Studio's block cards, a draft inserted into a document.
///
/// <para>The Flow editor saves the whole document at once, and the server
/// deletes whatever that save leaves out. It only learns someone else wrote
/// when the version it expected has moved (a 409, then a rebase). A write that
/// left the version alone was invisible to it: its next save deleted a block
/// created in Studio, or overwrote a Studio edit, with no conflict at all.</para>
///
/// <para>Tracked, not ExecuteUpdate: the caller's own SaveChanges writes the
/// block and the version together, so neither lands without the other.</para>
/// </summary>
public static class ConcurrencyVersion
{
    public static async Task BumpAsync(LiliaDbContext db, Guid documentId)
    {
        var document = await db.Documents.FindAsync(documentId);
        if (document is null) return;
        document.Version += 1;
        document.UpdatedAt = DateTime.UtcNow;
    }
}
