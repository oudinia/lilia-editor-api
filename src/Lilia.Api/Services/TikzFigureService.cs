using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines.TexSafety;

namespace Lilia.Api.Services;

/// <summary>
/// Draws a TikZ figure block as SVG (TikZ figures, step 1, 7 Oct 2026).
///
/// <para>The picture is compiled on its own: a <c>standalone</c> document with the packages
/// and the TikZ setup of its document, through the same safety path as every compile
/// (<see cref="TexSourceGuard"/>, no shell escape, a scrubbed environment, the shared bound
/// on concurrent compiles, a timeout), then the PDF is converted with <c>pdftocairo -svg</c>.</para>
///
/// <para><b>Cache.</b> On disk, by sha256 of the source, the preamble it was compiled with and
/// the engine, so the same picture in two documents with the same setup is drawn once.
/// Compile errors are cached too (they are deterministic); timeouts are not. The cache is
/// capped in size and pruned oldest-first. Separately, the last SVG that drew for each block
/// is kept, so a figure the author breaks can show its last good version.</para>
///
/// <para><b>Budget.</b> A compile (a cache miss) takes one permit of a per-caller window
/// (30 a minute by default, <c>Tikz:CompilesPerMinute</c>): a document full of new figures
/// draws them all; a script hammering the endpoint does not get a TeX process per request.</para>
///
/// <para><b>Validation.</b> Each outcome is recorded as a block validation, so a figure that does
/// not draw is listed among the document's issues on that block (<c>validation-errors</c>). The
/// validator is the engine that compiled it (pdflatex unless the document names another): the
/// table's check constraint allows engines and typst only, and it is the same verdict a per-block
/// pdflatex validation of the figure gives. Timeouts and budget refusals are not the author's
/// fault and are not recorded.</para>
/// </summary>
public interface ITikzFigureService
{
    /// <summary>Draw a TikZ figure block. <paramref name="budgetKey"/> names who pays for a compile (a user id).</summary>
    Task<TikzRenderResult> RenderAsync(Document doc, Block block, string budgetKey, CancellationToken ct = default);

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

    private readonly ILaTeXRenderService _latex;
    private readonly IValidationCacheService? _validation;
    private readonly ILogger<TikzFigureService> _logger;
    private readonly string _cacheDir;
    private readonly long _cacheMaxBytes;
    private readonly int _compilesPerMinute;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<int, PartitionedRateLimiter<string>> Budgets = new();
    private static int _writesSincePrune;

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
    }

    private PartitionedRateLimiter<string> Budget => Budgets.GetOrAdd(_compilesPerMinute, permits =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        })));

    public async Task<TikzRenderResult> RenderAsync(Document doc, Block block, string budgetKey, CancellationToken ct = default)
    {
        var content = block.Content.RootElement;
        var source = TikzFigure.Source(content);
        if (string.IsNullOrWhiteSpace(source))
            return new TikzRenderResult(null, new TikzRenderError("tex", "This TikZ figure has no source.", null, null, null), false);

        var engine = EngineFor(doc);
        var full = BuildStandalone(doc, source, includeCustomPreamble: true, out var sourceStartLine);
        var key = Hash(source, full[..full.IndexOf(@"\begin{document}", StringComparison.Ordinal)], engine);

        var cached = ReadCache(key);
        if (cached is not null)
        {
            await RecordAsync(doc, block, cached, ct);
            if (cached.Ok) KeepLastGood(block.Id, cached.Svg!);
            return cached with { Cached = true };
        }

        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            cached = ReadCache(key);
            if (cached is not null)
            {
                await RecordAsync(doc, block, cached, ct);
                if (cached.Ok) KeepLastGood(block.Id, cached.Svg!);
                return cached with { Cached = true };
            }

            using var lease = Budget.AttemptAcquire(budgetKey);
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
                var minimal = BuildStandalone(doc, source, includeCustomPreamble: false, out var minimalStart);
                var retry = await CompileAsync(minimal, source, minimalStart, engine, ct);
                if (retry.Ok || retry.Error?.Line is not null) result = retry;
            }

            if (result.Error?.Kind != "timeout") WriteCache(key, result);
            await RecordAsync(doc, block, result, ct);
            if (result.Ok) KeepLastGood(block.Id, result.Svg!);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

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
    /// </summary>
    internal static string BuildStandalone(Document doc, string source, bool includeCustomPreamble, out int sourceStartLine)
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

        // TikZ and what this picture needs from it, whether or not the document declared it.
        var needed = TikzFigure.RequiredPackages(source);
        foreach (var pkg in needed) sb.Append(@"\usepackage{").Append(pkg).Append("}\n");
        var custom = doc.CustomPreamble ?? "";
        if (needed.Contains("pgfplots") && !custom.Contains("compat", StringComparison.Ordinal))
            sb.Append(@"\pgfplotsset{compat=1.18}").Append('\n');

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
    /// </summary>
    internal const string RendererVersion = "2";

    internal static string Hash(string source, string preamble, string engine)
    {
        var bytes = Encoding.UTF8.GetBytes(RendererVersion + "\u0000" + source + "\u0000" + preamble + "\u0000" + engine);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private async Task<TikzRenderResult> CompileAsync(string full, string source, int sourceStartLine, string engine, CancellationToken ct)
    {
        try
        {
            var r = await _latex.CompileStandaloneSvgAsync(full, engine, CompileTimeoutSeconds, ct);
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

    /// <summary>Oldest first until the cache (drawings, errors and last-good copies) is under 80% of its cap.</summary>
    internal void Prune()
    {
        try
        {
            if (!Directory.Exists(_cacheDir)) return;
            var files = new DirectoryInfo(_cacheDir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal))
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
