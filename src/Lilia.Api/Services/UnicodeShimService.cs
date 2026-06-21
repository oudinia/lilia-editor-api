using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Result of scanning a LaTeX source against the Unicode→LaTeX catalog.
/// <see cref="Shim"/> is the <c>\newunicodechar</c> preamble block to inject
/// (empty when nothing needs mapping); <see cref="UnmappedCodepoints"/> are
/// non-ASCII characters present in the source with no catalog row — surfaced
/// as telemetry so coverage gaps are visible.
/// </summary>
public readonly record struct UnicodeShimResult(string Shim, IReadOnlyList<int> UnmappedCodepoints)
{
    public bool HasShim => Shim.Length > 0;
    public bool HasUnmapped => UnmappedCodepoints.Count > 0;
}

public interface IUnicodeShimService
{
    Task PreloadAsync();

    /// <summary>
    /// Scan an assembled LaTeX source for non-ASCII characters and build the
    /// <c>\newunicodechar</c> shim for the ones the catalog knows. Pure given
    /// the loaded map.
    /// </summary>
    UnicodeShimResult BuildShim(string source);

    /// <summary>
    /// Insert a shim block into a full LaTeX source immediately before
    /// <c>\begin{document}</c> (i.e. at the end of the preamble).
    /// </summary>
    string Inject(string source, string shim);

    int MappedCount { get; }
}

/// <summary>
/// Loads <c>latex_unicode_map</c> into an in-memory dictionary at startup
/// (mirroring <see cref="LatexCatalogService"/>) and builds per-document
/// <c>\newunicodechar</c> shims so literal Unicode in prose (γ, ×, —, …)
/// compiles under pdflatex instead of aborting with "Unicode character not
/// set up for use with LaTeX". DB is authoritative; the map can grow and
/// unmapped characters surface via telemetry.
/// </summary>
public class UnicodeShimService : IUnicodeShimService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnicodeShimService> _logger;

    // codepoint → LaTeX replacement
    private readonly ConcurrentDictionary<int, string> _map = new();
    private volatile bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public UnicodeShimService(IServiceScopeFactory scopeFactory, ILogger<UnicodeShimService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public int MappedCount => _map.Count;

    public async Task PreloadAsync() => await EnsureLoadedAsync(CancellationToken.None);

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_loaded) return;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
            var rows = await db.LatexUnicodeChars.AsNoTracking()
                .Select(u => new { u.Codepoint, u.Replacement })
                .ToListAsync(ct);
            foreach (var r in rows)
                _map[r.Codepoint] = r.Replacement;
            _loaded = true;
            _logger.LogInformation("Unicode shim map loaded: {Count} characters", _map.Count);
        }
        catch (Exception ex)
        {
            // Never let a catalog hiccup take down validation — degrade to
            // "no shim" (validation behaves exactly as before this feature).
            _logger.LogWarning(ex, "Unicode shim map failed to load; shimming disabled this process");
            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public UnicodeShimResult BuildShim(string source)
    {
        if (string.IsNullOrEmpty(source) || _map.IsEmpty)
            return new UnicodeShimResult(string.Empty, Array.Empty<int>());

        // Collect the distinct non-ASCII codepoints actually present, so the
        // shim only carries what the document uses (adaptive, lean preamble).
        var present = new HashSet<int>();
        var i = 0;
        while (i < source.Length)
        {
            int cp = char.ConvertToUtf32(source, i);
            i += char.IsSurrogatePair(source, i) ? 2 : 1;
            if (cp > 0x7F) present.Add(cp);
        }
        if (present.Count == 0)
            return new UnicodeShimResult(string.Empty, Array.Empty<int>());

        var sb = new StringBuilder();
        List<int>? unmapped = null;
        // Deterministic ordering keeps the assembled source stable.
        foreach (var cp in present.OrderBy(c => c))
        {
            if (_map.TryGetValue(cp, out var repl))
            {
                sb.Append(@"\newunicodechar{")
                  .Append(char.ConvertFromUtf32(cp))
                  .Append("}{").Append(repl).Append("}\n");
            }
            else if (!IsInputencSafe(cp))
            {
                (unmapped ??= new List<int>()).Add(cp);
            }
        }

        if (sb.Length == 0)
            return new UnicodeShimResult(string.Empty, (IReadOnlyList<int>?)unmapped ?? Array.Empty<int>());

        // textcomp covers the \text… typographic replacements; newunicodechar
        // provides the mapping mechanism itself.
        var shim = "\\usepackage{textcomp}\n\\usepackage{newunicodechar}\n" + sb;
        return new UnicodeShimResult(shim, (IReadOnlyList<int>?)unmapped ?? Array.Empty<int>());
    }

    public string Inject(string source, string shim)
    {
        if (string.IsNullOrEmpty(shim)) return source;
        const string marker = "\\begin{document}";
        var idx = source.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return shim + source; // defensive: no body marker
        return source[..idx] + shim + source[idx..];
    }

    // Latin-1 letters/punctuation that inputenc[utf8] already handles (accented
    // Latin: é, ñ, ü, …). Don't flag these as gaps — they compile fine.
    private static bool IsInputencSafe(int cp)
    {
        if (cp is >= 0x00A0 and <= 0x024F) return true;     // Latin-1 suppl. + Latin Extended-A/B
        var cat = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
        return cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format;
    }
}
