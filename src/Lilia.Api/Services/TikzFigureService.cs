using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines.TexSafety;
using Lilia.Engines.Themes;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Draws a TikZ figure block as SVG (TikZ figures, step 1, 7 Oct 2026; drafts, the precompiled
/// format and theme colour names, step 3).
///
/// <para>The picture is compiled on its own: a <c>standalone</c> document with the packages
/// and the TikZ setup of its document, through the same safety path as every compile
/// (<see cref="TexSourceGuard"/>, no shell escape, a scrubbed environment, the shared bound
/// on concurrent compiles, a timeout), then the PDF is converted with <c>pdftocairo -svg</c>.</para>
///
/// <para><b>Theme colours.</b> A figure naming <c>lilia-ink</c>, <c>lilia-chapter</c> … (its source or
/// the custom preamble) is compiled with its document's theme, colours only, and this figure's
/// chapter colour (<see cref="FigureColours.StandaloneLines"/>), so moving it to another Index
/// chapter recolours it on the next draw. A figure naming none is drawn the same in every theme.</para>
///
/// <para><b>Cache.</b> On disk, by sha256 of the source, the preamble it was compiled with, the
/// engine and, for a figure that names a theme colour, the theme, its paper and the chapter colour,
/// so the same picture in two documents with the same setup is drawn once. Compile errors are cached
/// too (they are deterministic); timeouts are not. Drafts share it: a draft identical to the saved
/// source is a hit. The cache is capped in size and pruned oldest-first. Separately, the last SVG
/// that drew for each block is kept, so a figure the author breaks can show its last good version;
/// only the saved figure (figure.svg) updates it, never a draft.</para>
///
/// <para><b>Speed.</b> With pdflatex, the standalone class, the fonts, amsmath, xcolor, graphicx and
/// the TikZ packages the picture needs (tikz, + pgfplots, + tikz-cd) are loaded once into a
/// precompiled format (mylatexformat, <c>pdflatex -ini</c>), cached on disk by the hash of that
/// preamble; each compile starts from it (<c>-fmt</c>) and reads only the rest. It is built in the
/// background the first time a preamble is seen (that compile runs the normal way) and after a
/// failure the normal compile is used. <c>Tikz:Format=false</c> turns it off.</para>
///
/// <para><b>Budget.</b> A compile (a cache miss) takes one permit of a per-caller window
/// (30 a minute by default, <c>Tikz:CompilesPerMinute</c>): a document full of new figures
/// draws them all; a script hammering the endpoint does not get a TeX process per request.
/// Drafts have their own per-caller token bucket: 30 at once, refilled at 120 a minute
/// (<c>Tikz:DraftBurst</c>, <c>Tikz:DraftsPerMinute</c>). The editor sends one 600 ms after the
/// author stops typing, so sustained typing asks for about one a second at most; the burst
/// covers ⌘S and quick fixes, and a cache hit costs nothing.</para>
///
/// <para><b>Cancellation.</b> A draft is cancelled, and its TeX process killed, when the request is
/// aborted (the editor drops the older request when a newer keystroke draws) and when a newer
/// draft of the same block by the same caller arrives with a different source.</para>
///
/// <para><b>Validation.</b> Each outcome of the saved figure is recorded as a block validation, so a
/// figure that does not draw is listed among the document's issues on that block
/// (<c>validation-errors</c>). The validator is the engine that compiled it (pdflatex unless the
/// document names another): the table's check constraint allows engines and typst only, and it is
/// the same verdict a per-block pdflatex validation of the figure gives. Timeouts and budget
/// refusals are not the author's fault and are not recorded. Drafts record nothing.</para>
/// </summary>
public interface ITikzFigureService
{
    /// <summary>
    /// Draw a TikZ figure block. <paramref name="budgetKey"/> names who pays for a compile (a user id).
    /// <paramref name="theme"/>: the figure's theme (<see cref="TikzFigureThemes"/>); null works it out
    /// from <c>doc.Blocks</c>.
    /// </summary>
    Task<TikzRenderResult> RenderAsync(Document doc, Block block, string budgetKey, CancellationToken ct = default, FigureTheme? theme = null);

    /// <summary>
    /// Draw unsaved source for a TikZ figure block (the split view). Shares the cache; never touches
    /// the block's last good drawing or its validation. Throws <see cref="OperationCanceledException"/>
    /// when <paramref name="ct"/> fires or a newer draft of the block replaces it.
    /// </summary>
    Task<TikzRenderResult> DraftAsync(Document doc, Block block, string source, string budgetKey, FigureTheme? theme = null, CancellationToken ct = default);

    /// <summary>The last SVG this block drew, or null.</summary>
    byte[]? LastGood(Guid blockId);

    bool HasLastGood(Guid blockId);
}

public sealed record TikzRenderResult(byte[]? Svg, TikzRenderError? Error, bool Cached)
{
    public bool Ok => Svg is not null;
}

/// <summary>
/// Why a figure did not draw. <c>Kind</c>: <c>"tex"</c> (the source: the author can fix it),
/// <c>"timeout"</c> or <c>"budget"</c> (not the author's fault: try again). <c>Line</c> is 1-based
/// in the figure's own source, <c>Excerpt</c> that line's text, <c>Column</c> the bad part of it
/// when TeX says where (0-based, end exclusive, as
/// the editor slices it).
/// </summary>
public sealed record TikzRenderError(string Kind, string Message, int? Line, string? Excerpt, TikzErrorColumn? Column);

public sealed record TikzErrorColumn(int Start, int End);

public sealed class TikzFigureService : ITikzFigureService
{
    public const int CompileTimeoutSeconds = 20;

    /// <summary>A draft longer than this is refused (413) before any work: no figure is that long.</summary>
    public const int MaxDraftChars = 200_000;

    /// <summary>The line that ends the part of the standalone preamble kept in the precompiled format.</summary>
    internal const string EndOfDump = @"\csname endofdump\endcsname";

    private readonly ILaTeXRenderService _latex;
    private readonly IValidationCacheService? _validation;
    private readonly ILogger<TikzFigureService> _logger;
    private readonly string _cacheDir;
    private readonly long _cacheMaxBytes;
    private readonly int _compilesPerMinute;
    private readonly int _draftBurst;
    private readonly int _draftsPerMinute;
    private readonly bool _useFormat;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<int, PartitionedRateLimiter<string>> Budgets = new();
    private static readonly ConcurrentDictionary<(int, int), PartitionedRateLimiter<string>> DraftBudgets = new();
    private static readonly ConcurrentDictionary<string, DraftSlot> Drafts = new();
    private static readonly ConcurrentDictionary<string, Task<bool>> FormatBuilds = new();
    private static readonly ConcurrentDictionary<string, DateTime> FormatFailures = new();
    private static int _writesSincePrune;

    /// <summary>After a failed build or a rejected format, the normal compile is used this long before trying again.</summary>
    private static readonly TimeSpan FormatRetryAfter = TimeSpan.FromMinutes(10);

    private sealed record DraftSlot(CancellationTokenSource Cts, string Key);

    public TikzFigureService(
        ILaTeXRenderService latex,
        IConfiguration configuration,
        ILogger<TikzFigureService> logger,
        IValidationCacheService? validation = null)
    {
        _latex = latex;
        _validation = validation;
        _logger = logger;
        _cacheDir = configuration["Tikz:CacheDir"] is { Length: > 0 } dir ? dir : Path.Combine(Path.GetTempPath(), "lilia-tikz-cache");
        _cacheMaxBytes = (configuration.GetValue<long?>("Tikz:CacheMaxMb") ?? 256) * 1024 * 1024;
        _compilesPerMinute = Math.Max(1, configuration.GetValue<int?>("Tikz:CompilesPerMinute") ?? 30);
        _draftBurst = Math.Max(1, configuration.GetValue<int?>("Tikz:DraftBurst") ?? 30);
        _draftsPerMinute = Math.Max(1, configuration.GetValue<int?>("Tikz:DraftsPerMinute") ?? 120);
        _useFormat = configuration.GetValue<bool?>("Tikz:Format") ?? true;
    }

    private PartitionedRateLimiter<string> Budget => Budgets.GetOrAdd(_compilesPerMinute, permits =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        })));

    private PartitionedRateLimiter<string> DraftBudget => DraftBudgets.GetOrAdd((_draftBurst, _draftsPerMinute), b =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = b.Item1,
            // Refilled every 5 s (a rate under 12 a minute: one token at a time).
            ReplenishmentPeriod = b.Item2 >= 12 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(60.0 / b.Item2),
            TokensPerPeriod = b.Item2 >= 12 ? b.Item2 / 12 : 1,
            AutoReplenishment = true,
            QueueLimit = 0,
        })));

    public async Task<TikzRenderResult> RenderAsync(Document doc, Block block, string budgetKey, CancellationToken ct = default, FigureTheme? theme = null)
    {
        var source = TikzFigure.Source(block.Content.RootElement);
        if (string.IsNullOrWhiteSpace(source)) return NoSource;
        theme ??= TikzFigureThemes.From(doc, doc.Blocks?.OrderBy(b => b.SortOrder)).For(block.Id);

        var result = await DrawAsync(doc, source, theme, budgetKey, Budget, ct);
        if (result.Error is { Kind: not "tex" }) return result;
        await RecordAsync(doc, block, result, ct);
        if (result.Ok) KeepLastGood(block.Id, result.Svg!);
        return result;
    }

    public async Task<TikzRenderResult> DraftAsync(Document doc, Block block, string source, string budgetKey, FigureTheme? theme = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return NoSource;
        theme ??= TikzFigureThemes.From(doc, doc.Blocks?.OrderBy(b => b.SortOrder)).For(block.Id);

        // A newer draft of the same block by the same caller replaces this one: the older compile
        // is killed, unless it is drawing the very same thing (then the newer one waits for it and
        // reads the cache).
        var (_, _, key, _) = Prepare(doc, source, theme, includeCustomPreamble: true);
        var slotKey = budgetKey + "\u0000" + block.Id.ToString("N");
        using var mine = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var slot = new DraftSlot(mine, key);
        Drafts.AddOrUpdate(slotKey, slot, (_, older) =>
        {
            if (older.Key != key)
            {
                try { older.Cts.Cancel(); } catch (ObjectDisposedException) { /* it has finished */ }
            }
            return slot;
        });
        try
        {
            return await DrawAsync(doc, source, theme, budgetKey, DraftBudget, mine.Token);
        }
        finally
        {
            Drafts.TryRemove(KeyValuePair.Create(slotKey, slot));
        }
    }

    private static readonly TikzRenderResult NoSource =
        new(null, new TikzRenderError("tex", "This TikZ figure has no source.", null, null, null), false);

    /// <summary>The drawing of this source in this document and theme: from the cache, or compiled.</summary>
    private async Task<TikzRenderResult> DrawAsync(Document doc, string source, FigureTheme theme, string budgetKey,
        PartitionedRateLimiter<string> budget, CancellationToken ct)
    {
        var (full, sourceStartLine, key, engine) = Prepare(doc, source, theme, includeCustomPreamble: true);

        var cached = ReadCache(key);
        if (cached is not null) return cached with { Cached = true };

        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            cached = ReadCache(key);
            if (cached is not null) return cached with { Cached = true };

            using var lease = budget.AttemptAcquire(budgetKey);
            if (!lease.IsAcquired)
                return new TikzRenderResult(null, new TikzRenderError("budget",
                    "Too many figures are being drawn right now. This isn't a problem with the figure: try again in a minute.",
                    null, null, null), false);

            var result = await CompileAsync(full, source, sourceStartLine, engine, ct);
            // A document's own preamble can break a picture that is fine (a package the picture
            // does not need, a macro needing a class option): when the error is in the preamble,
            // draw it again with only the TikZ setup.
            if (result.Error is { Kind: "tex", Line: null } && !string.IsNullOrWhiteSpace(doc.CustomPreamble))
            {
                var (minimal, minimalStart, _, _) = Prepare(doc, source, theme, includeCustomPreamble: false);
                var retry = await CompileAsync(minimal, source, minimalStart, engine, ct);
                if (retry.Ok || retry.Error?.Line is not null) result = retry;
            }

            if (result.Error?.Kind != "timeout") WriteCache(key, result);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The standalone document, where the source starts in it, the cache key and the engine.</summary>
    private static (string Full, int SourceStartLine, string Key, string Engine) Prepare(
        Document doc, string source, FigureTheme theme, bool includeCustomPreamble)
    {
        var engine = EngineFor(doc);
        var themed = NamesThemeColours(doc, source);
        var full = BuildStandalone(doc, source, includeCustomPreamble, out var start, themed ? theme : null);
        // The key is always the full document's, so the minimal retry's result is found under it.
        var keyed = includeCustomPreamble ? full : BuildStandalone(doc, source, true, out _, themed ? theme : null);
        var key = Hash(source, keyed[..keyed.IndexOf(@"\begin{document}", StringComparison.Ordinal)], engine, themed ? theme.Key : "");
        return (full, start, key, engine);
    }

    /// <summary>The figure (or the TikZ setup it is compiled with) names a theme colour.</summary>
    internal static bool NamesThemeColours(Document doc, string source) =>
        FigureColours.Uses(source) || FigureColours.Uses(doc.CustomPreamble);

    public byte[]? LastGood(Guid blockId)
    {
        var path = LastGoodPath(blockId);
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
    }

    public bool HasLastGood(Guid blockId) => File.Exists(LastGoodPath(blockId));

    // ── The standalone document ─────────────────────────────────────────

    // Packages a standalone picture must not load, or need not: page layout, floats, references,
    // bibliography, and what the standalone preamble below loads already.
    private static readonly HashSet<string> NotForStandalone = new(StringComparer.OrdinalIgnoreCase)
    {
        "geometry", "fancyhdr", "hyperref", "cleveref", "titlesec", "titletoc", "tocloft", "titling", "setspace",
        "multicol", "parskip", "placeins", "biblatex", "natbib", "lineno", "microtype", "appendix", "abstract",
        "float", "afterpage", "pdfpages", "lastpage", "background", "draftwatermark", "eso-pic", "everypage",
        "lipsum", "blindtext", "tikz", "pgfplots", "tikz-cd", "xcolor", "color", "inputenc", "fontenc", "lmodern",
        "amsmath", "amssymb", "amsfonts", "graphicx", "graphics", "standalone", "varwidth", "caption", "subcaption",
        "subfig", "wrapfig", "sidecap", "authblk", "fancyvrb", "listings", "minted", "lilia-theme",
    };

    // Engine-specific packages: they only load on the engine that has them.
    private static readonly HashSet<string> UnicodeEngineOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "fontspec", "unicode-math", "polyglossia", "xeCJK", "luacode", "luatexja", "xltxtra", "xunicode",
    };

    /// <summary>
    /// The standalone document for one picture. <paramref name="sourceStartLine"/> is the line
    /// the source starts on, so a TeX line number maps back into the figure's own source.
    ///
    /// <para>Everything before the <see cref="EndOfDump"/> line (the class, the fonts, the maths,
    /// the colours and the TikZ packages the picture needs) is the same for every picture needing
    /// the same packages: it is what the precompiled format holds. After it: the document's own
    /// packages, its TikZ setup and macros, and the theme colours when the picture names them
    /// (<paramref name="theme"/>).</para>
    /// </summary>
    internal static string BuildStandalone(Document doc, string source, bool includeCustomPreamble, out int sourceStartLine, FigureTheme? theme = null)
    {
        var engine = EngineFor(doc);
        var sb = new StringBuilder();
        // The document loads xcolor with these options, so a colour that works there works here.
        sb.Append(@"\PassOptionsToPackage{dvipsnames,svgnames,table}{xcolor}").Append('\n');
        // varwidth: the picture, whatever wraps it (display math around a tikzcd, a \resizebox,
        // two pictures side by side), is one box cropped to its size: one page, one SVG.
        sb.Append(@"\documentclass[border=2pt,varwidth]{standalone}").Append('\n');
        if (engine == "pdflatex")
            sb.Append(@"\usepackage[utf8]{inputenc}").Append('\n').Append(@"\usepackage[T1]{fontenc}").Append('\n');
        sb.Append(@"\usepackage{lmodern}").Append('\n');
        sb.Append(@"\usepackage{amsmath,amssymb,amsfonts}").Append('\n');
        sb.Append(@"\usepackage{xcolor}").Append('\n');
        sb.Append(@"\usepackage{graphicx}").Append('\n');

        // TikZ and what this picture needs from it, whether or not the document declared it.
        var needed = TikzFigure.RequiredPackages(source);
        foreach (var pkg in needed) sb.Append(@"\usepackage{").Append(pkg).Append("}\n");
        sb.Append(EndOfDump).Append('\n');

        // The document's own packages (a node may use \si, \bm, a font): those a picture can use.
        var declared = new List<(string Name, string? Options)>();
        if (!string.IsNullOrWhiteSpace(doc.LatexPackages))
        {
            try
            {
                using var json = JsonDocument.Parse(doc.LatexPackages);
                if (json.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var pkg in json.RootElement.EnumerateArray())
                        if (pkg.ValueKind == JsonValueKind.Object && pkg.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                            declared.Add((name.Trim(), pkg.TryGetProperty("options", out var o) ? o.GetString() : null));
            }
            catch (JsonException) { /* unreadable: none */ }
        }
        foreach (var (name, options) in declared)
        {
            if (NotForStandalone.Contains(name)) continue;
            if (engine == "pdflatex" && UnicodeEngineOnly.Contains(name)) continue;
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9\-_.]+$")) continue;
            var load = string.IsNullOrWhiteSpace(options) ? $@"\usepackage{{{name}}}" : $@"\usepackage[{options}]{{{name}}}";
            sb.Append($@"\IfFileExists{{{name}.sty}}{{{load}}}{{}}").Append('\n');
        }

        var custom = doc.CustomPreamble ?? "";
        if (needed.Contains("pgfplots") && !custom.Contains("compat", StringComparison.Ordinal))
            sb.Append(@"\pgfplotsset{compat=1.18}").Append('\n');

        // The theme's colour names, and this figure's chapter colour: before the custom preamble,
        // as the document's theme line is, so a \tikzset there can use them.
        if (theme is not null) sb.Append(FigureColours.StandaloneLines(theme));

        // The document's custom preamble (its macros and its TikZ setup), or, when that does not
        // compile here, its TikZ setup alone.
        var preambleLines = includeCustomPreamble ? custom.Trim() : TikzFigure.SetupLines(custom);
        if (preambleLines.Length > 0) sb.Append(preambleLines).Append('\n');

        sb.Append(@"\setlength{\parindent}{0pt}").Append('\n');
        sb.Append(@"\begin{document}").Append('\n');
        sourceStartLine = sb.ToString().Count(c => c == '\n') + 1;
        sb.Append(source).Append('\n');
        sb.Append(@"\end{document}").Append('\n');
        return sb.ToString();
    }

    private static string EngineFor(Document doc) => (doc.LatexEngine ?? "").Trim().ToLowerInvariant() switch
    {
        "xelatex" => "xelatex",
        "lualatex" => "lualatex",
        _ => "pdflatex",
    };

    /// <summary>
    /// Bumped whenever how a drawing or its error is produced changes (the standalone wrapper, the SVG
    /// conversion, the error wording), so a deploy never serves results the old code made from the cache.
    /// 3: theme colour names, the precompiled format's preamble order.
    /// </summary>
    internal const string RendererVersion = "3";

    /// <summary>
    /// The cache key. <paramref name="themeKey"/> (<see cref="FigureTheme.Key"/>: theme, paper, chapter
    /// colour) is given for a figure that names a theme colour, and empty otherwise.
    /// </summary>
    internal static string Hash(string source, string preamble, string engine, string themeKey = "")
    {
        var bytes = Encoding.UTF8.GetBytes(RendererVersion + "\u0000" + source + "\u0000" + preamble + "\u0000" + engine + "\u0000" + themeKey);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private async Task<TikzRenderResult> CompileAsync(string full, string source, int sourceStartLine, string engine, CancellationToken ct)
    {
        try
        {
            var format = FormatFor(full, engine);
            var r = await _latex.CompileStandaloneSvgAsync(full, engine, CompileTimeoutSeconds, ct, format);
            if (r.FormatRejected && format is not null) Reject(format);
            if (r.TimedOut)
                return new TikzRenderResult(null, new TikzRenderError("timeout",
                    $"Drawing this figure took longer than {CompileTimeoutSeconds} seconds and was stopped. This isn't necessarily a problem with the figure: try again.",
                    null, null, null), false);
            if (r.Svg is { Length: > 0 } svg) return new TikzRenderResult(svg, null, false);
            return new TikzRenderResult(null, TikzErrors.FromLog(r.Log, source, sourceStartLine), false);
        }
        catch (UnsafeLatexException ex)
        {
            return new TikzRenderResult(null, TikzErrors.FromGuard(ex.Message, source), false);
        }
    }

    // ── The precompiled format ──────────────────────────────────────────

    /// <summary>The part of a standalone document the precompiled format holds.</summary>
    internal static string DumpPart(string full)
    {
        var at = full.IndexOf(EndOfDump, StringComparison.Ordinal);
        return at < 0 ? "" : full[..at];
    }

    internal string FormatPath(string dumpPart, string engine) =>
        Path.Combine(_cacheDir, "fmt", "tikz-" + Hash("format", dumpPart, engine)[..24]);

    /// <summary>
    /// The format to compile this document with (a path without <c>.fmt</c>), or null: the
    /// normal compile. A format not built yet is built in the background, once.
    /// </summary>
    internal string? FormatFor(string full, string engine)
    {
        if (!_useFormat || engine != "pdflatex") return null;
        var dump = DumpPart(full);
        if (dump.Length == 0) return null;
        var path = FormatPath(dump, engine);
        if (File.Exists(path + ".fmt")) return path;
        if (FormatFailures.TryGetValue(path, out var failedAt) && DateTime.UtcNow - failedAt < FormatRetryAfter) return null;

        FormatBuilds.GetOrAdd(path, p => Task.Run(async () =>
        {
            try
            {
                var ok = await _latex.BuildFormatAsync(dump, engine, p);
                if (!ok) FormatFailures[p] = DateTime.UtcNow;
                else FormatFailures.TryRemove(p, out _);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Tikz] building the precompiled format failed");
                FormatFailures[p] = DateTime.UtcNow;
                return false;
            }
            finally
            {
                FormatBuilds.TryRemove(p, out _);
            }
        }));
        return null;
    }

    /// <summary>Wait for the format of this document to be built (tests and warm-up). True when it is there.</summary>
    internal async Task<bool> EnsureFormatAsync(string full, string engine)
    {
        if (FormatFor(full, engine) is not null) return true;
        var path = FormatPath(DumpPart(full), engine);
        if (FormatBuilds.TryGetValue(path, out var build)) await build;
        return File.Exists(path + ".fmt");
    }

    /// <summary>The engine refused this format: drop it, compile the normal way for a while, then build it again.</summary>
    private void Reject(string format)
    {
        _logger.LogWarning("[Tikz] the precompiled format {Format} was rejected; drawing without it", Path.GetFileName(format));
        FormatFailures[format] = DateTime.UtcNow;
        try { File.Delete(format + ".fmt"); } catch { /* in use or gone */ }
    }

    // ── Validation ──────────────────────────────────────────────────────

    private async Task RecordAsync(Document doc, Block block, TikzRenderResult result, CancellationToken ct)
    {
        if (_validation is null || result.Error is { Kind: not "tex" }) return;
        try
        {
            var hash = _validation.ComputeHash(block);
            var validator = EngineFor(doc);
            if (await _validation.GetAsync(block.Id, hash, validator, ct) is not null) return;
            await _validation.InvalidateOlderThanAsync(block.Id, hash, ValidationCacheService.RuleVersion, ct);
            await _validation.PersistAsync(new BlockValidation
            {
                BlockId = block.Id,
                DocumentId = block.DocumentId,
                ContentHash = hash,
                Status = result.Ok ? "valid" : "error",
                ErrorMessage = result.Error is { } e ? TikzErrors.Describe(e) : null,
                Validator = validator,
                RuleVersion = ValidationCacheService.RuleVersion,
                ValidatedAt = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            // The drawing is what was asked for; the record is best effort.
            _logger.LogWarning(ex, "[Tikz] could not record the validation of block {BlockId}", block.Id);
        }
    }

    // ── Disk cache ──────────────────────────────────────────────────────

    private string CachePath(string key, string ext) => Path.Combine(_cacheDir, key[..2], key + ext);
    private string LastGoodPath(Guid blockId) => Path.Combine(_cacheDir, "last-good", blockId.ToString("N") + ".svg");

    private TikzRenderResult? ReadCache(string key)
    {
        try
        {
            var svg = CachePath(key, ".svg");
            if (File.Exists(svg))
            {
                Touch(svg);
                return new TikzRenderResult(File.ReadAllBytes(svg), null, true);
            }
            var err = CachePath(key, ".err.json");
            if (File.Exists(err))
            {
                Touch(err);
                var e = JsonSerializer.Deserialize<TikzRenderError>(File.ReadAllText(err));
                if (e is not null) return new TikzRenderResult(null, e, true);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "[Tikz] cache read failed for {Key}", key);
        }
        return null;
    }

    private void WriteCache(string key, TikzRenderResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_cacheDir, key[..2]));
            if (result.Svg is { } svg) WriteAtomically(CachePath(key, ".svg"), svg);
            else if (result.Error is { Kind: "tex" } e) WriteAtomically(CachePath(key, ".err.json"), JsonSerializer.SerializeToUtf8Bytes(e));
            if (Interlocked.Increment(ref _writesSincePrune) >= 50)
            {
                Interlocked.Exchange(ref _writesSincePrune, 0);
                Prune();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[Tikz] cache write failed for {Key}", key);
        }
    }

    private void KeepLastGood(Guid blockId, byte[] svg)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_cacheDir, "last-good"));
            WriteAtomically(LastGoodPath(blockId), svg);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "[Tikz] could not keep the last good drawing of {BlockId}", blockId);
        }
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    private static void Touch(string path)
    {
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { /* read-only cache: fine */ }
    }

    /// <summary>
    /// Oldest first until the cache (drawings, errors and last-good copies) is under 80% of its cap.
    /// The precompiled formats (a handful, one per set of TikZ packages) are not counted or pruned.
    /// </summary>
    internal void Prune()
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return;
            var formats = Path.Combine(_cacheDir, "fmt") + Path.DirectorySeparatorChar;
            var files = new DirectoryInfo(_cacheDir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal))
                .Where(f => !f.FullName.StartsWith(formats, StringComparison.Ordinal))
                .ToList();
            var total = files.Sum(f => f.Length);
            if (total <= _cacheMaxBytes) return;
            var target = (long)(_cacheMaxBytes * 0.8);
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= target) break;
                try { total -= f.Length; f.Delete(); } catch { /* in use: next */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Tikz] cache prune failed");
        }
    }
}

