using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Lilia.Api.Services;

/// <summary>
/// The compiled PDF of an unchanged document, kept (the user, 18 Oct: "Generating the pdf is time
/// consuming and probably cpu bound. Caching in the case of an unmodified document, like
/// validation?"). A compile took 4–5 s for a six-block document, every time it was asked for.
///
/// <para>Keyed by what the engine is given, not by the document: the .tex, the engine, the
/// tolerant flag, every file beside it (figures, bibliography), the theme files staged with it,
/// the installed TeX and <see cref="ProcedureVersion"/>. Any edit, setting, look or figure changes
/// the key, so nothing has to be invalidated by hand and a stale PDF cannot be served.</para>
///
/// <para>Per user (18 Oct: "add redis and cache in it per user"): entries are kept under the
/// signed-in user (<see cref="CacheOwner"/>), first in this process's memory (bounded by bytes,
/// least recently used out first), then in Redis for three days (<see cref="UserCache"/>), so a
/// restart or a deploy no longer starts every document cold. Closing an account deletes its
/// entries. Two requests for the same compile at once share one run. Failures are not kept.</para>
/// </summary>
public sealed class CompileCache
{
    public sealed record Entry(byte[] Pdf, string Log, string Aux)
    {
        public long Bytes => Pdf.LongLength + (Log.Length + Aux.Length) * 2L;
    }

    /// <summary>About 200 typical documents.</summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;
    /// <summary>A single PDF above this is compiled every time rather than crowding out the rest.</summary>
    public const long MaxEntryBytes = 32L * 1024 * 1024;

    /// <summary>
    /// How Lilia compiles (the passes, BibTeX inside them, tolerant mode). Bump it whenever
    /// <c>LaTeXRenderService.CompileInDirAsync</c> or the bibliography steps change what they
    /// produce: kept results outlive deploys, and would otherwise be served from the old steps.
    /// </summary>
    public const string ProcedureVersion = "2026-10-18.1";

    private readonly long _maxBytes;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Entry Entry)>> _map = new();
    private readonly LinkedList<(string Key, Entry Entry)> _lru = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _inFlight = new();
    private long _bytes;

    private readonly string _kind;

    public CompileCache(long maxBytes = DefaultMaxBytes, string kind = "pdf")
    {
        _maxBytes = maxBytes;
        _kind = kind;
    }

    /// <summary>The cache every compile in this process shares.</summary>
    public static CompileCache Shared { get; } = new();

    /// <summary>
    /// Finished PDFs by document version (DocumentVersions): answered before anything is rendered.
    /// The entry's Log holds the engine that made it.
    /// </summary>
    public static CompileCache Versions { get; } = new(128L * 1024 * 1024, "pdfv");

    /// <summary>The whole-document check's answer (JSON, in the entry's bytes) by document version.</summary>
    public static CompileCache Checks { get; } = new(32L * 1024 * 1024, "valv");

    public long Hits;
    public long Misses;
    public long Bytes { get { lock (_lock) return _bytes; } }
    public int Count { get { lock (_lock) return _map.Count; } }

    /// <summary>The key for compiling <paramref name="latex"/> with the files in <paramref name="dir"/>.</summary>
    public static string Key(string latex, string engine, bool tolerant, string? dir)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            sha.AppendData(BitConverter.GetBytes(b.Length));
            sha.AppendData(b);
        }
        Add(ProcedureVersion);
        Add(DocumentFormats.TexIdentity);
        foreach (var (name, source) in Lilia.Engines.Themes.ThemeCatalog.FilesUsedBy(latex)) { Add(name); Add(source); }
        Add(engine);
        Add(tolerant ? "tolerant" : "strict");
        Add(latex);
        if (dir is not null && Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                         .Select(f => (Rel: Path.GetRelativePath(dir, f).Replace('\\', '/'), Full: f))
                         .OrderBy(f => f.Rel, StringComparer.Ordinal))
            {
                Add(file.Rel);
                using var stream = File.OpenRead(file.Full);
                Add(Convert.ToHexString(SHA256.HashData(stream)));
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public Entry? Get(string key)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var node)) return null;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Entry;
        }
    }

    public void Put(string key, Entry entry)
    {
        if (entry.Bytes > MaxEntryBytes || entry.Bytes > _maxBytes) return;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var old))
            {
                _lru.Remove(old);
                _bytes -= old.Value.Entry.Bytes;
            }
            var node = _lru.AddFirst((key, entry));
            _map[key] = node;
            _bytes += entry.Bytes;
            while (_bytes > _maxBytes && _lru.Last is { } last)
            {
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _bytes -= last.Value.Entry.Bytes;
            }
        }
    }

    /// <summary>
    /// The cached result for <paramref name="key"/>, or <paramref name="compile"/>'s, run once however
    /// many ask at the same moment. <paramref name="hit"/> says which.
    /// </summary>
    public async Task<(Entry Entry, bool Hit)> GetOrCompileAsync(string key, Func<Task<Entry>> compile)
    {
        // The current user's entry: this process's memory, then Redis, then the compile.
        var scoped = CacheOwner.Scope(CacheOwner.Current) + ":" + key;
        if (Get(scoped) is { } cached)
        {
            Interlocked.Increment(ref Hits);
            return (cached, true);
        }
        if (await UserCache.GetAsync(_kind, key) is { } kept && Deserialize(kept) is { } fromRedis)
        {
            Interlocked.Increment(ref Hits);
            Lilia.Core.Diagnostics.Timing.Mark(_kind + " hit (redis)");
            Put(scoped, fromRedis);
            return (fromRedis, true);
        }
        var mine = new Lazy<Task<Entry>>(compile);
        var running = _inFlight.GetOrAdd(scoped, mine);
        try
        {
            var entry = await running.Value;
            if (ReferenceEquals(running, mine))
            {
                Interlocked.Increment(ref Misses);
                Put(scoped, entry);
                if (entry.Bytes <= MaxEntryBytes) await UserCache.SetAsync(_kind, key, Serialize(entry));
                return (entry, false);
            }
            Interlocked.Increment(ref Hits);
            return (entry, true);
        }
        finally
        {
            if (ReferenceEquals(running, mine)) _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<Entry>>>(scoped, mine));
        }
    }

    /// <summary>Drop one user's entries from this process's memory (account closed); how many.</summary>
    public int RemoveScope(string scope)
    {
        var prefix = scope + ":";
        lock (_lock)
        {
            var gone = _map.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var k in gone)
            {
                var node = _map[k];
                _lru.Remove(node);
                _bytes -= node.Value.Entry.Bytes;
                _map.Remove(k);
            }
            return gone.Count;
        }
    }

    internal static byte[] Serialize(Entry e)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1); // format
            w.Write(e.Pdf.Length); w.Write(e.Pdf);
            w.Write(e.Log); w.Write(e.Aux);
        }
        return ms.ToArray();
    }

    internal static Entry? Deserialize(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            if (r.ReadInt32() != 1) return null;
            var pdf = r.ReadBytes(r.ReadInt32());
            return new Entry(pdf, r.ReadString(), r.ReadString());
        }
        catch { return null; }
    }
}
