using System.Diagnostics;
using Lilia.Core.Entities;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Lilia.Engines;

namespace Lilia.Api.Services;

/// <summary>
/// Transparent preview compile path. Tries Typst first (sub-second compile);
/// on any failure falls through to pdflatex silently. Users never see the
/// engine choice — the editor frontend just gets a faster preview when
/// Typst can render the document, and the same PDF as before when it can't.
///
/// Phase 2 step 9 of the pre-launch Typst foundation. The fallback is
/// silent for the user but instrumented (step 11) — every fallback emits
/// a <c>silent_fallback</c> telemetry event so the next iteration of
/// LatexToTypstMath can target the most-frequent gaps.
/// </summary>
public interface IPreviewRenderService
{
    Task<PreviewRenderResult> RenderPdfAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Try the Typst path for this document. Returns the PDF bytes on
    /// success, null on any failure (with a silent_fallback telemetry
    /// event recorded). Callers decide their own fallback strategy
    /// (strict pdflatex, tolerant pdflatex, etc.) when this returns null.
    /// </summary>
    Task<byte[]?> TryTypstPdfAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// The accessible PDF made by Typst as PDF/UA-1 (launch-bot #259, 19 Nov 2026), or why it goes to LaTeX's tagged
    /// PDF instead: the document has maths (LaTeX tags it as MathML), or Typst refused something PDF/UA-1 forbids
    /// that Lilia doesn't fix by itself (a skipped heading level, for example).
    /// </summary>
    Task<TypstAccessiblePdf> TryAccessibleTypstPdfAsync(Guid documentId, CancellationToken ct = default);
}

/// <summary>The accessible PDF from Typst (PDF/UA-1), or null and the reason it goes to LaTeX (launch-bot #259).</summary>
public sealed record TypstAccessiblePdf(byte[]? Pdf, string? WhyLatex);

public sealed record PreviewRenderResult(
    byte[] Pdf,
    string Engine,
    long ElapsedMs);

public class PreviewRenderService : IPreviewRenderService
{
    private readonly LiliaDbContext _db;
    private readonly ITypstExportService _typstExporter;
    private readonly ITypstCompileService _typstCompiler;
    private readonly IRenderService _renderService;
    private readonly ILaTeXRenderService _latexService;
    private readonly IImportTelemetrySink _telemetry;
    private readonly IDocumentImageStager _imageStager;
    private readonly ILogger<PreviewRenderService> _logger;

    public PreviewRenderService(
        LiliaDbContext db,
        ITypstExportService typstExporter,
        ITypstCompileService typstCompiler,
        IRenderService renderService,
        ILaTeXRenderService latexService,
        IImportTelemetrySink telemetry,
        IDocumentImageStager imageStager,
        ILogger<PreviewRenderService> logger)
    {
        _db = db;
        _typstExporter = typstExporter;
        _typstCompiler = typstCompiler;
        _renderService = renderService;
        _latexService = latexService;
        _telemetry = telemetry;
        _imageStager = imageStager;
        _logger = logger;
    }

    /// <summary>
    /// Above this many blocks, pdflatex is measured to be the faster preview and
    /// Typst is not tried first.
    ///
    /// <para>Measured 2026-09-06 on an idle 8-core box, median of 3, same content
    /// emitted to both syntaxes (harness in <c>lilia-latex-service/bench</c>):</para>
    ///
    /// <code>
    ///   blocks     typst   pdflatex
    ///       10     0.064      0.413      typst 6.5x faster
    ///      100     0.143      0.395      typst 2.8x faster
    ///      300     0.299      0.460      typst 1.5x faster
    ///     1000     0.775      0.638      pdflatex faster
    /// </code>
    ///
    /// <para>Typst's advantage is real but it is a SMALL-document advantage, and
    /// it inverts somewhere between 300 and 1000 blocks. Trying Typst first on a
    /// 1000-block document costs ~140ms and then, if it falls through, the whole
    /// pdflatex compile on top.</para>
    ///
    /// <para>500 rather than a fitted crossover: the measured documents are plain
    /// (geometry + amsmath, one pass, no fontspec, no bibliography), so the
    /// absolute numbers are a floor and the true crossover moves with content.
    /// A round number inside the measured band is more honest than false
    /// precision, and it is one config value away from being changed.</para>
    /// </summary>
    private const int DefaultTypstFirstMaxBlocks = 500;

    public async Task<PreviewRenderResult> RenderPdfAsync(Guid documentId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var blockCount = await _db.Blocks.AsNoTracking()
            .CountAsync(b => b.DocumentId == documentId, ct);
        var typstFirst = blockCount <= DefaultTypstFirstMaxBlocks;

        // Typst ignores most page setup (margins, columns, headers, ...): a document that sets any of it
        // goes to LaTeX, which honours it. See PageSetupRouting.
        var setupDoc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        var whyLatex = setupDoc is null ? null : PageSetupRouting.WhyLatex(setupDoc);
        // …and content Typst would print wrong (ContentRouting, MCP-80).
        if (whyLatex is null && typstFirst)
            whyLatex = ContentRouting.WhyLatex((await _db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId)
                .Select(b => new { b.Type, b.Content }).ToListAsync(ct)).Select(b => (b.Type, b.Content.RootElement)));
        if (typstFirst && whyLatex is not null)
        {
            typstFirst = false;
            _logger.LogInformation("[Preview] {DocId} sets {Setup}, which Typst ignores; going to LaTeX", documentId, whyLatex);
        }
        else if (!typstFirst)
        {
            _logger.LogInformation(
                "[Preview] {Blocks} blocks is over the {Max}-block threshold — going straight to " +
                "pdflatex, which is measured faster at this size", blockCount,
                DefaultTypstFirstMaxBlocks);
        }

