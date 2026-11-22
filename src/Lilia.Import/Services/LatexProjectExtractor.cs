using Lilia.Core.Security;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// Flattens an Overleaf-style .tex project zip into a single resolved
/// LaTeX string ready for the existing LatexParser.
///
/// Handles:
///   - Finding the main file (has \documentclass + \begin{document}).
///   - Inlining \input{x} and \include{x} recursively (with .tex resolution).
///   - Emitting diagnostics for .sty / .cls / .bib / images we haven't
///     staged yet — v1 users get notified what was dropped so they can
///     re-add manually.
///
/// Out of scope for v1:
///   - Image asset staging to R2 (diagnostic only).
///   - Bibliography asset pickup (diagnostic only).
///   - Multi-level project sessions per valiant-waddling-otter plan.
/// </summary>
public interface ILatexProjectExtractor
{
    LatexProjectResult Extract(byte[] zipBytes);
}

public record LatexProjectResult(
    string InlinedTex,
    string MainFileName,
    IReadOnlyList<LatexProjectFile> Files,
    IReadOnlyList<string> UnresolvedIncludes,
    IReadOnlyList<string> Notices)
{
    /// <summary>Every file that could be the main one (\documentclass and \begin{document}), the chosen one first.</summary>
    public IReadOnlyList<string> MainCandidates { get; init; } = [];

    /// <summary>
    /// The <see cref="Notices"/> that only say what the zip holds ("Found 6 image file(s): …", the .bib, the listings, the
    /// other files kept): information, not a warning (21 Nov 2026, the launch bot's production retest of the bulk
    /// import's warning list: 6 of a file's 8 "warnings" were a Mathpix tex.zip's image list, with no sentence and no
    /// block). A file skipped, a local style not applied and an include not resolved stay warnings.
    /// </summary>
    public IReadOnlySet<string> InformationNotices { get; init; } = new HashSet<string>();

    /// <summary>The import diagnostic severity a notice is stored with: <c>info</c> for an inventory, else <c>warning</c>.</summary>
    public string SeverityOf(string notice) => InformationNotices.Contains(notice) ? "info" : "warning";

    /// <summary>
    /// A notice stored before 21 Nov 2026 as a warning though it is an inventory (category <c>auto_shimmed</c>, no code):
    /// its message starts "Found " (images, bibliography, listings) or says other files were kept. Readers that list
    /// warnings leave these rows out; new rows have severity <c>info</c>.
    /// </summary>
    public const string InventoryPrefix = "Found ";
    public const string OtherFilesKept = " other file(s) preserved as assets";
}

/// <summary>
/// A non-.tex file pulled from the zip. One shape for every category
/// (image / bib / style / listing / data / other) so the downstream
/// staging loop handles all of them uniformly — upload to R2, create
/// Asset row, optionally wire into a block.
///
/// Kind drives the finalize-time wire-up:
///   image   → rewrite figure blocks' src to the R2 URL
///   bib     → parse into BibliographyEntry rows
///   listing → inline into \lstinputlisting code blocks
///   style   → preserved only (we don't apply .sty/.cls on render)
///   data    → preserved only (pgfplots / csv / tsv / etc.)
///   other   → preserved only (fonts, included PDFs, etc.)
/// </summary>
public record LatexProjectFile(
    string Path,
    byte[] Bytes,
    string ContentType,
    string Kind);

public static class LatexProjectFileKinds
{
    public const string Image = "image";
    public const string Bib = "bib";
    public const string Listing = "listing";
    public const string Style = "style";
    public const string Data = "data";
    public const string Other = "other";
}

// Back-compat aliases for callers that still expect the old shape.
// Ok to keep during the extractor refactor — caller JobService uses
// .Count only, which works on IReadOnlyList<anything>.
public record LatexProjectImage(string Path, byte[] Bytes, string ContentType);
public record LatexProjectBibFile(string Path, string Content);

public class LatexProjectExtractor : ILatexProjectExtractor
{
    /// <summary>Largest file taken from a project; bigger ones are skipped with a notice.</summary>
    private const long MaxFileBytes = 5L * 1024 * 1024;

    private static readonly Regex DocumentClassPattern =
        new(@"\\documentclass(?:\[[^\]]*\])?\{[^}]+\}", RegexOptions.Compiled);
    private static readonly Regex BeginDocumentPattern =
        new(@"\\begin\{document\}", RegexOptions.Compiled);
    private static readonly Regex InputPattern =
        new(@"\\(input|include)\{([^}]+)\}", RegexOptions.Compiled);

    public LatexProjectResult Extract(byte[] zipBytes)
    {
        var texFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<LatexProjectFile>();
        var notices = new List<string>();

        // Per file, 5 MB of real bytes (a header can claim any size); the
        // archive-wide limits (entries, total, ratio) are the shared ones.
        var limits = ZipLimitsOptions.Default.WithMaxEntryBytes(MaxFileBytes);
        var budget = new ZipBudget(limits);

        using (var ms = new MemoryStream(zipBytes))
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            SafeZip.CheckEntryCount(archive, limits);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 || entry.FullName.EndsWith("/")) continue;