/// <summary>
/// Each TikZ figure's theme in its document (<see cref="FigureTheme"/>): the theme as printed, its
/// paper, and the colour <c>lilia-chapter</c> takes. Only an Index document needs the blocks (the
/// figure's chapter, worked out as the PDF does: <see cref="ThemeSections"/>).
/// </summary>
public sealed class TikzFigureThemes
{
    private readonly FigureTheme _default;
    private readonly IReadOnlyDictionary<Guid, SectionPlace>? _places;
    private readonly Document _doc;

    private TikzFigureThemes(Document doc, IReadOnlyDictionary<Guid, SectionPlace>? places)
    {
        _doc = doc;
        _places = places;
        _default = FigureColours.For(doc, null);
    }

    public FigureTheme For(Guid blockId) =>
        _places is not null && _places.TryGetValue(blockId, out var place) ? FigureColours.For(_doc, place) : _default;

    /// <summary>Whether a figure's chapter changes its colours: Index, on a class that prints it, not a deck.</summary>
    public static bool TracksChapters(Document doc)
    {
        if (ThemeLock.Reason(doc.LatexDocumentClass) is not null || ThemeLock.IsBeamer(doc.LatexDocumentClass)) return false;
        return DocumentLook.Parse(doc.Look).ForClass(doc.LatexDocumentClass).Theme == ThemeCatalog.Index;
    }