        var typstAttempt = typstFirst ? await TryTypstPdfAsync(documentId, ct) : null;
        if (typstAttempt is { } pdf)
        {
            sw.Stop();
            _logger.LogInformation("[Preview] Typst path succeeded for {DocId} in {Ms}ms", documentId, sw.ElapsedMilliseconds);
            return new PreviewRenderResult(pdf, "typst", sw.ElapsedMilliseconds);
        }

        var latex = await _renderService.RenderToLatexAsync(documentId);
        var explicitEngine = await _db.Documents.AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => d.LatexEngine)
            .FirstOrDefaultAsync(ct) ?? "pdflatex";
        // Auto-detect from the rendered full LaTeX (preamble + body).
        // User override can only force a HIGHER engine — forcing pdflatex
        // on a doc that uses fontspec would silently fail at compile.
        var detected = EngineDetector.Detect(latex);
        var explicitParsed = explicitEngine.ParseEngine();
        var chosen = detected > explicitParsed ? detected : explicitParsed;
        var engine = chosen.ToCli();
        var pdflatexPdf = await _latexService.RenderToPdfAsync(latex, engine);

        sw.Stop();
        _logger.LogInformation("[Preview] {Engine} path used for {DocId} in {Ms}ms (detected={Detected}, explicit={Explicit})",
            engine, documentId, sw.ElapsedMilliseconds, detected, explicitParsed);
        return new PreviewRenderResult(pdflatexPdf, engine, sw.ElapsedMilliseconds);
    }

    /// <summary>What a Typst build of the document reads: the document, its blocks, references and layout groups.</summary>
    private sealed record TypstInput(Document Doc, List<Block> Blocks, List<BibliographyEntry> BibEntries, List<BlockGroup> LayoutGroups);

    private async Task<TypstInput?> LoadTypstInputAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var doc = await _db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId, ct);
            if (doc == null) return null;

            var blocks = await _db.Blocks.AsNoTracking()
                .Where(b => b.DocumentId == documentId)
                .OrderBy(b => b.SortOrder)
                .ToListAsync(ct);

            // Load bib entries so #bibliography("references.bib") in
            // the generated Typst source resolves at compile time. Empty
            // list is fine — we just don't write the asset.
            var bibEntries = await _db.BibliographyEntries.AsNoTracking()
                .Where(b => b.DocumentId == documentId)
                .ToListAsync(ct);
            // And the references written in the bibliography blocks, as the LaTeX PDF reads them.
            bibEntries.AddRange(BibliographyBlocks.Unstored(documentId, blocks, bibEntries.Select(e => e.CiteKey)));

            // Layout-dimension groups so the exporter can wrap the
            // right runs in `#columns(N)` (LILIA-136). Other dimensions
            // are not consulted here.
            var layoutGroups = await _db.BlockGroups.AsNoTracking()
                .Where(g => g.DocumentId == documentId
                         && g.Dimension == BlockGroupDimensions.Layout)
                .Include(g => g.Memberships)
                .ToListAsync(ct);
            return new TypstInput(doc, blocks, bibEntries, layoutGroups);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Preview] Typst pre-flight DB load failed for {DocId} — falling back", documentId);
            return null;
        }
    }

    /// <summary>
    /// Drop a references.bib alongside main.typ when there are entries.
    /// The directive is only emitted when there are, so an unfilled
    /// References section no longer costs a "file not found".
    /// </summary>
    private static Dictionary<string, string>? BibAsset(List<BibliographyEntry> bibEntries) =>
        bibEntries.Count == 0 ? null : new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["references.bib"] = BibTeXSerializer.SerializeForTypst(bibEntries),
        };

    public async Task<byte[]?> TryTypstPdfAsync(Guid documentId, CancellationToken ct = default)
    {
        if (await LoadTypstInputAsync(documentId, ct) is not { } input) return null;
        var (doc, blocks, bibEntries, layoutGroups) = input;

        // Resolve the document's figure images first: the generator needs to
        // know where each one will sit in the compile directory, or it emits a
        // placeholder rectangle instead. Nothing is fetched from the URL a
        // block names — see DocumentImageStager.
        var staged = await _imageStager.StageAsync(documentId, ct);

        string source;
        try
        {
            source = _typstExporter.BuildTypstDocument(doc, blocks, layoutGroups,
                new TypstExportOptions { LocalImagePaths = staged.Paths, TikzFigures = staged.Tikz, HasBibliography = bibEntries.Count > 0 });
        }
        catch (Exception ex)
        {
            EmitFallback(documentId, "typst-export-threw", ex.Message, sample: null);
            return null;
        }

        var assets = BibAsset(bibEntries);

        // A document with labels also asks Typst which number each got, so the
        // editor can show "table 1" after a preview rather than only after a
        // pdflatex compile.
        var probe = source.Contains($"<{TypstExportService.LabelNumbersTag}>", StringComparison.Ordinal)
            ? TypstExportService.LabelNumbersEval
            : null;
        var result = await _typstCompiler.CompileAsync(
            source, TypstOutputFormat.Pdf, assets, staged.Files, probe, ct);
        if (result.Success && result.Output is { Length: > 0 })
        {
            await KeepLabelNumbersAsync(documentId, result.Evaluated, ct);
            return result.Output;
        }

        EmitFallback(documentId, "typst-compile-failed", result.Error, sample: source);
        return null;
    }

    public async Task<TypstAccessiblePdf> TryAccessibleTypstPdfAsync(Guid documentId, CancellationToken ct = default)
    {
        if (await LoadTypstInputAsync(documentId, ct) is not { } input)
            return new TypstAccessiblePdf(null, "the document could not be read for Typst");
        var (doc, blocks, bibEntries, layoutGroups) = input;

        // The language as the LaTeX accessible PDF reads it: the setting, or what the text reads as ("auto").
        doc.Blocks = blocks;
        var language = DocumentLanguage.For(doc).Code;

        var staged = await _imageStager.StageAsync(documentId, ct);
        TypstAccessibleSource built;
        try
        {
            built = _typstExporter.BuildAccessibleTypstDocument(doc, blocks, layoutGroups, new TypstExportOptions
            {
                LocalImagePaths = staged.Paths, TikzFigures = staged.Tikz, HasBibliography = bibEntries.Count > 0, Language = language,
            });
        }
        catch (Exception ex)
        {
            return new TypstAccessiblePdf(null, "the Typst source could not be built: " + ex.Message);
        }

        // PDF/UA-1 carries a formula only as alt text; LaTeX's tagged PDF gives it as MathML, which a screen reader
        // can walk through. A document with maths keeps LaTeX's.
        if (built.HasMaths)
            return new TypstAccessiblePdf(null, "it has maths, which the LaTeX accessible PDF tags as MathML");

        var result = await _typstCompiler.CompileAsync(
            built.Source, TypstOutputFormat.Pdf, BibAsset(bibEntries), staged.Files, evalAfter: null, TypstPdfStandard.Ua1, ct);
        if (result.Success && result.Output is { Length: > 0 })
            return new TypstAccessiblePdf(result.Output, null);

        // What PDF/UA-1 refused, as Typst words it ("PDF/UA-1 error: skipped from heading level 1 to 3").
        var why = UaRefusal(result.Error);
        EmitFallback(documentId, "typst-ua1-refused", result.Error, sample: null);
        return new TypstAccessiblePdf(null, why);
    }

    /// <summary>The first error line of a refused Typst compile, without its "error: " prefix (launch-bot #259).</summary>
    internal static string UaRefusal(string? stderr)
    {
        var line = (stderr ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("error:", StringComparison.Ordinal));
        return line is null ? "Typst could not make a PDF/UA-1 file" : line["error:".Length..].Trim();
    }

    /// <summary>
    /// Keep the numbers this preview gave the document's labels, dated, as the
    /// pdflatex path does — the references panel and the editor read them from
    /// the same place, so whichever compile ran last is what they show.
    /// Best effort: the PDF is what was asked for.
    /// </summary>
    private async Task KeepLabelNumbersAsync(Guid documentId, string? evaluated, CancellationToken ct)
    {
        var stored = LabelNumbers.FromTypst(evaluated);
        if (stored is null) return;
        try
        {
            var now = DateTime.UtcNow;
            await _db.Documents
                .Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.LabelNumbers, stored)
                    .SetProperty(d => d.LabelNumbersAt, now), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Preview] could not keep label numbers for {DocId}", documentId);
        }
    }

    private void EmitFallback(Guid documentId, string reason, string? detail, string? sample)
    {
        try
        {
            var sampleHead = sample is { Length: > 0 }
                ? sample[..Math.Min(200, sample.Length)]
                : null;
            var detailHead = detail is { Length: > 0 }
                ? detail[..Math.Min(500, detail.Length)]
                : null;

            _telemetry.Record(new ImportTelemetryRecord
            {
                EventKind = "silent_fallback",
                Severity = "info",
                SourceFormat = "typst",
                TokenOrEnv = reason,
                BlockKindEmitted = "pdflatex-pdf",
                BlockKindExpected = "typst-pdf",
                SampleText = sampleHead,
                Metadata = detailHead is null
                    ? $"{{\"document_id\":\"{documentId}\"}}"
                    : $"{{\"document_id\":\"{documentId}\",\"detail\":{System.Text.Json.JsonSerializer.Serialize(detailHead)}}}",
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Preview] telemetry record swallowed");
        }
    }
}
