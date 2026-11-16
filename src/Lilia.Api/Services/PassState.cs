using System.Security.Cryptography;
using System.Text;

namespace Lilia.Api.Services;

/// <summary>
/// What a document's last LaTeX compile left beside its PDF (<c>.aux</c>, <c>.toc</c>, <c>.out</c>, <c>.bbl</c>…),
/// kept so the next compile of the same document starts from it (16 Nov 2026,
/// lilia-docs/reports/2026-11-16-latex-compile-speed.md).
///
/// <para>Every compile ran pdflatex twice in a new folder, because pass one never had the cross-references
/// the pass before had written. After an edit that moves no reference (most edits) the second pass changed
/// nothing: C-003 took 2.01 s for two passes, 0.87 s for one. Overleaf keeps its compile folder for the same
/// reason, and latexmk stops by the same rule.</para>
///
/// <para>The rule: the kept files go in before pass one. If pass one (and BibTeX, when the document has a
/// bibliography) leaves every one of them exactly as it found them and LaTeX asks for no rerun, the PDF is the
/// one more passes would give: they would read the same files again. Otherwise the passes run as before —
/// pass two and, when LaTeX asks, three; a document with a bibliography starts again from an empty folder,
/// as it always did.</para>
///
/// <para>Only for a compile marked with its document (<see cref="ForDocument"/>: the PDF export, which the
/// preview uses), and only from a compile of that same document, with the same engine, tolerant setting,
/// preamble and installed TeX: a kept <c>.aux</c> can name commands only that preamble defines. Per user, in
/// this process's memory only (a restart starts each document with two passes again), at most
/// <see cref="MaxBytes"/>, least recently used out first. Closing an account removes its entries.
/// Set LILIA_KEEP_PASS_STATE=false to compile every document from an empty folder as before.</para>
/// </summary>
public sealed class PassState
{
    public const long MaxBytes = 64L * 1024 * 1024;
    public const long MaxEntryBytes = 2L * 1024 * 1024;

    /// <summary>The store every compile shares, or null when turned off.</summary>
    public static PassState? Shared { get; } =
        string.Equals(Environment.GetEnvironmentVariable("LILIA_KEEP_PASS_STATE"), "false", StringComparison.OrdinalIgnoreCase)
            ? null : new PassState();

    /// <summary>Compiles that needed one pass because the kept files did not change (tests and the log).</summary>
    internal static int OnePassCompiles;

    private static readonly AsyncLocal<string?> CurrentDocument = new();

    /// <summary>The document the compiles in this flow belong to, or null.</summary>
    public static string? Document => CurrentDocument.Value;

    /// <summary>Mark the compiles inside the returned scope as compiles of <paramref name="documentId"/>.</summary>
    public static IDisposable ForDocument(Guid documentId)
    {
        var before = CurrentDocument.Value;
        CurrentDocument.Value = documentId.ToString("N");
        return new Restore(before);
    }

    private sealed class Restore(string? before) : IDisposable
    {
        public void Dispose() => CurrentDocument.Value = before;
    }

    /// <summary>Files of the job that are not state a pass reads: the source, the outputs, the log.</summary>
    private static readonly HashSet<string> NotState = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tex", ".pdf", ".log", ".synctex", ".gz", ".fls", ".fdb_latexmk", ".dvi", ".xdv",
    };

    /// <summary>
    /// The key of this compile's state, or null when it belongs to no document. Everything before
    /// <c>\begin{document}</c> is in it: a kept file can only be read under the preamble that wrote it.
    /// </summary>
    public static string? KeyFor(string latex, string engine, bool tolerant)
    {
        if (Document is not { } doc) return null;
        var begin = latex.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var preamble = begin < 0 ? latex : latex[..begin];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            doc + "\u0001" + engine + "\u0001" + tolerant + "\u0001" + DocumentFormats.TexIdentity + "\u0001" + preamble)));
        return CacheOwner.Scope(CacheOwner.Current) + ":" + hash;
    }

    /// <summary>The state files of job <paramref name="stem"/> in <paramref name="dir"/>, by extension (".aux").</summary>
    public static Dictionary<string, byte[]> Read(string dir, string stem = "document")
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return files;
        foreach (var path in Directory.EnumerateFiles(dir, stem + ".*"))
        {
            var ext = Path.GetFileName(path)[stem.Length..];
            if (ext.Length < 2 || NotState.Contains(Path.GetExtension(path))) continue;
            files[ext] = File.ReadAllBytes(path);
        }
        return files;
    }

    public static void Write(string dir, IReadOnlyDictionary<string, byte[]> files, string stem = "document")
    {
        foreach (var (ext, bytes) in files) File.WriteAllBytes(Path.Combine(dir, stem + ext), bytes);
    }

    /// <summary>The same files with the same bytes.</summary>
    public static bool Same(IReadOnlyDictionary<string, byte[]> a, IReadOnlyDictionary<string, byte[]> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && kv.Value.AsSpan().SequenceEqual(other));

    /// <summary>Every state file of the job except <paramref name="stem"/>.tex: the folder as a first pass finds it.</summary>
    public static void Clear(string dir, string stem = "document")
    {
        foreach (var path in Directory.EnumerateFiles(dir, stem + ".*"))
            if (!string.Equals(Path.GetExtension(path), ".tex", StringComparison.OrdinalIgnoreCase)) File.Delete(path);
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Dictionary<string, byte[]> Files, long Bytes)>> _map = new();
    private readonly LinkedList<(string Key, Dictionary<string, byte[]> Files, long Bytes)> _lru = new();
    private long _bytes;

    public long Bytes { get { lock (_lock) return _bytes; } }

    public IReadOnlyDictionary<string, byte[]>? Get(string key)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var node)) return null;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Files;
        }
    }

    public void Put(string key, Dictionary<string, byte[]> files)
    {
        var bytes = files.Sum(f => (long)f.Value.Length + f.Key.Length * 2);
        lock (_lock)
        {
            if (_map.Remove(key, out var old))
            {
                _lru.Remove(old);
                _bytes -= old.Value.Bytes;
            }
            if (files.Count == 0 || bytes > MaxEntryBytes) return;
            _map[key] = _lru.AddFirst((key, files, bytes));
            _bytes += bytes;
            while (_bytes > MaxBytes && _lru.Last is { } last)
            {
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }
        }
    }

    /// <summary>Drop one user's entries (account closed); how many.</summary>
    public int RemoveScope(string scope)
    {
        var prefix = scope + ":";
        lock (_lock)
        {
            var gone = _map.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var k in gone)
            {
                _bytes -= _map[k].Value.Bytes;
                _lru.Remove(_map[k]);
                _map.Remove(k);
            }
            return gone.Count;
        }
    }
}
