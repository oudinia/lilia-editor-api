using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lilia.Api.Mcp;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// "Does this document compile, and what is wrong where?" — one implementation for the connector's
/// check_document and Ask Lilia's (15 Oct: Ask Lilia could edit but not check its own fixes, only the
/// connector could). Compiles the LaTeX Lilia emits, maps the error and the warnings to the blocks they
/// come from, and keeps the verdict per document version (<see cref="CheckMemoStore"/>) for export_document.
/// </summary>
public interface IDocumentCheckService
{
    /// <summary>Check the document as it is saved now. A version already checked is not compiled again.</summary>
    Task<DocumentCheck> CheckAsync(Lilia.Core.DTOs.DocumentDto d, CancellationToken ct = default);

    /// <summary>
    /// Compile a LaTeX source that belongs to no saved document (the paste preview, 11 Nov 2026) and judge it the
    /// same way. Block ids in the verdict are the ones in the source's <c>% block:{id}</c> markers.
    /// </summary>
    Task<DocumentCheck> CheckSourceAsync(string source, string? engine = null, CancellationToken ct = default);

    /// <summary>Wait (up to <paramref name="max"/>) for a check of this document that is still compiling.</summary>
    Task WaitForRunningAsync(Guid documentId, TimeSpan max);
}

/// <summary>One thing wrong, and where: the block (null in Lilia's preamble or when unknown), the line of the emitted LaTeX.</summary>
public sealed record BlockProblem(string? BlockId, int? Line, string Severity, string Message);

/// <summary>
/// A check's verdict. <see cref="Report"/> is what the tools return (the connector's shape, plus
/// <c>problems</c>); <see cref="Compiles"/> is null when the compile could not run at all.
/// <see cref="Cached"/>: this version was checked before and nothing was compiled.
/// </summary>
public sealed record DocumentCheck(object Report, bool? Compiles, bool PrintsCorrectly, IReadOnlyList<BlockProblem> Problems, bool Cached);