    /// <summary>From blocks already loaded, in order (null or empty: no chapters known).</summary>
    public static TikzFigureThemes From(Document doc, IEnumerable<Block>? orderedBlocks)
    {
        if (!TracksChapters(doc) || orderedBlocks is null) return new(doc, null);
        var body = LaTeXExportService.BodyBlocks(doc.Title, orderedBlocks);
        return new(doc, body.Count == 0 ? null : ThemeSections.Places(body, doc.LatexDocumentClass, doc.Look));
    }

    /// <summary>
    /// From the database: for an Index document, the outline and the contents that decide the
    /// chapters (headings, embeds, the Appendix block).
    /// </summary>
    public static async Task<TikzFigureThemes> LoadAsync(LiliaDbContext db, Document doc, CancellationToken ct)
    {
        if (!TracksChapters(doc)) return new(doc, null);
        var outline = await db.Blocks.AsNoTracking()
            .Where(b => b.DocumentId == doc.Id)
            .Select(b => new { b.Id, b.DocumentId, b.Type, b.SortOrder })
            .ToListAsync(ct);
        var contents = await db.Blocks.AsNoTracking()
            .Where(b => b.DocumentId == doc.Id
                        && (b.Type == "heading" || b.Type == "header" || b.Type == "embed" || b.Type == "backMatter"))
            .Select(b => new { b.Id, b.Content })
            .ToDictionaryAsync(b => b.Id, b => b.Content, ct);
        var blocks = outline.OrderBy(b => b.SortOrder).Select(b => new Block
        {
            Id = b.Id, DocumentId = b.DocumentId, Type = b.Type, SortOrder = b.SortOrder,
            Content = contents.TryGetValue(b.Id, out var c) ? c : JsonDocument.Parse("{}"),
        });
        return From(doc, blocks);
    }
}

