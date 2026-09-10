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
}

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
    private readonly ILogger<PreviewRenderService> _logger;

    public PreviewRenderService(
        LiliaDbContext db,
        ITypstExportService typstExporter,
        ITypstCompileService typstCompiler,
        IRenderService renderService,
        ILaTeXRenderService latexService,
        IImportTelemetrySink telemetry,
        ILogger<PreviewRenderService> logger)
    {
        _db = db;
        _typstExporter = typstExporter;
        _typstCompiler = typstCompiler;
        _renderService = renderService;
        _latexService = latexService;
        _telemetry = telemetry;
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

        if (!typstFirst)
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

    public async Task<byte[]?> TryTypstPdfAsync(Guid documentId, CancellationToken ct = default)
    {
        Document? doc;
        List<Block> blocks;
        List<BibliographyEntry> bibEntries;
        List<BlockGroup> layoutGroups;
        try
        {
            doc = await _db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId, ct);
            if (doc == null) return null;

            blocks = await _db.Blocks.AsNoTracking()
                .Where(b => b.DocumentId == documentId)
                .OrderBy(b => b.SortOrder)
                .ToListAsync(ct);

            // Load bib entries so #bibliography("references.bib") in
            // the generated Typst source resolves at compile time. Empty
            // list is fine — we just don't write the asset.
            bibEntries = await _db.BibliographyEntries.AsNoTracking()
                .Where(b => b.DocumentId == documentId)
                .ToListAsync(ct);

            // Layout-dimension groups so the exporter can wrap the
            // right runs in `#columns(N)` (LILIA-136). Other dimensions
            // are not consulted here.
            layoutGroups = await _db.BlockGroups.AsNoTracking()
                .Where(g => g.DocumentId == documentId
                         && g.Dimension == BlockGroupDimensions.Layout)
                .Include(g => g.Memberships)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Preview] Typst pre-flight DB load failed for {DocId} — falling back", documentId);
            return null;
        }

        string source;
        try
        {
            source = _typstExporter.BuildTypstDocument(doc, blocks, layoutGroups);
        }
        catch (Exception ex)
        {
            EmitFallback(documentId, "typst-export-threw", ex.Message, sample: null);
            return null;
        }

        // Drop a references.bib alongside main.typ when there are
        // entries; otherwise we'd hit "file not found" inside the
        // compile temp dir. Generated source emits the directive
        // unconditionally — see TypstExportService.RenderBibliography.
        Dictionary<string, string>? assets = null;
        if (bibEntries.Count > 0)
        {
            assets = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["references.bib"] = BibTeXSerializer.SerializeForTypst(bibEntries),
            };
        }

        var result = await _typstCompiler.CompileAsync(source, TypstOutputFormat.Pdf, assets, ct);
        if (result.Success && result.Output is { Length: > 0 })
        {
            return result.Output;
        }

        EmitFallback(documentId, "typst-compile-failed", result.Error, sample: source);
        return null;
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
