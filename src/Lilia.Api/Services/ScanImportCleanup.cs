using System.Text.Json;
using System.Text.Json.Nodes;
using Lilia.Core.Entities;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// What Mathpix's reading of a scan or a PDF needs before it becomes a document (the launch bot's handwritten page,
/// 20 Nov 2026), done on the review session's blocks, whichever route made them (tex.zip or Markdown), before finalize:
/// <list type="bullet">
/// <item>a character no formula can hold (خ read inside a formula; <see cref="OcrMathGuard"/>) becomes a visible box, so
///   the PDF compiles; the block gets a warning and the review a diagnostic naming it;</item>
/// <item>a Mathpix figure id or cropped-image address never stays in the text (<see cref="MathpixFigures"/>): it is taken
///   out, with a warning;</item>
/// <item>a figure whose image couldn't be fetched is a figure placeholder with a diagnostic, never text.</item>
/// </list>
/// Text outside formulas is never changed: a real Arabic or Chinese text compiles with XeLaTeX.
/// </summary>
public static class ScanImportCleanup
{
    public static async Task ApplyAsync(LiliaDbContext db, Guid sessionId, ILogger logger, CancellationToken ct = default)
    {
        var blocks = await db.ImportBlockReviews.Where(b => b.SessionId == sessionId).OrderBy(b => b.SortOrder).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var changed = 0;
        foreach (var block in blocks)
        {
            var type = block.CurrentType ?? block.OriginalType;
            var notes = new List<(string Code, string Category, string Message)>();

            var replaced = new List<string>();
            var removedReference = false;
            var original = Clean(block.OriginalContent, type, replaced, ref removedReference);
            var current = block.CurrentContent is null ? null : Clean(block.CurrentContent, type, replaced, ref removedReference);
            if (replaced.Count > 0)
            {
                var symbols = string.Join(", ", replaced.Distinct().Take(5));
                notes.Add(("UnreadableSymbolReplaced", "auto_shimmed",
                    $"Block {block.SortOrder + 1} ({type}): an unreadable symbol in a formula ({symbols}) was replaced with a box (□). Check it against the scan."));
            }
            if (removedReference)
                notes.Add(("FigureReferenceRemoved", "auto_shimmed",
                    $"Block {block.SortOrder + 1} ({type}): a figure reference from the scan reader was taken out of the text."));
            if (type == "figure" && FigureSource(original ?? block.OriginalContent) is "")
                notes.Add(("FigureImageMissing", "missing_asset",
                    $"Block {block.SortOrder + 1} (figure): the figure's image couldn't be brought in from the scan. Add it from the scan, or remove the figure."));

            if (notes.Count == 0) continue;
            if (original is not null) block.OriginalContent = original;
            if (current is not null) block.CurrentContent = current;
            block.Warnings = WithWarnings(block.Warnings, notes.Select(n => (n.Code, n.Message)));
            foreach (var note in notes)
            {
                db.ImportDiagnostics.Add(new ImportDiagnostic
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    BlockId = block.BlockId,
                    Category = note.Category,
                    Severity = "warning",
                    Code = note.Code,
                    Message = note.Message,
                    AutoFixApplied = note.Category == "auto_shimmed",
                    CreatedAt = now,
                });
            }
            changed++;
        }
        if (changed == 0) return;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("[Import] Scan cleanup: {Count} block(s) of session {SessionId} changed or flagged", changed, sessionId);
    }

    /// <summary>The content with formulas guarded and raw figure references removed, or null when nothing changed.</summary>
    private static JsonDocument? Clean(JsonDocument content, string type, List<string> replaced, ref bool removedReference)
    {
        var node = JsonNode.Parse(content.RootElement.GetRawText());
        if (node is null) return null;
        var before = replaced.Count;
        var removed = false;
        Walk(node, null, type, replaced, ref removed);
        if (removed) removedReference = true;
        return replaced.Count > before || removed ? JsonDocument.Parse(node.ToJsonString()) : null;
    }

    private static void Walk(JsonNode node, string? key, string type, List<string> replaced, ref bool removed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, child) in obj.ToList())
                {
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var updated = CleanString(text, k, type, replaced, ref removed);
                        if (!ReferenceEquals(updated, text) && updated != text) obj[k] = updated;
                    }
                    else if (child is not null) Walk(child, k, type, replaced, ref removed);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var updated = CleanString(text, key, type, replaced, ref removed);
                        if (updated != text) array[i] = updated;
                    }
                    else if (array[i] is { } child) Walk(child, key, type, replaced, ref removed);
                }
                break;
        }
    }

    private static string CleanString(string text, string? key, string type, List<string> replaced, ref bool removed)
    {
        // A figure's own image address, file name and settings are the figure, not text: only its caption and alt text are.
        if (type == "figure" && key is not ("caption" or "alt" or "altText")) return text;
        if (key is "id" or "type" or "language" or "equationMode") return text;
        var cleaned = (type == "equation" && key == "latex") || key == "latex"
            ? OcrMathGuard.InMath(text, replaced)
            : OcrMathGuard.InText(text, replaced);
        if (MathpixFigures.HasRawReference(cleaned))
        {
            cleaned = MathpixFigures.WithoutRawReferences(cleaned);
            removed = true;
        }
        return cleaned;
    }

    private static string? FigureSource(JsonDocument content) =>
        content.RootElement.ValueKind == JsonValueKind.Object && content.RootElement.TryGetProperty("src", out var src)
            && src.ValueKind == JsonValueKind.String
            ? src.GetString()
            : null;

    private static JsonDocument WithWarnings(JsonDocument? existing, IEnumerable<(string Code, string Message)> notes)
    {
        var array = existing is not null && JsonNode.Parse(existing.RootElement.GetRawText()) is JsonArray a ? a : new JsonArray();
        foreach (var (code, message) in notes)
            array.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["type"] = code,
                ["message"] = message,
                ["severity"] = "warning",
            });
        return JsonDocument.Parse(array.ToJsonString());
    }
}