/// <summary>A TeX log, or a guard refusal, as a <see cref="TikzRenderError"/> on the figure's own source.</summary>
public static class TikzErrors
{
    private static readonly Regex LineRef = new(@"^l\.(\d+) ?(.*)$", RegexOptions.Compiled);
    private static readonly Regex ControlSequence = new(@"\\[A-Za-z@]+\*?|\\.", RegexOptions.Compiled);

    public static string Describe(TikzRenderError e) =>
        e.Line is { } line ? $"This TikZ figure doesn't draw (line {line}): {e.Message}" : $"This TikZ figure doesn't draw: {e.Message}";

    public static TikzRenderError FromLog(string? log, string source, int sourceStartLine)
    {
        var lines = (log ?? "").Replace("\r\n", "\n").Split('\n');
        var bang = Array.FindIndex(lines, l => l.StartsWith("! ", StringComparison.Ordinal));
        if (bang < 0)
            return new TikzRenderError("tex", "TeX stopped without drawing the figure.", null, null, null);

        // The message: the "!" line and its continuations (the log wraps at 79 characters).
        var message = new StringBuilder(lines[bang][2..].TrimEnd());
        var prevLength = lines[bang].Length;
        for (var k = bang + 1; k < lines.Length && k < bang + 6; k++)
        {
            var l = lines[k];
            if (l.Length == 0 || l.StartsWith("l.", StringComparison.Ordinal) || l.StartsWith("See the ", StringComparison.Ordinal)
                || l.StartsWith("Type ", StringComparison.Ordinal) || l.StartsWith("<", StringComparison.Ordinal)
                || l.StartsWith("! ", StringComparison.Ordinal) || l.StartsWith("(", StringComparison.Ordinal))
                break;
            message.Append(prevLength >= 79 ? "" : " ").Append(l.TrimEnd());
            prevLength = l.Length;
        }

        // Where: the l.N line after it, and the text TeX had read on that line.
        int? line = null;
        string? consumed = null;
        for (var k = bang + 1; k < lines.Length && k < bang + 40; k++)
        {
            var m = LineRef.Match(lines[k]);
            if (!m.Success) continue;
            var n = int.Parse(m.Groups[1].Value) - sourceStartLine + 1;
            var sourceLines = source.Replace("\r\n", "\n").Split('\n');
            if (n >= 1 && n <= sourceLines.Length) { line = n; consumed = m.Groups[2].Value; }
            else if (n > sourceLines.Length) line = sourceLines.Length;   // ran off the end: an unclosed environment
            break;
        }

        var text = Clean(message.ToString());
        if (text.StartsWith("Undefined control sequence", StringComparison.Ordinal) && consumed is not null
            && ControlSequence.Matches(consumed) is { Count: > 0 } cs)
            text = $"Undefined control sequence {cs[^1].Value}.";

        // TeX often notices a missing ")" or "}" only at the blank line or the end that follows it
        // ("Paragraph ended before … was complete", "Runaway argument"): the line it names is empty, and
        // the author's mistake is on the last line with content before it. Point there, without a column.
        if (text.StartsWith("Paragraph ended before", StringComparison.Ordinal)
            || text.StartsWith("Runaway argument", StringComparison.Ordinal)
            || text.StartsWith("File ended while scanning", StringComparison.Ordinal))
            text = "Something isn't closed: a ')' or '}' is probably missing on this line.";

        string? excerpt = null;
        TikzErrorColumn? column = null;
        if (line is { } ln)
        {
            var sourceLines = source.Replace("\r\n", "\n").Split('\n');
            var at = ln;
            while (at > 1 && string.IsNullOrWhiteSpace(sourceLines[at - 1])) at--;
            if (at != ln) { line = at; consumed = null; }
            excerpt = sourceLines[line!.Value - 1];
            if (consumed is not null) column = ColumnOf(excerpt, consumed);
        }
        return new TikzRenderError("tex", text, line, excerpt, column);
    }

