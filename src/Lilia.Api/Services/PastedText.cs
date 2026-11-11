using System.Text.Json;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Services;

/// <summary>One block read from pasted or imported text, before it belongs to a document.</summary>
public sealed record PastedBlock(string Type, object Content, int Depth = 0);

/// <summary>
/// What a text reads as: its blocks, and a title when the text names one. <see cref="TitleFromText"/> says the title
/// came from the LML text itself (an <c>@title</c> or first heading), which the import may prefer over a filename.
/// </summary>
public sealed record PastedBlocks(string Format, string? Title, bool TitleFromText, IReadOnlyList<PastedBlock> Blocks, string? Fallback)
{
    /// <summary>
    /// The title block a LaTeX paper's <c>\title</c>/<c>\author</c> make in the screen import (the upload path), which the
    /// preview shows first (#64, 11 Nov 2026: the preview showed 5 blocks and the import made 6). Not one of
    /// <see cref="Blocks"/>: the text import and paste_content leave the title to the document.
    /// </summary>
    public PastedBlock? TitleBlock { get; init; }
}

/// <summary>
/// Text → blocks for the three text formats (LaTeX, Lilia text or JSON, Markdown), in memory. The paste and text
/// import (<see cref="JobService"/>) and the paste preview (phase 2 of paste → LaTeX, 11 Nov 2026) read text the same
/// way, so what the preview shows is what the import makes. Nothing is saved here.
/// </summary>
public static class PastedText
{
    public static async Task<PastedBlocks> ParseAsync(
        string? content, string format, ILatexParser latexParser, ILmlTextParser lmlParser, bool autoDetectEquations = true,
        Action<Exception, string>? warn = null)
    {
        content ??= "";
        var f = format.ToUpperInvariant();
        if (f == "AUTO") f = PasteFormat.Detect(content);

        if (f == "LML")
        {
            // 1) A Lilia JSON export: { document: { blocks: [...] } } or { blocks: [...] }
            var trimmed = content.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(content);
                    var blocks = json.ValueKind == JsonValueKind.Object
                        && json.TryGetProperty("document", out var d) && d.ValueKind == JsonValueKind.Object
                        && d.TryGetProperty("blocks", out var b1) && b1.ValueKind == JsonValueKind.Array && b1.GetArrayLength() > 0
                            ? b1
                            : json.ValueKind == JsonValueKind.Object && json.TryGetProperty("blocks", out var b2)
                              && b2.ValueKind == JsonValueKind.Array && b2.GetArrayLength() > 0
                                ? b2
                                : (JsonElement?)null;
                    if (blocks is { } list)
                        return new PastedBlocks(f, null, false, list.EnumerateArray().Select(FromJson).ToList(), null);
                }
                catch (JsonException ex)
                {
                    warn?.Invoke(ex, "LML content looked like JSON but failed to parse; trying text LML");
                }
            }
            // 2) Lilia text: @heading / @paragraph / @theorem …
            if (lmlParser.LooksLikeTextLml(content))
            {
                var parsed = lmlParser.Parse(content);
                if (parsed.Blocks.Count > 0)
                    return new PastedBlocks(f, parsed.Title, !string.IsNullOrWhiteSpace(parsed.Title),
                        parsed.Blocks.Select(x => new PastedBlock(x.Type, x.Content, x.Depth)).ToList(), null);
            }
            // 3) Last resort: the text as one paragraph.
            return new PastedBlocks(f, null, false, [new PastedBlock("paragraph", new { text = content })], "paragraph");
        }

        if (f == "MARKDOWN")
            return new PastedBlocks(f, null, false,
                LatexImportJobExecutor.MapElements(MarkdownTextParser.Parse(content)).Select(x => new PastedBlock(x.type, x.content)).ToList(), null);

        // LaTeX: a full document or a fragment.
        try
        {
            var doc = await latexParser.ParseTextAsync(content, new LatexImportOptions
            {
                ExtractDocumentTitle = true,
                ConvertEquationEnvironments = autoDetectEquations,
                ConvertDisplayMath = autoDetectEquations,
            });
            var title = string.IsNullOrWhiteSpace(doc.Title) ? null : doc.Title;
            if (doc.Elements.Count == 0)
                return new PastedBlocks("LATEX", title, false, [new PastedBlock("paragraph", new { text = content })], "paragraph");
            // The upload path's mapping (LatexImportJobExecutor), not a copy: a copy had drifted before (15 Oct).
            return new PastedBlocks("LATEX", title, false,
                LatexImportJobExecutor.MapElements(doc.Elements).Select(x => new PastedBlock(x.type, x.content)).ToList(), null)
            {
                TitleBlock = LatexImportJobExecutor.TitleBlockFor(doc) is { } t ? new PastedBlock(t.type, t.content) : null,
            };
        }
        catch (Exception ex)
        {
            warn?.Invoke(ex, "LaTeX parsing failed, falling back to a code block");
            return new PastedBlocks("LATEX", null, false, [new PastedBlock("code", new { code = content, language = "latex" })], "code");
        }
    }

    private static PastedBlock FromJson(JsonElement block) => new(
        block.TryGetProperty("type", out var t) ? t.GetString() ?? "paragraph" : "paragraph",
        block.TryGetProperty("content", out var c) ? c.Clone() : JsonDocument.Parse("{}").RootElement.Clone(),
        block.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0);

    /// <summary>The block's content as stored (a JSON document).</summary>
    public static JsonDocument ContentJson(PastedBlock block) => block.Content is JsonElement e
        ? JsonDocument.Parse(e.GetRawText())
        : JsonDocument.Parse(JsonSerializer.Serialize(block.Content));
}
