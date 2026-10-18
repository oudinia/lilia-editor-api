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
/// tolerant flag, every file beside it (figures, bibliography) and this build of the server (its
/// theme .sty files and its rules). Any edit, setting, look, figure or deploy changes the key, so
/// nothing has to be invalidated by hand and a stale PDF cannot be served.</para>
///
/// <para>In memory, bounded by bytes, least recently used first out, gone on restart: no user
/// content is written to disk for it. Two requests for the same compile at once share one run.
/// Failures are not kept.</para>
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

    private static readonly string BuildToken = typeof(CompileCache).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    private readonly long _maxBytes;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Entry Entry)>> _map = new();
    private readonly LinkedList<(string Key, Entry Entry)> _lru = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _inFlight = new();
    private long _bytes;

    public CompileCache(long maxBytes = DefaultMaxBytes) => _maxBytes = maxBytes;

    /// <summary>The cache every compile in this process shares.</summary>
    public static CompileCache Shared { get; } = new();

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
        Add(BuildToken);
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
        if (Get(key) is { } cached)
        {
            Interlocked.Increment(ref Hits);
            return (cached, true);
        }
        var mine = new Lazy<Task<Entry>>(compile);
        var running = _inFlight.GetOrAdd(key, mine);
        try
        {
            var entry = await running.Value;
            if (ReferenceEquals(running, mine))
            {
                Interlocked.Increment(ref Misses);
                Put(key, entry);
                return (entry, false);
            }
            Interlocked.Increment(ref Hits);
            return (entry, true);
        }
        finally
        {
            if (ReferenceEquals(running, mine)) _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<Entry>>>(key, mine));
        }
    }
}
