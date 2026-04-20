using System.Text.Json;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Single-block LaTeX fragment → block content JSONB. See ILatexFragmentParser
/// for intent and <see cref="LatexFragmentParseException"/> for error contract.
/// </summary>
public class LatexFragmentParser : ILatexFragmentParser
{
    private readonly ILatexParser _latexParser;

    public LatexFragmentParser(ILatexParser latexParser)
    {
        _latexParser = latexParser;
    }

    public async Task<JsonDocument> ParseFragmentAsync(string latex, string targetBlockType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(latex))
        {
            throw new LatexFragmentParseException(
                "EMPTY_FRAGMENT",
                "LaTeX fragment is empty.",
                new[] { new FragmentDiagnostic(1, 1, "error", "EMPTY_FRAGMENT", "Fragment contains no LaTeX content.") });
        }

        var wrapped = WrapFragment(latex);
        ImportDocument parsed;
        try
        {
            parsed = await _latexParser.ParseTextAsync(wrapped, new LatexImportOptions
            {
                OnlyDocumentContent = true,
                ExtractDocumentTitle = false,
            });
        }
        catch (Exception ex)
        {
            throw new LatexFragmentParseException(
                "PARSE_ERROR",
                $"LaTeX fragment failed to parse: {ex.Message}",
                new[] { new FragmentDiagnostic(null, null, "error", "PARSE_ERROR", ex.Message) });
        }

        if (parsed.Elements.Count == 0)
        {
            throw new LatexFragmentParseException(
                "NO_ELEMENTS",
                "Fragment parsed but produced no blocks.",
                BuildWarningDiagnostics(parsed));
        }

        // For list blocks the parser emits one ImportListItem per \item; fold
        // consecutive items into a single list content shape.
        if (targetBlockType == "list")
        {
            return BuildListContent(parsed.Elements);
        }

        var element = parsed.Elements[0];
        var mapped = MapElementToBlock(element);

        if (mapped.Type != targetBlockType)
        {
            throw new LatexFragmentParseException(
                "TYPE_MISMATCH",
                $"Fragment parsed as '{mapped.Type}', expected '{targetBlockType}'. The LaTeX tab cannot change a block's type.",
                new[] { new FragmentDiagnostic(null, null, "error", "TYPE_MISMATCH",
                    $"Expected {targetBlockType}, got {mapped.Type}.") });
        }

        return JsonSerializer.SerializeToDocument(mapped.Content);
    }

    // Wrap a fragment in a minimal document so LatexParser's main-body
    // extraction path fires. We deliberately include amsmath/graphicx here so
    // fragments using \begin{align} / \includegraphics don't warn about
    // missing packages — they're never going to compile standalone anyway.
    private static string WrapFragment(string latex) =>
        "\\documentclass{article}\n" +
        "\\usepackage{amsmath,amssymb,graphicx}\n" +
        "\\begin{document}\n" +
        latex +
        "\n\\end{document}\n";

    // Mirror of LatexImportJobExecutor.MapImportElementToBlock — the same
    // mapping the import pipeline uses when writing fresh blocks. Kept as a
    // local copy so the two paths stay decoupled (one is for staging-area
    // INSERTs, the other for in-place UPDATEs).
    private static (string Type, object Content) MapElementToBlock(ImportElement element) => element switch
    {
        ImportHeading h => ("heading", new { text = h.Text, level = h.Level }),
        ImportParagraph p => ("paragraph", new { text = p.Text }),
        ImportEquation eq => ("equation", new
        {
            latex = eq.LatexContent ?? eq.OmmlXml,
            equationMode = eq.IsInline ? "inline" : "display",
        }),
        ImportCodeBlock c => ("code", new { code = c.Text, language = c.Language ?? "" }),
        ImportTable t => ("table", new
        {
            headers = t.HasHeaderRow && t.Rows.Count > 0
                ? t.Rows[0].Select(cell => cell.Text).ToArray()
                : Enumerable.Range(0, t.ColumnCount).Select(i => $"Column {i + 1}").ToArray(),
            rows = (t.HasHeaderRow ? t.Rows.Skip(1) : t.Rows)
                .Select(r => r.Select(cell => cell.Text).ToArray()).ToArray(),
        }),
        ImportAbstract a => ("abstract", new { text = a.Text }),
        ImportTheorem th => ("theorem", new
        {
            text = th.Text,
            theoremType = th.EnvironmentType.ToString().ToLowerInvariant(),
            title = th.Title ?? string.Empty,
            label = th.Label ?? string.Empty,
        }),
        ImportListItem li => ("list", new
        {
            items = new[] { li.Text },
            ordered = li.IsNumbered,
        }),
        ImportPageBreak => ("pageBreak", new { }),
        ImportImage img => ("figure", new
        {
            src = string.Empty,
            caption = img.AltText ?? string.Empty,
            alt = img.AltText ?? string.Empty,
        }),
        ImportBlockquote bq => ("blockquote", new { text = bq.Text }),
        ImportLatexPassthrough lp => ("code", new { code = lp.LatexCode, language = "latex" }),
        _ => ("paragraph", new { text = string.Empty }),
    };

    // Fold a run of consecutive ImportListItem elements into a single list
    // block content. Falls back to a one-item list if only one item was
    // emitted, and respects the first item's ordered flag (LaTeX fragments
    // can't mix itemize + enumerate at the top level in one block).
    private static JsonDocument BuildListContent(IReadOnlyList<ImportElement> elements)
    {
        var items = new List<string>();
        bool ordered = false;
        bool anyItem = false;

        foreach (var el in elements)
        {
            if (el is ImportListItem li)
            {
                items.Add(li.Text);
                if (!anyItem)
                {
                    ordered = li.IsNumbered;
                    anyItem = true;
                }
            }
        }

        if (!anyItem)
        {
            throw new LatexFragmentParseException(
                "TYPE_MISMATCH",
                "Fragment did not parse as a list. Expected \\begin{itemize} or \\begin{enumerate}.",
                new[] { new FragmentDiagnostic(null, null, "error", "TYPE_MISMATCH",
                    "No \\item entries found.") });
        }

        return JsonSerializer.SerializeToDocument(new
        {
            items = items.ToArray(),
            ordered,
        });
    }

    private static FragmentDiagnostic[] BuildWarningDiagnostics(ImportDocument doc)
    {
        if (doc.Warnings.Count == 0)
        {
            return new[] { new FragmentDiagnostic(null, null, "error", "NO_ELEMENTS", "Fragment produced no blocks.") };
        }
        return doc.Warnings
            .Select(w => new FragmentDiagnostic(null, null, "warning", w.Type.ToString(), w.Message ?? string.Empty))
            .ToArray();
    }
}
