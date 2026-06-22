using System.Text;
using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;

namespace Lilia.Api.Services;

public record ToolRunResult(string Output, string Format, string? Title);

public interface IToolRunnerService
{
    /// <summary>
    /// Execute a tool over Lilia's existing engines. <paramref name="input"/> carries
    /// the JSON payload for text/grid tools; <paramref name="file"/> the upload for file
    /// tools. Throws <see cref="ToolInputException"/> on bad input.
    /// </summary>
    Task<ToolRunResult> RunAsync(Tool tool, JsonElement input, IFormFile? file, CancellationToken ct);
}

public sealed class ToolInputException : Exception
{
    public ToolInputException(string message) : base(message) { }
}

/// <summary>
/// The standalone-tool executor: thin dispatch over existing engines
/// (bibliography lookup, table render, DOCX import) — no new conversion logic.
/// See lilia-docs/features/2026-06-22-standalone-tools-strategy.md §8.
/// </summary>
public class ToolRunnerService : IToolRunnerService
{
    private readonly IBibliographyService _bibliography;
    private readonly IRenderService _render;
    private readonly IDocxImportService _docx;
    private readonly ILogger<ToolRunnerService> _logger;

    public ToolRunnerService(
        IBibliographyService bibliography,
        IRenderService render,
        IDocxImportService docx,
        ILogger<ToolRunnerService> logger)
    {
        _bibliography = bibliography;
        _render = render;
        _docx = docx;
        _logger = logger;
    }

    public async Task<ToolRunResult> RunAsync(Tool tool, JsonElement input, IFormFile? file, CancellationToken ct)
    {
        return tool.Engine switch
        {
            "doi" => await RunDoiAsync(input),
            "table" => RunTable(input),
            "word" => await RunWordAsync(file, ct),
            _ => throw new ToolInputException($"Unknown tool engine '{tool.Engine}'."),
        };
    }

    // ── DOI → BibTeX (over BibliographyService.LookupDoiAsync) ───────────────
    private async Task<ToolRunResult> RunDoiAsync(JsonElement input)
    {
        var doi = TryGetString(input, "doi")?.Trim();
        if (string.IsNullOrWhiteSpace(doi))
            throw new ToolInputException("Provide a DOI (or DOI URL).");
        // Accept a pasted DOI URL too.
        doi = doi.Replace("https://doi.org/", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("http://doi.org/", "", StringComparison.OrdinalIgnoreCase)
                 .Replace("doi.org/", "", StringComparison.OrdinalIgnoreCase).Trim();

        var result = await _bibliography.LookupDoiAsync(doi);
        if (result is null)
            throw new ToolInputException("No record found for that DOI.");

        var bibtex = FormatBibTex(result);
        var title = TryGetString(result.Data, "title");
        return new ToolRunResult(bibtex, "bibtex", title);
    }

    private static string FormatBibTex(DoiLookupResultDto dto)
    {
        var sb = new StringBuilder();
        sb.Append('@').Append(dto.EntryType).Append('{').Append(dto.CiteKey).Append(',');
        if (dto.Data.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in dto.Data.EnumerateObject())
            {
                var val = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                if (string.IsNullOrWhiteSpace(val)) continue;
                sb.Append("\n  ").Append(p.Name).Append(" = {").Append(val).Append("},");
            }
        }
        if (sb.Length > 0 && sb[^1] == ',') sb.Length--;
        sb.Append("\n}\n");
        return sb.ToString();
    }

