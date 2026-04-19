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
    IReadOnlyList<string> ImagesFound,
    IReadOnlyList<string> StyleFilesFound,
    IReadOnlyList<string> BibFilesFound,
    IReadOnlyList<string> UnresolvedIncludes,
    IReadOnlyList<string> Notices);

public class LatexProjectExtractor : ILatexProjectExtractor
{
    private static readonly Regex DocumentClassPattern =
        new(@"\\documentclass(?:\[[^\]]*\])?\{[^}]+\}", RegexOptions.Compiled);
    private static readonly Regex BeginDocumentPattern =
        new(@"\\begin\{document\}", RegexOptions.Compiled);
    private static readonly Regex InputPattern =
        new(@"\\(input|include)\{([^}]+)\}", RegexOptions.Compiled);

    public LatexProjectResult Extract(byte[] zipBytes)
    {
        var texFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<string>();
        var styles = new List<string>();
        var bibs = new List<string>();
        var notices = new List<string>();

        using (var ms = new MemoryStream(zipBytes))
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 || entry.FullName.EndsWith("/")) continue;

                // Hard ceiling per file: 5 MB. Stops zip-bombs and keeps the
                // parser's memory budget predictable.
                if (entry.Length > 5 * 1024 * 1024)
                {
                    notices.Add($"Skipped {entry.FullName} ({entry.Length} bytes) — per-file cap is 5 MB.");
                    continue;
                }

                var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
                switch (ext)
                {
                    case ".tex":
                    case ".ltx":
                    {
                        using var r = new StreamReader(entry.Open(), Encoding.UTF8);
                        texFiles[NormalizeKey(entry.FullName)] = r.ReadToEnd();
                        break;
                    }
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".gif":
                    case ".pdf":
                    case ".svg":
                        images.Add(entry.FullName);
                        break;
                    case ".sty":
                    case ".cls":
                        styles.Add(entry.FullName);
                        break;
                    case ".bib":
                        bibs.Add(entry.FullName);
                        break;
                    default:
                        notices.Add($"Ignored unsupported file {entry.FullName}.");
                        break;
                }
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

        if (images.Count > 0)
            notices.Add($"Found {images.Count} image file(s) in the project. They're not auto-uploaded yet — you can re-add them from the Figure block. Files: {string.Join(", ", images.Take(6))}{(images.Count > 6 ? ", …" : "")}");
        if (styles.Count > 0)
            notices.Add($"Project contains {styles.Count} local style file(s) ({string.Join(", ", styles)}). These aren't applied — we compile with pdflatex and a fixed preamble.");
        if (bibs.Count > 0)
            notices.Add($"Project contains {bibs.Count} bibliography file(s). Citations are preserved but the .bib content isn't yet imported — re-import entries from the Bibliography panel.");
        if (unresolved.Count > 0)
            notices.Add($"Could not resolve these \\input/\\include references: {string.Join(", ", unresolved)}.");

        return new LatexProjectResult(
            InlinedTex: inlined,
            MainFileName: mainKey,
            ImagesFound: images,
            StyleFilesFound: styles,
            BibFilesFound: bibs,
            UnresolvedIncludes: unresolved,
            Notices: notices);
    }

    private static string? SelectMainFile(Dictionary<string, string> texFiles)
    {
        var candidates = texFiles
            .Where(kv => DocumentClassPattern.IsMatch(kv.Value) && BeginDocumentPattern.IsMatch(kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        if (candidates.Count == 0) return null;
        // Preference: "main.tex" / "main" > shortest path.
        var mainExact = candidates.FirstOrDefault(k =>
            string.Equals(Path.GetFileName(k), "main.tex", StringComparison.OrdinalIgnoreCase));
        if (mainExact != null) return mainExact;
        return candidates.OrderBy(k => k.Count(c => c == '/')).ThenBy(k => k.Length).First();
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
}
