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

    /// <summary>
    /// Raw LaTeX for the document's preamble, from a Markdown file's <c>header-includes</c> (MarkdownFrontMatter,
    /// launch bot #106, 14 Nov 2026). Empty for the other formats: LaTeX carries its own preamble.
    /// </summary>
    public IReadOnlyList<string> PreambleLatex { get; init; } = [];
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
        {
            // YAML front matter is metadata, not text: it became one run-on heading (#106, 14 Nov 2026).
            var front = MarkdownFrontMatter.Read(content);
            var title = string.IsNullOrWhiteSpace(front.Title) ? null : front.Title;
            PastedBlock? titleBlock = front.Found && (title ?? front.Author ?? front.Date) is not null
                ? new PastedBlock("title", new Dictionary<string, object> { ["title"] = title ?? "", ["author"] = front.Author ?? "", ["date"] = front.Date ?? "" })
                : null;
            return new PastedBlocks(f, title, title is not null,
                LatexImportJobExecutor.MapElements(MarkdownTextParser.Parse(front.Body)).Select(x => new PastedBlock(x.type, x.content)).ToList(), null)
            {
                TitleBlock = titleBlock,
                PreambleLatex = front.HeaderIncludes,
            };
        }

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
            {
                // A whole document whose body holds nothing the parser keeps (only \titlepage or \maketitle): its title
                // page, as a Title block from \title, \author and \date, or nothing. The source as one paragraph, the
                // fallback for text that isn't LaTeX, put \documentclass and the preamble in the body, and the PDF
                // stopped (the document sweep's chunk 7, a TeX Stack Exchange deck, 16 Nov 2026).
                if (IsWholeDocument(content))
                    return new PastedBlocks("LATEX", title, false,
                        LatexImportJobExecutor.TitleBlockFor(doc) is { } only ? [new PastedBlock(only.type, only.content)] : [], null);
                return new PastedBlocks("LATEX", title, false, [new PastedBlock("paragraph", new { text = content })], "paragraph");
            }
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

    /// <summary>A whole LaTeX document: a \documentclass and a \begin{document}, outside comments.</summary>
    internal static bool IsWholeDocument(string content)
    {
        var code = System.Text.RegularExpressions.Regex.Replace(content, @"(?m)(?<!\\)%.*$", "");
        return code.Contains(@"\documentclass", StringComparison.Ordinal) && code.Contains(@"\begin{document}", StringComparison.Ordinal);
    }

    private static PastedBlock FromJson(JsonElement block) => new(
        block.TryGetProperty("type", out var t) ? t.GetString() ?? "paragraph" : "paragraph",
        block.TryGetProperty("content", out var c) ? c.Clone() : JsonDocument.Parse("{}").RootElement.Clone(),
        block.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0);

    /// <summary>
    /// A Markdown file that opens with its only level-1 heading names itself with it ("# My paper"): imported, that
    /// heading became section 1 and the document took the file's name (the connector test, 17 Nov 2026). The heading
    /// becomes the title (in the title block, with any front-matter author and date). For a file's import only: pasted
    /// into a document, a level-1 heading is a section.
    /// </summary>
    public static PastedBlocks WithLeadingH1AsTitle(PastedBlocks pasted, string? source)
    {
        if (pasted.Format != "MARKDOWN" || pasted.Title is not null || pasted.Blocks.Count == 0) return pasted;
        // The source's own "# " headings, outside code fences: the parsed levels are relative (a text whose top heading
        // is "## Results" reads it as level 1), and "## Results" opening a chat answer is a section, not a title.
        var fenced = false;
        var h1 = new List<int>();
        var lines = MarkdownFrontMatter.Read(source ?? "").Body.Split('\n');
        var firstText = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (fenced) continue;
            if (firstText < 0 && line.Trim().Length > 0) firstText = i;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^#[ \t]+\S")) h1.Add(i);
        }
        if (h1.Count != 1 || h1[0] != firstText) return pasted;
        static int? Level(PastedBlock b) => b.Type == "heading" && ContentJson(b).RootElement is { ValueKind: JsonValueKind.Object } c
            && c.TryGetProperty("level", out var l) && l.TryGetInt32(out var n) ? n : null;
        if (Level(pasted.Blocks[0]) != 1 || pasted.Blocks.Skip(1).Any(b => Level(b) == 1)) return pasted;
        var root = ContentJson(pasted.Blocks[0]).RootElement;
        var title = root.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
        if (title.Length == 0) return pasted;
        var front = pasted.TitleBlock is { } tb ? ContentJson(tb).RootElement : default;
        string Field(string name) => front.ValueKind == JsonValueKind.Object && front.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        return pasted with
        {
            Title = title,
            TitleFromText = true,
            Blocks = pasted.Blocks.Skip(1).ToList(),
            TitleBlock = new PastedBlock("title", new Dictionary<string, object> { ["title"] = title, ["author"] = Field("author"), ["date"] = Field("date") }),
        };
    }

    /// <summary>The block's content as stored (a JSON document).</summary>
    public static JsonDocument ContentJson(PastedBlock block) => block.Content is JsonElement e
        ? JsonDocument.Parse(e.GetRawText())
        : JsonDocument.Parse(JsonSerializer.Serialize(block.Content));
}