    // ── Table grid → booktabs LaTeX (over RenderService.RenderBlockToLatex) ──
    private ToolRunResult RunTable(JsonElement input)
    {
        if (!input.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
            throw new ToolInputException("Provide at least one row.");

        // Pass the grid straight through as a table block's content — the render
        // service already produces booktabs LaTeX from { headers, rows, caption }.
        var content = JsonSerializer.Serialize(new
        {
            headers = input.TryGetProperty("headers", out var h) ? (object)h : Array.Empty<string>(),
            rows = (object)rows,
            caption = TryGetString(input, "caption") ?? "",
        });
        var block = new Block { Type = "table", Content = JsonDocument.Parse(content) };
        var latex = _render.RenderBlockToLatex(block);
        return new ToolRunResult(latex, "latex", "Table");
    }

    // ── .docx → LaTeX (over DocxImportService → blocks → RenderBlockToLatex) ─
    private async Task<ToolRunResult> RunWordAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new ToolInputException("Upload a .docx file.");
        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            throw new ToolInputException("Only .docx files are supported here (Word/PDF preview is coming).");

        var tmp = Path.Combine(Path.GetTempPath(), $"lilia-tool-{Guid.NewGuid():N}.docx");
        try
        {
            await using (var fs = File.Create(tmp))
                await file.CopyToAsync(fs, ct);

            var result = await _docx.ImportAsync(tmp);
            if (!result.Success || result.Document is null)
                throw new ToolInputException("Couldn't read that .docx file.");

            var latex = ImportToLatex(result.Document);
            var title = string.IsNullOrWhiteSpace(result.Document.Title)
                ? Path.GetFileNameWithoutExtension(file.FileName)
                : result.Document.Title;
            return new ToolRunResult(latex, "latex", title);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    // Import model (sections → blocks) → a clean LaTeX skeleton. Intentionally a
    // quick "first draft" — full-fidelity equations/tables/citations + review are
    // the "Open in Lilia" (import-review) upsell, per strategy §7/§8.
    private static string ImportToLatex(Lilia.Core.Models.Document doc)
    {
        var sb = new StringBuilder();
        foreach (var section in doc.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Title))
                sb.Append("\\section{").Append(LatexEscape(section.Title)).Append("}\n\n");

            foreach (var block in section.Blocks.OrderBy(b => b.SortOrder))
            {
                var c = block.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(c)) continue;
                switch (block.BlockType)
                {
                    case Lilia.Core.Models.BlockType.Equation:
                        sb.Append("\\[\n").Append(c.Trim()).Append("\n\\]\n\n"); break;       // OMML already converted to LaTeX
                    case Lilia.Core.Models.BlockType.Code:
                        sb.Append("\\begin{verbatim}\n").Append(c).Append("\n\\end{verbatim}\n\n"); break;
                    case Lilia.Core.Models.BlockType.Blockquote:
                        sb.Append("\\begin{quote}\n").Append(LatexEscape(c)).Append("\n\\end{quote}\n\n"); break;
                    case Lilia.Core.Models.BlockType.Abstract:
                        sb.Append("\\begin{abstract}\n").Append(LatexEscape(c)).Append("\n\\end{abstract}\n\n"); break;
                    case Lilia.Core.Models.BlockType.List:
                        sb.Append("\\begin{itemize}\n");
                        foreach (var line in c.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            sb.Append("  \\item ").Append(LatexEscape(line)).Append('\n');
                        sb.Append("\\end{itemize}\n\n"); break;
                    case Lilia.Core.Models.BlockType.Table:
                        // Table content arrives pre-converted; pass through.
                        sb.Append(c.Trim()).Append("\n\n"); break;
                    case Lilia.Core.Models.BlockType.PageBreak:
                        sb.Append("\\clearpage\n\n"); break;
                    default: // Paragraph / Theorem / Figure / Bibliography / ToC
                        sb.Append(LatexEscape(c.Trim())).Append("\n\n"); break;
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string LatexEscape(string s) => s
        .Replace("\\", "\\textbackslash{}")
        .Replace("&", "\\&").Replace("%", "\\%").Replace("$", "\\$").Replace("#", "\\#")
        .Replace("_", "\\_").Replace("{", "\\{").Replace("}", "\\}")
        .Replace("~", "\\textasciitilde{}").Replace("^", "\\textasciicircum{}");

    private static string? TryGetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