                // Skip macOS / Windows metadata noise early — users don't
                // want __MACOSX/ entries as Asset rows.
                if (entry.FullName.StartsWith("__MACOSX/") ||
                    entry.FullName.EndsWith(".DS_Store") ||
                    entry.FullName.EndsWith("Thumbs.db"))
                    continue;

                // Hard ceiling per file: 5 MB, as declared — caps the parser
                // memory budget and keeps R2 uploads reasonable. Large included
                // PDFs hit this; a notice says which file was skipped. The
                // declared size can lie, so every read below is also bounded
                // by real bytes (SafeZip): a lying entry fails the import.
                if (entry.Length > MaxFileBytes)
                {
                    notices.Add($"Skipped {entry.FullName} ({entry.Length / 1024 / 1024} MB) — per-file cap is 5 MB.");
                    continue;
                }
                SafeZip.CheckRatio(entry, limits);

                var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();

                // .tex / .ltx are the only files NOT staged as Assets —
                // they go through the inline resolver instead.
                // Every file is read whole and checked against the zip's CRC-32 (launch-bot #221, 23 Nov 2026): a zip
                // that lost a character in transit is refused instead of stored with a damaged image.
                byte[] bytes;
                using (var stream = SafeZip.OpenBounded(entry, budget))
                using (var buf = new MemoryStream())
                {
                    stream.CopyTo(buf);
                    bytes = buf.ToArray();
                }
                SafeZip.CheckCrc(entry, bytes);

                if (ext == ".tex" || ext == ".ltx")
                {
                    using var r = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
                    texFiles[NormalizeKey(entry.FullName)] = r.ReadToEnd();
                    continue;
                }

                // Everything else: classify, and add to the unified asset list.

