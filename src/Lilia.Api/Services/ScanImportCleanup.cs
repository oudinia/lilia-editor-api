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
/// <item>a text accent inside a formula (<c>$\'e$</c>; <see cref="OcrMathAccents"/>) becomes its maths accent
///   (<c>\acute{e}</c>), so the PDF compiles; only one with nothing to sit on, whose printing changes, is reported
///   (21 Nov 2026);</item>
/// <item>a Mathpix figure id or cropped-image address never stays in the text (<see cref="MathpixFigures"/>): it is taken
///   out, with a warning;</item>
/// <item>a figure whose image couldn't be fetched is a figure placeholder with a diagnostic, never text.</item>
/// </list>
/// Text outside formulas is never changed: a real Arabic or Chinese text compiles with XeLaTeX, and Cyrillic or Greek with
/// LuaLaTeX (UnicodeFacesText, 21 Nov 2026). A maths command Mathpix left in the text was set back in maths before the
/// LaTeX was read (<see cref="OcrMathText"/>).
/// </summary>
public static class ScanImportCleanup
{
    public static async Task ApplyAsync(LiliaDbContext db, Guid sessionId, ILogger logger, CancellationToken ct = default)
    {
        var blocks = await db.ImportBlockReviews.Where(b => b.SessionId == sessionId).OrderBy(b => b.SortOrder).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var changed = 0;
        var render = new Lilia.Engines.RenderService(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<Lilia.Engines.RenderService>.Instance);
        foreach (var block in blocks)
        {
            var type = block.CurrentType ?? block.OriginalType;
            var notes = new List<(string Code, string Category, string Message)>();

            var replaced = new List<string>();
            var accents = new List<string>();
            var removedReference = false;
            var original = Clean(block.OriginalContent, type, replaced, accents, ref removedReference);
            var current = block.CurrentContent is null ? null : Clean(block.CurrentContent, type, replaced, accents, ref removedReference);
            if (replaced.Count > 0)
            {
                var symbols = string.Join(", ", replaced.Distinct().Take(5));
                notes.Add(("UnreadableSymbolReplaced", "auto_shimmed",
                    $"Block {block.SortOrder + 1} ({type}): an unreadable symbol in a formula ({symbols}) was replaced with a box (□). Check it against the scan."));
            }
            if (accents.Count > 0)
                notes.Add(("MathAccentWithoutLetter", "auto_shimmed",
                    $"Block {block.SortOrder + 1} ({type}): an accent in a formula ({string.Join(", ", accents.Distinct().Take(5))}) had no letter under it and was taken out (an accent \\' after a letter is read as its prime). Check it against the scan."));
            if (removedReference)
                notes.Add(("FigureReferenceRemoved", "auto_shimmed",
                    $"Block {block.SortOrder + 1} ({type}): a figure reference from the scan reader was taken out of the text."));
            // Last: the block as TeX will read it. Its maths made canonical, then rendered and checked; what still can't
            // compile is printed as written (the notebooks A and C, 21 Nov 2026: one bad formula stopped the whole PDF).
            var printedAsText = new List<string>();
            if (MakeCompilable(original ?? block.OriginalContent, type, render, printedAsText) is { } safeOriginal) original = safeOriginal;
            if ((current ?? block.CurrentContent) is { } cur && MakeCompilable(cur, type, render, printedAsText) is { } safeCurrent) current = safeCurrent;
            if (printedAsText.Count > 0)
                notes.Add(("MathPrintedAsText", "auto_shimmed",
                    $"Block {block.SortOrder + 1} ({type}): maths from the scan reader that LaTeX can't read ({printedAsText.Distinct().First()}) is printed as written. Check it against the scan."));
            if (type == "figure" && FigureSource(original ?? block.OriginalContent) is "")
                notes.Add(("FigureImageMissing", "missing_asset",
                    $"Block {block.SortOrder + 1} (figure): the figure's image couldn't be brought in from the scan. Add it from the scan, or remove the figure."));

            // A text accent turned into a maths one changes the block without a note: it prints the same.
            if (original is not null) block.OriginalContent = original;
            if (current is not null) block.CurrentContent = current;
            if (notes.Count == 0)
            {
                if (original is not null || current is not null) changed++;
                continue;
            }
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

    /// <summary>Block types whose text is not LaTeX text: code, raw LaTeX, BibTeX.</summary>
    private static bool NotText(string type) => type is "code" or "embed" or "bibliography" or "tikz";

    /// <summary>Content fields that hold no text to typeset.</summary>
    private static bool NotTextField(string? key) =>
        key is null or "id" or "type" or "language" or "equationMode" or "latex" or "code" or "src" or "url" or "href" or "label"
            or "citeKey" or "key" or "kind" or "variant" or "level" or "style" or "theme" or "spacing" or "labelFormat" or "placement"
            or "align" or "columnAlign" or "width" or "height" or "size" or "color" or "format" or "status" or "numbering" or "lml"
            or "source" or "engine" or "calloutType" or "theoremType" or "subType" or "listType"
        || key.EndsWith("Id", StringComparison.Ordinal);

    /// <summary>
    /// The content as TeX can read it, or null when it already was: every text field's maths made canonical
    /// (<see cref="OcrMathText.CanonicalText"/>) and every formula mended; then the block is rendered and read in TeX's modes
    /// (<see cref="OcrMathText.ModeProblem"/>). If something would still stop TeX, each field holding maths is printed as
    /// written instead (a code span, an equation as \texttt text (<see cref="EquationAsText"/>)), and <paramref name="printedAsText"/> says why.
    /// </summary>
    internal static JsonDocument? MakeCompilable(JsonDocument content, string type, Lilia.Engines.RenderService render, List<string> printedAsText)
    {
        if (NotText(type)) return null;
        var node = JsonNode.Parse(content.RootElement.GetRawText());
        if (node is null) return null;
        var before = node.ToJsonString();
        Strings(node, null, (key, text) =>
            type == "equation" && key == "latex" ? OcrMathText.MendFormula(text)
            : type == "figure" && key is not ("caption" or "alt" or "altText") || NotTextField(key) ? text
            : OcrMathText.CanonicalText(text));
        if (Problem(node, type, render) is { } problem)
        {
            printedAsText.Add(problem);
            Strings(node, null, (key, text) =>
                type == "equation" && key == "latex" ? EquationAsText(text)
                : type == "figure" && key is not ("caption" or "alt" or "altText") || NotTextField(key) ? text
                : text.Length > 0 ? "`" + text.Replace('`', '\'').Replace("\r", "").Replace('\n', ' ') + "`"
                : text);
        }
        var after = node.ToJsonString();
        return after == before ? null : JsonDocument.Parse(after);
    }

    /// <summary>An equation's formula printed as written, in typewriter text inside the maths. A test pins it: on 21 Nov 2026
    /// the backslashes of \text and \texttt were written as tab characters, and nothing noticed.</summary>
    internal static string EquationAsText(string formula) => @"\text{\texttt{" + Lilia.Engines.LatexText.Escape(formula) + "}}";

    private static string? Problem(JsonNode node, string type, Lilia.Engines.RenderService render)
    {
        try
        {
            var block = new Block { Id = Guid.NewGuid(), Type = type, Content = JsonDocument.Parse(node.ToJsonString()) };
            return OcrMathText.ModeProblem(render.RenderBlockToLatex(block));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;   // a block the renderer can't read is not this check's to judge
        }
    }

    /// <summary>Every string in the content, replaced by <paramref name="change"/> (given its field's name).</summary>
    private static void Strings(JsonNode node, string? key, Func<string?, string, string> change)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, child) in obj.ToList())
                {
                    if (child is JsonValue v && v.TryGetValue<string>(out var text)) { var u = change(k, text); if (u != text) obj[k] = u; }
                    else if (child is not null) Strings(child, k, change);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out var text)) { var u = change(key, text); if (u != text) array[i] = u; }
                    else if (array[i] is { } child) Strings(child, key, change);
                }
                break;
        }
    }

    /// <summary>The content with formulas guarded and raw figure references removed, or null when nothing changed.</summary>
    private static JsonDocument? Clean(JsonDocument content, string type, List<string> replaced, List<string> accents, ref bool removedReference)
    {
        var node = JsonNode.Parse(content.RootElement.GetRawText());
        if (node is null) return null;
        var removed = false;
        var changed = false;
        Walk(node, null, type, replaced, accents, ref removed, ref changed);
        if (removed) removedReference = true;
        return changed ? JsonDocument.Parse(node.ToJsonString()) : null;
    }

    private static void Walk(JsonNode node, string? key, string type, List<string> replaced, List<string> accents, ref bool removed, ref bool changed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, child) in obj.ToList())
                {
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var updated = CleanString(text, k, type, replaced, accents, ref removed);
                        if (updated != text) { obj[k] = updated; changed = true; }
                    }
                    else if (child is not null) Walk(child, k, type, replaced, accents, ref removed, ref changed);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        var updated = CleanString(text, key, type, replaced, accents, ref removed);
                        if (updated != text) { array[i] = updated; changed = true; }
                    }
                    else if (array[i] is { } child) Walk(child, key, type, replaced, accents, ref removed, ref changed);
                }
                break;
        }
    }

    private static string CleanString(string text, string? key, string type, List<string> replaced, List<string> accents, ref bool removed)
    {
        // A figure's own image address, file name and settings are the figure, not text: only its caption and alt text are.
        if (type == "figure" && key is not ("caption" or "alt" or "altText")) return text;
        if (key is "id" or "type" or "language" or "equationMode") return text;
        var cleaned = (type == "equation" && key == "latex") || key == "latex"
            ? OcrMathGuard.InMath(OcrMathAccents.InMath(text, accents), replaced)
            : OcrMathGuard.InText(OcrMathAccents.InText(text, accents), replaced);
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
