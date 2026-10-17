using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Lilia.Admin.Data;

public sealed record RedactedBlock(
    int Index, Guid Id, string Type, int Depth, string Shape, string? Preview, int Words,
    string? ValidationStatus, string? Validation, int Warnings, DateTime? CompileFailedAt, string? CompileError, bool Pending);

public sealed record RedactedDocument(
    Guid Id, string OwnerId, string? OwnerEmail, DateTime CreatedAt, DateTime UpdatedAt, bool Deleted,
    string Language, string PaperSize, int Columns, IReadOnlyList<RedactedBlock> Blocks);

/// <summary>
/// A document as support sees it (the user, 17 Oct: "a block view, errors view, text replaced by
/// lorem ipsum, formula would be fixed only by user"; "data driven, every block"). Read from
/// block_shapes, which the API keeps as the document lives, and from the blocks' order, type and
/// depth: never a block's content, and no title.
/// </summary>
public sealed partial class AdminDocuments(IDbContextFactory<LiliaDbContext> factory)
{
    public async Task<RedactedDocument?> DocumentAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doc = await db.Documents.IgnoreQueryFilters().Where(d => d.Id == id)
            .Select(d => new { d.Id, d.OwnerId, d.CreatedAt, d.UpdatedAt, Deleted = d.DeletedAt != null, d.Language, d.PaperSize, d.Columns })
            .FirstOrDefaultAsync(ct);
        if (doc is null) return null;
        var email = await db.Users.Where(u => u.Id == doc.OwnerId).Select(u => u.Email).FirstOrDefaultAsync(ct);

        var blocks = await db.Blocks.Where(b => b.DocumentId == id).OrderBy(b => b.SortOrder)
            .Select(b => new { b.Id, b.Type, b.Depth }).ToListAsync(ct);
        var shapes = await db.BlockShapes.Where(s => s.DocumentId == id).ToDictionaryAsync(s => s.BlockId, ct);
        var failures = (await db.LaTeXCompilationEvents.Where(e => e.DocumentId == id && !e.Success && e.BlockId != null)
                .Select(e => new { e.BlockId, e.OccurredAt, e.ErrorCategory, e.ErrorToken }).ToListAsync(ct))
            .GroupBy(e => e.BlockId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.OccurredAt).First());

        var items = blocks.Select((b, i) =>
        {
            shapes.TryGetValue(b.Id, out var s);
            failures.TryGetValue(b.Id, out var f);
            var token = f?.ErrorToken is { } t && CommandOnly().IsMatch(t) ? $" {t}" : "";
            return new RedactedBlock(
                i + 1, b.Id, b.Type, b.Depth,
                s?.Shape ?? "shape on its way (the API refreshes it within seconds)", s?.Preview, s?.Words ?? 0,
                s?.ValidationStatus, s?.ValidationKind, s?.Warnings ?? 0,
                f?.OccurredAt, f is null ? null : (f.ErrorCategory ?? "error") + token, Pending: s is null);
        }).ToList();
        return new RedactedDocument(doc.Id, doc.OwnerId, email, doc.CreatedAt, doc.UpdatedAt, doc.Deleted, doc.Language, doc.PaperSize, doc.Columns, items);
    }

    [GeneratedRegex(@"^\\[A-Za-z@]{1,40}$")]
    private static partial Regex CommandOnly();
}