                var kind = ClassifyKind(ext);
                files.Add(new LatexProjectFile(
                    entry.FullName,
                    bytes,
                    ContentTypeFromExt(ext),
                    kind));
            }
        }

        if (texFiles.Count == 0)
        {
            throw new InvalidOperationException(
                "Zip contains no .tex files — nothing to import.");
        }

        // Pick the main file: the one with \documentclass + \begin{document}.
        // Prefer a file literally named main.tex when multiple qualify.
        var mainKey = SelectMainFile(texFiles);
        if (mainKey is null)
        {
            throw new InvalidOperationException(
                "Could not identify a main .tex file (none contain both \\documentclass and \\begin{document}).");
        }

        // Inline \input / \include recursively.
        var unresolved = new List<string>();
        var inlined = InlineIncludes(texFiles[mainKey], texFiles, mainKey, unresolved, depth: 0);

        var images = files.Where(f => f.Kind == LatexProjectFileKinds.Image).ToList();
        var bibs = files.Where(f => f.Kind == LatexProjectFileKinds.Bib).ToList();
        var styles = files.Where(f => f.Kind == LatexProjectFileKinds.Style).ToList();
        var listings = files.Where(f => f.Kind == LatexProjectFileKinds.Listing).ToList();
        var other = files.Where(f => f.Kind == LatexProjectFileKinds.Data
                                    || f.Kind == LatexProjectFileKinds.Other).ToList();

        // An inventory is information (InformationNotices); the rest are warnings.
        var information = new HashSet<string>();
        void Inform(string notice) { notices.Add(notice); information.Add(notice); }

        if (images.Count > 0)
            Inform($"Found {images.Count} image file(s): {string.Join(", ", images.Take(6).Select(i => i.Path))}{(images.Count > 6 ? ", …" : "")}");
        if (styles.Count > 0)
            notices.Add($"Project contains {styles.Count} local style file(s) ({string.Join(", ", styles.Select(s => s.Path))}). Preserved as assets — not applied on render (we compile with pdflatex + a fixed preamble).");
        if (bibs.Count > 0)
            Inform($"Found {bibs.Count} bibliography file(s): {string.Join(", ", bibs.Select(b => b.Path))}.");
        if (listings.Count > 0)
            Inform($"Found {listings.Count} code listing file(s). Inlined into \\lstinputlisting code blocks when referenced.");
        if (other.Count > 0)
            Inform($"{other.Count}{LatexProjectResult.OtherFilesKept}: {string.Join(", ", other.Take(6).Select(f => f.Path))}{(other.Count > 6 ? ", …" : "")}");
        if (unresolved.Count > 0)
            notices.Add($"Could not resolve these \\input/\\include references: {string.Join(", ", unresolved)}.");

        return new LatexProjectResult(
            InlinedTex: inlined,
            MainFileName: mainKey,
            Files: files,
            UnresolvedIncludes: unresolved,
            Notices: notices)
        {
            InformationNotices = information,
            MainCandidates = texFiles
                .Where(kv => DocumentClassPattern.IsMatch(kv.Value) && BeginDocumentPattern.IsMatch(kv.Value))
                .Select(kv => kv.Key).OrderBy(k => k == mainKey ? 0 : 1).ThenBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static string? SelectMainFile(Dictionary<string, string> texFiles)
    {
        var candidates = texFiles
            .Where(kv => DocumentClassPattern.IsMatch(kv.Value) && BeginDocumentPattern.IsMatch(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        if (candidates.Count == 0) return null;
        // Preference: "main.tex" > shortest path, then the file name in alphabetical order, so the same zip always gives the
        // same document (RevTeX's sample zip took whichever of its four samples the zip listed first: launch-bot #241,
        // 18 Nov 2026). The connector's reply names the file used and the others (MainCandidates).
        var mainExact = candidates.FirstOrDefault(k =>
            string.Equals(Path.GetFileName(k), "main.tex", StringComparison.OrdinalIgnoreCase));
        if (mainExact != null) return mainExact;
        return candidates.OrderBy(k => k.Count(c => c == '/')).ThenBy(k => k.Length).ThenBy(k => k, StringComparer.OrdinalIgnoreCase).First();
    }

    private static string InlineIncludes(
        string source,
        Dictionary<string, string> texFiles,
        string currentKey,
        List<string> unresolved,
        int depth)
    {
        // Depth guard: stops circular \input chains.
        if (depth > 10) return source;

        return InputPattern.Replace(source, match =>
        {
            var target = match.Groups[2].Value.Trim();
            var resolved = ResolveTexReference(texFiles, target, currentKey);
            if (resolved is null)
            {
                unresolved.Add(target);
                // Keep the raw command — a passthrough lets the user see what's missing.
                return match.Value;
            }
            var body = texFiles[resolved];
            return InlineIncludes(body, texFiles, resolved, unresolved, depth + 1);
        });
    }

    private static string? ResolveTexReference(
        Dictionary<string, string> texFiles,
        string reference,
        string currentKey)
    {
        // Try exact match first, then with .tex appended, then relative-to-current.
        var candidates = new[]
        {
            reference,
            reference + ".tex",
            CombinePath(currentKey, reference),
            CombinePath(currentKey, reference + ".tex"),
        };
        foreach (var c in candidates)
        {
            var key = NormalizeKey(c);
            if (texFiles.ContainsKey(key)) return key;
        }
        return null;
    }

    private static string CombinePath(string currentKey, string relative)
    {
        var dir = Path.GetDirectoryName(currentKey) ?? string.Empty;
        return string.IsNullOrEmpty(dir) ? relative : $"{dir}/{relative}";
    }

    private static string NormalizeKey(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string ContentTypeFromExt(string ext) => ext switch
    {
        ".png"  => "image/png",
        ".jpg"  => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".gif"  => "image/gif",
        ".pdf"  => "application/pdf",
        ".svg"  => "image/svg+xml",
        ".eps"  => "application/postscript",
        ".webp" => "image/webp",
        ".bib"  => "application/x-bibtex",
        ".sty"  => "text/x-tex",
        ".cls"  => "text/x-tex",
        ".bst"  => "text/plain",
        ".csv"  => "text/csv",
        ".tsv"  => "text/tab-separated-values",
        ".dat"  => "text/plain",
        ".txt"  => "text/plain",
        ".json" => "application/json",
        ".yaml" => "text/yaml",
        ".yml"  => "text/yaml",
        ".py"   => "text/x-python",
        ".js"   => "text/javascript",
        ".ts"   => "text/typescript",
        ".cpp"  => "text/x-c++",
        ".c"    => "text/x-c",
        ".h"    => "text/x-c",
        ".java" => "text/x-java",
        ".sh"   => "text/x-shellscript",
        ".r"    => "text/x-r",
        ".m"    => "text/x-matlab",
        ".ttf"  => "font/ttf",
        ".otf"  => "font/otf",
        ".tikz" => "text/x-tex",
        ".pgf"  => "text/x-tex",
        _       => "application/octet-stream",
    };

    // Classify a non-.tex file so the finalize step knows how to wire it.
    // Kind drives: images → figure blocks; bib → BibliographyEntry;
    // listing → inline into \lstinputlisting code blocks; style / data /
    // other → preserved as Asset rows without block wiring.
    private static string ClassifyKind(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".eps" or ".webp" or ".pdf"
            => LatexProjectFileKinds.Image,
        ".bib"
            => LatexProjectFileKinds.Bib,
        ".sty" or ".cls" or ".bst"
            => LatexProjectFileKinds.Style,
        ".csv" or ".tsv" or ".dat" or ".txt" or ".json" or ".yaml" or ".yml"
            => LatexProjectFileKinds.Data,
        ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".java" or ".sh" or ".r" or ".m"
            => LatexProjectFileKinds.Listing,
        _   => LatexProjectFileKinds.Other,
    };
}