    /// <summary>
    /// The part of the line TeX choked on. TeX prints the line up to the error point (with
    /// "..." when it cut the start): the bad part ends there and is its last control sequence,
    /// or else its last word.
    /// </summary>
    private static TikzErrorColumn? ColumnOf(string excerpt, string consumed)
    {
        var read = consumed.Replace("^^I", "\t");
        if (read.StartsWith("...", StringComparison.Ordinal)) read = read[3..];
        if (read.Trim().Length == 0) return null;
        var at = excerpt.IndexOf(read, StringComparison.Ordinal);
        if (at < 0) return null;
        var end = at + read.Length;          // exclusive, 0-based
        var trimmedEnd = end;
        while (trimmedEnd > at && char.IsWhiteSpace(excerpt[trimmedEnd - 1])) trimmedEnd--;
        if (trimmedEnd == at) return null;
        var cs = ControlSequence.Matches(excerpt[at..trimmedEnd]);
        int start;
        if (cs.Count > 0 && at + cs[^1].Index + cs[^1].Length >= trimmedEnd - 1)
            start = at + cs[^1].Index;
        else
        {
            start = trimmedEnd - 1;
            while (start > at && !char.IsWhiteSpace(excerpt[start - 1])) start--;
        }
        return new TikzErrorColumn(start, trimmedEnd);
    }

    /// <summary>"Package pgfkeys Error: I do not know the key …" → "I do not know the key …", and so on.</summary>
    private static string Clean(string message)
    {
        var m = Regex.Replace(message, @"^(?:Package \S+ Error|LaTeX Error|Class \S+ Error):\s*", "");
        m = Regex.Replace(m, @"\s+", " ").Trim();
        m = m.Replace(" and I am going to ignore it", "", StringComparison.Ordinal);
        if (m.Length > 300) m = m[..300] + "…";
        return m;
    }

    public static TikzRenderError FromGuard(string message, string source)
    {
        // The guard names no line: ask it about each line of the source in turn.
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (TexSourceGuard.Violation(lines[i]) is not null)
                return new TikzRenderError("tex", message, i + 1, lines[i], null);
        return new TikzRenderError("tex", message, null, null, null);
    }
}
