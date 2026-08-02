using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Lilia.Engines;

namespace Lilia.Tools.Api.Services;

/// <summary>
/// The outcome of actually compiling a tool's output, so the UI can state what
/// is known rather than assert what is hoped. <c>Status</c> is one of:
/// <c>verified</c> (compiled clean), <c>failed</c> (compiled with errors — the
/// findings say which), or <c>unchecked</c> (no compiler reachable; claim nothing).
/// </summary>
public record ToolVerdict(string Status, string[] Findings, int DurationMs)
{
    public static readonly ToolVerdict Unchecked = new("unchecked", [], 0);
}

public record ToolRunResult(string Output, string Format, string? Title, ToolVerdict? Verdict = null);

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
    /// <summary>
    /// Verification has to finish inside a web request, so it gets a much shorter
    /// leash than the 30s default. A table that can't compile in this long is
    /// reported as unchecked rather than made to wait.
    /// </summary>
    private const int VerifyTimeoutSeconds = 15;

    private readonly IBibliographyService _bibliography;
    private readonly IRenderService _render;
    private readonly IDocxImportService _docx;
    private readonly ICompilationQueueService _compiler;
    private readonly ILogger<ToolRunnerService> _logger;

    public ToolRunnerService(
        IBibliographyService bibliography,
        IRenderService render,
        IDocxImportService docx,
        ICompilationQueueService compiler,
        ILogger<ToolRunnerService> logger)
    {
        _bibliography = bibliography;
        _render = render;
        _docx = docx;
        _compiler = compiler;
        _logger = logger;
    }

    public async Task<ToolRunResult> RunAsync(Tool tool, JsonElement input, IFormFile? file, CancellationToken ct)
    {
        return tool.Engine switch
        {
            "doi" => await RunDoiAsync(input),
            "table" => await RunTableAsync(input),
            "word" => await RunWordAsync(file, ct),
            _ => throw new ToolInputException($"Unknown tool engine '{tool.Engine}'."),
        };
    }

    // ── DOI / ISBN / arXiv → BibTeX (routes to the right BibliographyService lookup) ──
    private async Task<ToolRunResult> RunDoiAsync(JsonElement input)
    {
        var raw = (TryGetString(input, "doi") ?? TryGetString(input, "id"))?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ToolInputException("Provide a DOI, ISBN, or arXiv id.");

        var arxiv = Regex.Match(raw, @"(?:arxiv:)?\s*(\d{4}\.\d{4,5}(?:v\d+)?)$", RegexOptions.IgnoreCase);
        var arxivOld = Regex.IsMatch(raw, @"^[a-z\-]+(?:\.[A-Z]{2})?/\d{7}$", RegexOptions.IgnoreCase);
        var isbnDigits = new string(raw.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray());

        DoiLookupResultDto? result;
        if (raw.StartsWith("arxiv", StringComparison.OrdinalIgnoreCase) || arxiv.Success || arxivOld)
        {
            var aid = arxiv.Success ? arxiv.Groups[1].Value
                : raw.Replace("arXiv:", "", StringComparison.OrdinalIgnoreCase).Trim();
            result = await _bibliography.LookupArxivAsync(aid);
        }
        else if (!raw.Contains('/') && (isbnDigits.Length == 10 || isbnDigits.Length == 13))
        {
            result = await _bibliography.LookupIsbnAsync(isbnDigits);
        }
        else
        {
            var doi = raw.Replace("https://doi.org/", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("http://doi.org/", "", StringComparison.OrdinalIgnoreCase)
                         .Replace("doi.org/", "", StringComparison.OrdinalIgnoreCase).Trim();
            result = await _bibliography.LookupDoiAsync(doi);
        }

        if (result is null)
            throw new ToolInputException("No record found for that identifier.");

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
    private async Task<ToolRunResult> RunTableAsync(JsonElement input)
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
        return new ToolRunResult(latex, "latex", "Table", await VerifyAsync(latex));
    }

    /// <summary>
    /// Compile the fragment and report what actually happened. Never throws: if the
    /// compiler is unreachable (no TeX on a dev box, queue saturated, timeout) the
    /// verdict is <c>unchecked</c>, because claiming a table compiles when nothing
    /// compiled it is the exact failure this tool exists to prevent.
    /// </summary>
    private async Task<ToolVerdict> VerifyAsync(string latexFragment)
    {
        try
        {
            var document = LaTeXPreamble.WrapForValidation(latexFragment);
            var result = await _compiler.CompileLatexAsync(document, CompilationType.Validate, VerifyTimeoutSeconds);
            var ms = (int)result.Duration.TotalMilliseconds;

            if (result.Success)
                return new ToolVerdict("verified", [], ms);

            return new ToolVerdict("failed", ExtractFindings(result), ms);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Tools] verification unavailable — reporting unchecked");
            return ToolVerdict.Unchecked;
        }
    }

    /// <summary>
    /// Pull the human-readable causes out of a failed compile. LaTeX errors are the
    /// lines starting with `!`; warnings are the fallback when the log has no error
    /// line (an overfull box fails validation without an `!`).
    /// </summary>
    private static string[] ExtractFindings(CompilationResult result)
    {
        var errors = (result.Error ?? string.Empty)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith('!'))
            .Select(l => l.TrimStart('!').Trim())
            .Where(l => l.Length > 0)
            .Take(3)
            .ToArray();

        if (errors.Length > 0) return errors;

        var warnings = result.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Take(3).ToArray();
        return warnings.Length > 0 ? warnings : ["The table did not compile, and LaTeX gave no reason."];
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
