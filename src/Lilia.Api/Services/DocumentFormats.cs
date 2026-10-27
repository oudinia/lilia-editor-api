using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Lilia.Api.Services;

/// <summary>
/// Lilia's preamble, precompiled (the user, 18 Oct: "Preamble caching makes total sense"). Loading
/// the preamble's 42 packages was about 1.7 s of each 1.9 s pdflatex pass; the document's own text
/// about 0.2 s. A format (mylatexformat) holds the loaded preamble, so a compile after an edit
/// starts from it: 0.85 s a pass, the same PDF.
///
/// <para>A format is one snapshot, not a stack of packages, so what goes into it is chosen to change
/// rarely: Lilia's part of the preamble, up to <c>% Document info</c> or the author's own lines
/// (<c>% lilia:your-preamble</c> in the preview, <c>% Custom preamble</c> in the export), whichever comes first. The title, author, date and the author's
/// lines are read on every compile, so editing them never rebuilds the format; a setting that
/// changes Lilia's part (paper, fonts, a theme) builds a new one once. Documents with the same
/// settings share a format. Nothing the author wrote is in it.</para>
///
/// <para>As TikZ figures do: a format not built yet is built in the background and this compile runs
/// without it; one that fails to build, or to compile with, is left alone for ten minutes and the
/// compile runs as before. pdflatex only. On disk, in the temp directory, at most
/// <see cref="MaxFormats"/> of them (about 7 MB each). They outlive deploys: a format is keyed by its
/// preamble, the theme files staged with it and the installed TeX, not by the server's build.</para>
/// </summary>
public sealed class DocumentFormats
{
    internal const string EndOfDump = @"\csname endofdump\endcsname";
    public const int MaxFormats = 40;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);
    /// <summary>
    /// What a format depends on besides its preamble text: the installed TeX (its own pdflatex format
    /// changes with every TeX Live update) and, per format, the theme files Lilia stages beside it.
    /// Not this server's build: keyed on the build, every deploy threw the formats away and the first
    /// PDF of each document after it paid full price (LTX-110: three TeX runs, 10 s, 18 Oct).
    /// </summary>
    internal static string TexIdentity => TexToken.Value;

    private static readonly Lazy<string> TexToken = new(() =>
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("kpsewhich", "-engine=pdftex pdflatex.fmt")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var path = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            var info = new FileInfo(path);
            if (info.Exists) return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch { /* no kpsewhich: fall back to the build */ }
        return typeof(DocumentFormats).Assembly.ManifestModule.ModuleVersionId.ToString("N");
    });

    private readonly string _dir;
    private readonly ConcurrentDictionary<string, Task<bool>> _builds = new();
    private readonly ConcurrentDictionary<string, DateTime> _failures = new();

    public DocumentFormats(string dir) => _dir = dir;

    /// <summary>Set LILIA_DOCUMENT_FORMATS=false to compile every preamble as before.</summary>
    public static DocumentFormats? Shared { get; } =
        Environment.GetEnvironmentVariable("LILIA_DOCUMENT_FORMATS") is "false" or "0"
            ? null
            : new(Path.Combine(Path.GetTempPath(), "lilia-document-formats"));

    /// <summary>
    /// The line index where the format stops (the first line read on every compile), or -1 when
    /// this .tex has no part to precompile (no <c>\begin{document}</c>, or nothing before the
    /// author's part).
    /// </summary>
    internal static int DumpEnd(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith(@"\begin{document}", StringComparison.Ordinal)
                || t == Lilia.Engines.RenderService.YourPreambleStart
                || t.StartsWith("% Custom preamble", StringComparison.Ordinal)
                || t.Equals("% Document info", StringComparison.Ordinal)
                || t.StartsWith(@"\title", StringComparison.Ordinal)
                || t.StartsWith(@"\author", StringComparison.Ordinal)
                || t.StartsWith(@"\date", StringComparison.Ordinal))
            {
                // A .tex without \begin{document} after this point is not a document.
                return i > 0 && lines.Skip(i).Any(l => l.TrimStart().StartsWith(@"\begin{document}", StringComparison.Ordinal)) ? i : -1;
            }
        }
        return -1;
    }

    /// <summary>The precompiled part of <paramref name="latex"/>, or null.</summary>
    internal static string? DumpPart(string latex)
    {
        var lines = latex.Replace("\r\n", "\n").Split('\n');
        var end = DumpEnd(lines);
        return end < 0 ? null : string.Join("\n", lines.Take(end));
    }

    /// <summary>The .tex to compile with the format: the same, with the format's end marked.</summary>
    internal static string WithEndOfDump(string latex)
    {
        var lines = latex.Replace("\r\n", "\n").Split('\n').ToList();
        var end = DumpEnd(lines);
        if (end < 0) return latex;
        lines.Insert(end, EndOfDump);
        return string.Join("\n", lines);
    }

    internal string PathFor(string dumpPart, string engine)
    {
        var themes = string.Concat(Lilia.Engines.Themes.ThemeCatalog.FilesUsedBy(dumpPart).Select(f => f.FileName + "\u0002" + f.Source + "\u0003"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TexToken.Value + "\u0001" + engine + "\u0001" + themes + "\u0001" + dumpPart)));
        return Path.Combine(_dir, "doc-" + hash[..24].ToLowerInvariant());
    }

    /// <summary>
    /// The format to compile <paramref name="latex"/> with (a path without <c>.fmt</c>), or null: the
    /// normal compile. A format not built yet starts building in the background, once.
    /// </summary>
    public string? FormatFor(string latex, string engine, Func<string, string, string, Task<bool>> build, ILogger logger)
    {
        if (engine != "pdflatex") return null;
        var dump = DumpPart(latex);
        if (dump is null) return null;
        var path = PathFor(dump, engine);
        if (File.Exists(path + ".fmt"))
        {
            try { File.SetLastWriteTimeUtc(path + ".fmt", DateTime.UtcNow); } catch { /* still usable */ }
            return path;
        }
        if (_failures.TryGetValue(path, out var failedAt) && DateTime.UtcNow - failedAt < RetryAfter) return null;
        _builds.GetOrAdd(path, p => Task.Run(async () =>
        {
            // Every later compile uses the format: the request that started the build may go away.
            Lilia.Engines.CompileSlots.Detach();
            try
            {
                var ok = await build(dump, engine, p);
                if (ok) { _failures.TryRemove(p, out _); Prune(); }
                else _failures[p] = DateTime.UtcNow;
                logger.LogInformation("[Compile] preamble format {Outcome}: {Name}", ok ? "built" : "could not be built", Path.GetFileName(p));
                return ok;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Compile] building the preamble format failed");
                _failures[p] = DateTime.UtcNow;
                return false;
            }
            finally
            {
                _builds.TryRemove(p, out _);
            }
        }));
        return null;
    }

    /// <summary>A format a compile could not use: compile without it for a while.</summary>
    public void Failed(string path)
    {
        _failures[path] = DateTime.UtcNow;
        try { File.Delete(path + ".fmt"); } catch { /* gone or in use */ }
    }

    /// <summary>Wait for the format of <paramref name="latex"/> (tests and warm-up). True when it is there.</summary>
    public async Task<bool> EnsureAsync(string latex, string engine, Func<string, string, string, Task<bool>> build, ILogger logger)
    {
        if (FormatFor(latex, engine, build, logger) is not null) return true;
        var dump = DumpPart(latex);
        if (dump is null) return false;
        var path = PathFor(dump, engine);
        if (_builds.TryGetValue(path, out var running)) await running;
        return File.Exists(path + ".fmt");
    }

    /// <summary>Keep the most recently used formats only.</summary>
    private void Prune()
    {
        try
        {
            foreach (var old in new DirectoryInfo(_dir).GetFiles("doc-*.fmt")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxFormats))
                old.Delete();
        }
        catch { /* best effort */ }
    }
}