public sealed partial class DocumentCheckService(
    Lilia.Engines.IRenderService render,
    ILaTeXRenderService latex,
    CheckMemoStore checks,
    IConfiguration config,
    ILogger<DocumentCheckService> logger,
    Lilia.Infrastructure.Data.LiliaDbContext? db = null) : IDocumentCheckService
{
    private const int MaxProblems = 30;

    /// <summary>Checks still compiling, by document. An app that calls check_document and
    /// export_document together got "Never checked" from the export (MCP-88): it now waits for the check.</summary>
    private static readonly ConcurrentDictionary<Guid, TaskCompletionSource> Running = new();

    /// <summary>The verdict per version: the document, the engine and the hash of the LaTeX emitted. Same source, same verdict.</summary>
    private static readonly ConcurrentDictionary<string, DocumentCheck> ByVersion = new();
    private const int MaxCachedVersions = 500;

    public async Task WaitForRunningAsync(Guid documentId, TimeSpan max)
    {
        if (Running.TryGetValue(documentId, out var checking))
            await Task.WhenAny(checking.Task, Task.Delay(max));
    }

    public async Task<DocumentCheck> CheckAsync(Lilia.Core.DTOs.DocumentDto d, CancellationToken ct = default)
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Running[d.Id] = running;
        try
        {
            return await CompileAsync(d);
        }
        finally
        {
            Running.TryRemove(new KeyValuePair<Guid, TaskCompletionSource>(d.Id, running));
            running.TrySetResult();
        }
    }

    public async Task<DocumentCheck> CheckSourceAsync(string source, string? engine = null, CancellationToken ct = default)
    {
        try
        {
            var use = LiliaMcpTools.EngineFor(source, engine);
            var result = await latex.ValidateAsync(source, use);
            return Judge(null, source, use, result);
        }
        catch (Exception ex) when (Failure(ex, null) is { } failed)
        {
            return failed;
        }
    }

    /// <summary>A compile that could not give a verdict, said plainly (MCP-45): refused, too long, or failed.</summary>
    private DocumentCheck? Failure(Exception ex, Guid? docId)
    {
        switch (ex)
        {
            case Lilia.Engines.TexSafety.UnsafeLatexException unsafeTex:
            {
                var message = $"Lilia refuses to compile this LaTeX: {unsafeTex.Message}";
                return new DocumentCheck(new { compiles = false, category = "refused", error = message }, false, false,
                    [new BlockProblem(null, null, "error", message)], false);
            }
            case TimeoutException or TaskCanceledException or OperationCanceledException:
            {
                logger.LogWarning(ex, "[Check] check timed out for {DocId}", docId);
                const string message = "The compile took too long and was stopped. A loop or a very heavy figure is the usual cause.";
                return new DocumentCheck(new { compiles = false, category = "timeout", error = message }, false, false,
                    [new BlockProblem(null, null, "error", message)], false);
            }
            default:
            {
                logger.LogWarning(ex, "[Check] check failed for {DocId}", docId);
                var message = $"Lilia could not compile this {(docId is null ? "LaTeX" : "document")}: {ex.Message}";
                return new DocumentCheck(new { category = "error", error = message }, null, false,
                    [new BlockProblem(null, null, "error", message)], false);
            }
        }
    }

    private string EditorUrl(Guid id) => $"{McpOAuth.PublicOrigin(config)}/d/{id}";

    private async Task<DocumentCheck> CompileAsync(Lilia.Core.DTOs.DocumentDto d)
    {
        try
        {
            var source = await render.RenderToLatexAsync(d.Id);
            // MCP-08: compile with the engine the emitted LaTeX needs (it loads fontspec for
            // xelatex/lualatex), or the document's own; validation was hard-wired to pdflatex.
            var engine = LiliaMcpTools.EngineFor(source, d.LatexEngine);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{d.Id}|{engine}|{source}")));
            if (ByVersion.TryGetValue(key, out var seen))
            {
                checks.Set(d.Id, new CheckMemoStore.Memo(d.UpdatedAt, DateTime.UtcNow, seen.Compiles == true, seen.PrintsCorrectly, ErrorOf(seen)));
                return seen with { Cached = true };
            }
            var result = await latex.ValidateAsync(source, engine);
            var check = Judge(d, source, engine, result, await PublisherAsync(d, source));
            if (ByVersion.Count >= MaxCachedVersions) ByVersion.Clear();
            ByVersion[key] = check;
            return check;
        }
        // MCP-45: say what happened. A refusal repeats, so "try again" was wrong for it.
        catch (Exception ex) when (Failure(ex, d.Id) is { } failed)
        {
            return failed;
        }
    }

    /// <summary>
    /// The publisher compliance check (PublisherCompliance) for a document with a publisher profile or
    /// class, so Ask Lilia and a connected app see what the publisher would reject (25 Oct 2026).
    /// Null for any other document, or without a database (tests).
    /// </summary>
    private async Task<Lilia.Engines.Publishers.ComplianceReport?> PublisherAsync(Lilia.Core.DTOs.DocumentDto d, string source)
    {
        if (db is null || (d.PublisherProfile is null && !Lilia.Engines.Publishers.PublisherClasses.Is(d.LatexDocumentClass))) return null;
        var doc = await db.Documents.AsNoTracking()
            .Include(x => x.Blocks.OrderBy(b => b.SortOrder))
            .Include(x => x.BibliographyEntries)
            .FirstOrDefaultAsync(x => x.Id == d.Id);
        if (doc is null) return null;
        var cls = Lilia.Engines.Publishers.PublisherClasses.Canonical(Lilia.Engines.LaTeXPreambleBuilder.ResolveClassName(doc));
        var installed = cls is null ? null : await Lilia.Engines.Publishers.PublisherClassVersions.InstalledAsync(cls);
        return Lilia.Engines.Publishers.PublisherCompliance.Check(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), source, installed);
    }

    private static readonly Regex EmptyFraction = new(@"\\[dt]?frac\s*(\{\s*\}|\{[^{}]*\}\s*\{\s*\})", RegexOptions.Compiled);

    /// <summary>1-based lines of the emitted LaTeX holding a fraction with an empty numerator or denominator.</summary>
    internal static IEnumerable<int> EmptyFractionLines(string source) =>
        source.Split('\n').Select((text, i) => (text, line: i + 1)).Where(x => EmptyFraction.IsMatch(x.text)).Select(x => x.line);

    private static string? ErrorOf(DocumentCheck c) => c.Problems.FirstOrDefault(p => p.Severity == "error")?.Message;

    /// <summary>
    /// What the references above do in the PDF, by kind (MCP-126): a label with no number before it
    /// prints nothing ("see also Listing ."), not "??"; a duplicate label points at the wrong number.
    /// The note said "?? or ?" for all of them.
    /// </summary>
    internal static string ReferenceNote(bool dangling, bool missingCitations, bool empty, bool duplicate)
    {
        var parts = new List<string>();
        if (dangling) parts.Add("references to labels the document never writes print as ??");
        if (missingCitations) parts.Add("citations with no bibliography entry print as ?");
        if (empty) parts.Add("references to a label with no numbered item before it print nothing");
        if (duplicate) parts.Add("a label written twice makes its references point at the wrong number");
        return "It compiles, but " + string.Join("; ", parts) + " (see references and citations above).";
    }

    private DocumentCheck Judge(Lilia.Core.DTOs.DocumentDto? d, string source, string engine, LatexValidationResult result,
        Lilia.Engines.Publishers.ComplianceReport? publisher = null)
    {
        // T1 (MCP-27/61, MCP-08): judge references and citations from the LaTeX actually emitted,
        // not the stored fields. A label the emitter never printed made "??" under a clean check,
        // and a one-pass compile can't report undefined references itself.
        var audit = new Lilia.Engines.EmittedLatexAudit(source);
        var references = audit.DanglingReferences
            .GroupBy(r => r.Key)
            .Select(g => new { kind = "dangling", label = g.Key, blockIds = g.Select(u => u.BlockId).Where(b => b is not null).Distinct() })
            .Cast<object>()
            .Concat(audit.DuplicateLabels.Select(l => (object)new { kind = "duplicate", label = l }))
            // MCP-92: a label with no number before it; the reference prints nothing.
            .Concat(audit.EmptyReferences.GroupBy(r => r.Key).Select(g => (object)new
            {
                kind = "empty", label = g.Key, blockIds = g.Select(u => u.BlockId).Where(b => b is not null).Distinct(),
                note = "No numbered item (caption, numbered equation or theorem, section) comes before this label, so the reference prints nothing.",
            }))
            .ToList();
        var citations = audit.MissingCitations
            .GroupBy(c => c.Key)
            .Select(g => new { kind = "missing", key = g.Key, blockIds = g.Select(u => u.BlockId).Where(b => b is not null).Distinct() })
            .ToList();
        // MCP-37: the block the compile error is in (the source carries a % block:{id} marker per block).
        var errorLine = result.ParsedError?.LineNumber;
        var errorBlockId = errorLine is int line ? audit.BlockAtLine(line) : null;
        // Blocks that print nothing: a type the emitter has no arm for (MCP-83).
        var unprinted = Regex.Matches(source, @"% Unknown block type: (\S+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        // Characters the font doesn't have compile, but are missing from the PDF (13 Oct sweep:
        // LTX-34's Cyrillic, with printsCorrectly true).
        var dropped = result.Warnings.FirstOrDefault(w => w.Contains(LaTeXGlyphScanner.DroppedMarker, StringComparison.Ordinal));
        // MCP-63: tables wider than their column run into the margin or the other column.
        var wideFound = Lilia.Engines.WideTables.Find(result.Log, source).ToList();
        var wide = wideFound
            .Select(o => new { blockId = audit.BlockAtLine(o.FirstLine), tooWidePt = Math.Round(o.Points, 1) })
            .ToList();
        // MCP-130: code lines wider than their column, the same way.
        var wideCodeFound = Lilia.Engines.WideTables.FindCode(result.Log, source).ToList();
        var wideCode = wideCodeFound
            .Select(o => new { blockId = audit.BlockAtLine(o.FirstLine), tooWidePt = Math.Round(o.Points, 1) })
            .ToList();

        var prints = result.Valid && references.Count + citations.Count == 0 && dropped is null && unprinted.Count == 0 && wide.Count == 0 && wideCode.Count == 0;
        if (d is not null) checks.Set(d.Id, new CheckMemoStore.Memo(d.UpdatedAt, DateTime.UtcNow, result.Valid, prints, result.Error));

        // Per block: what is wrong, where (15 Oct, for Ask Lilia's repair loop).
        var problems = new List<BlockProblem>();
        if (!result.Valid)
            problems.Add(new BlockProblem(errorBlockId?.ToString(), errorLine, "error",
                FirstLine(result.ParsedError?.ErrorRaw) ?? FirstLine(result.Error?.Replace("LaTeX compilation failed:", "")) ?? "It does not compile."));
        foreach (var w in result.Warnings)
        {
            var at = InputLine().Match(w) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var l) ? l : (int?)null;
            problems.Add(new BlockProblem(at is int n ? audit.BlockAtLine(n)?.ToString() : null, at, "warning", w));
        }
        foreach (var r in audit.DanglingReferences.Where(u => u.BlockId is not null).DistinctBy(u => (u.Key, u.BlockId)))
            problems.Add(new BlockProblem(r.BlockId.ToString(), r.Line, "warning", $"Reference to '{r.Key}', a label the document never writes: it prints ??."));
        foreach (var c in audit.MissingCitations.Where(u => u.BlockId is not null).DistinctBy(u => (u.Key, u.BlockId)))
            problems.Add(new BlockProblem(c.BlockId.ToString(), c.Line, "warning", $"Citation '{c.Key}' has no bibliography entry: it prints ?."));
        foreach (var o in wideFound)
            problems.Add(new BlockProblem(audit.BlockAtLine(o.FirstLine)?.ToString(), o.FirstLine, "warning",
                $"The table is {Math.Round(o.Points, 1)}pt wider than its column."));
        foreach (var o in wideCodeFound)
            problems.Add(new BlockProblem(audit.BlockAtLine(o.FirstLine)?.ToString(), o.FirstLine, "warning",
                $"A line of this code is {Math.Round(o.Points, 1)}pt wider than its column (listings wraps only at spaces)."));
        // A fraction with an empty part compiles and prints a blank (launch-bot #34, 13 Nov 2026: \frac{a}
        // was completed to \frac{a}{} and the check said all was well).
        foreach (var fracLine in EmptyFractionLines(source))
            problems.Add(new BlockProblem(audit.BlockAtLine(fracLine)?.ToString(), fracLine, "warning",
                "A fraction has an empty part (like \\frac{a}{}): it prints a blank there."));
        var listed = problems.Take(MaxProblems).ToList();

        var report = new
        {
            compiles = result.Valid,
            engine,
            printsCorrectly = prints,
            wideTables = wide.Count > 0 ? wide : null,
            wideCode = wideCode.Count > 0 ? wideCode : null,
            unprinted = unprinted.Count > 0 ? unprinted.Select(t => $"A {t} block prints nothing in the PDF.") : null,
            error = result.Error,
            errorCategory = result.ParsedError?.Category,
            // The command LaTeX doesn't know (MCP-37: only "Undefined control sequence." was said).
            errorCommand = result.ParsedError is { Category: "undefined_control_sequence", Token: { } token } ? token : null,
            errorBlockId,
            warnings = result.Warnings.Take(20),
            warningCount = result.Warnings.Length,
            references,
            citations,
            problems = listed.Select(p => new { blockId = p.BlockId, line = p.Line, severity = p.Severity, message = p.Message }),
            durationMs = result.DurationMs,
            // MCP-108: "It compiles, but…" was said of a document that doesn't compile.
            note = !result.Valid
                ? "It does not compile: see error (and errorBlockId for the block)."
                : references.Count + citations.Count > 0
                ? ReferenceNote(audit.DanglingReferences.Any(), citations.Count > 0, audit.EmptyReferences.Any(), audit.DuplicateLabels.Any())
                : dropped is not null
                    ? "It compiles, but some characters are missing from the PDF (see warnings)."
                    : wide.Count > 0
                        ? "It compiles, but a table is wider than its column and runs into the margin (wideTables)."
                        : wideCode.Count > 0
                            ? "It compiles, but a line of code is wider than its column and runs into the margin (wideCode)."
                            : null,
            url = d is null ? null : EditorUrl(d.Id),
            // What the publisher would reject or query, beside whether it compiles. Not in problems:
            // most are the author's to supply (an abstract, keywords, a country), not LaTeX to repair.
            publisher = publisher is null ? null : new
            {
                profile = publisher.Profile,
                status = publisher.Status,
                findings = publisher.Findings.Select(f => new { severity = f.Severity, code = f.Code, message = f.Message, blockId = f.BlockId }),
                guidelines = publisher.GuidelinesUrl,
            },
        };
        return new DocumentCheck(report, result.Valid, prints, listed, false);
    }

    private static string? FirstLine(string? text) =>
        text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    [GeneratedRegex(@"on input line (\d+)")]
    private static partial Regex InputLine();
}
