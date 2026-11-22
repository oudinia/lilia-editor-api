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
    public const string ProcedureVersion = "2026-11-22.1"; // Lilia's own bibliography names the volume, number, pages, publisher and DOI, and a title keeps a character already escaped (\\#) escaped once (launch-bot #221 retest). Before (11-21.1): A document holding Cyrillic (or Greek the preamble does not map) compiles with LuaLaTeX and its Unicode faces, whatever its language (the launch bot's bulk import, Rostov notes). Before (11-19.13): The images a raw LaTeX block includes are packed from the document's files (launch-bot #236). Before (11-19.12): REVTeX writes \\preprint before the title, and an author's \\altaffiliation[…], \\homepage and \\collaboration as written (launch-bot #238). Before (11-19.11): acmart writes an author's \\authornote, \\authornotemark and \\correspondingauthor after its \\author, and a short ORCID (launch-bot #239). Before (11-19.10): An image embedded in a figure (a PNG or JPEG data: source) prints in the PDF instead of a Missing figure frame (task 5). Before (11-19.9): A missing figure's frame names the file as the source wrote it, not image-0.png (launch-bot #244 note). Before (11-19.8): A TeX Live image (example-image-a) is included as written, and amsart writes \\curraddr and \\date (launch-bot #244, #243 notes). Before (11-19.7): Inline maths across one line break stays maths, and amsart writes a paper's \\subjclass and \\dedicatory (launch-bot #243). Before (11-19.6): A heading written over several lines is written on one (launch-bot #145). Before (11-19.5): An author's note keeps its citation in a publisher class's front matter (launch-bot #249 retest). Before (11-19.4): A \\thanks keeps a citation, reference or maths inside it, and amsart writes the title's note after \\title (launch-bot #249). Before (11-19.3): A $$ display written over several lines in a theorem or proof stays maths (launch-bot #180), and a deck gives xcolor its options before the class. Before (11-19.2): A table in a slide deck frame compiles (no #1 in the frame), a frame holding a definition with a parameter is fragile, and a numbered list keeps its start (launch-bot #247). Before (11-19.1): A paper's own theorem environments keep their names, shared counters and styles (launch-bot #230), and a slide deck keeps a frame written in an embed as one frame and an unnumbered divider heading's \\section* (the nightly run). Before (11-18.5): Arabic emphasis prints in Amiri Bold, not slanted (Oussama). Before (11-18.4): A TikZ figure whose source captions itself gets no second, empty caption, and an accent command \\^{…} or \\~{…} in text is no longer escaped as a caret or tilde (launch-bot #167, #169). Before (11-18.3): APS authors print an ORCID as \\orcidlink and each whole affiliation on its own, and amsart authors print their \\thanks (launch-bot #143). Before (11-18.2): Natbib-only settings from an imported preamble apply only with natbib, a ``quote closed by a straight " closes, and an Arabic document's Latin references are set left to right with their own italics (launch-bot #161, #163, #154). Before (11-18.1): The PDF's language goes in its catalog (/Lang), not through hyperref's pdflang (launch-bot #158). Before (11-17.6): The PDF carries its title and language; an escaped \\% in a table cell prints % (launch-bot #158, #152). Before (11-17.5): A preserved algorithm2e or algpseudocode is left out beside Lilia's algorithm + algorithmic, ``quotes'' aren't code, a $\\$$ formula stays whole, theorems and list items keep single line breaks, an imported \\date{} prints none and APS authors print their \\thanks (launch-bot #138, #141, #143, #144, #145). Before (11-17.4): Venues: ACM and Elsevier write the city and country, Elsevier's one-line address is plain text (no trailing comma), IEEE, LNCS and APS captions follow the class (launch-bot #134, #136). Before (11-17.3): An unnumbered alignment drops its labels (amsmath never writes them; one left stopped the next equation), and a chapter stays @incollection (launch-bot #124 note, #131). Before (11-17.2): references.bib carries an arXiv preprint's eprint fields and a chapter's series and editors; a footnote's text is escaped like its paragraph's; a conference paper names its book (launch-bot #127, #131, #132). Before (11-17.1): An arXiv preprint prints its number in Lilia's own bibliography, and a TikZ figure's alt text is its ACM \\Description (launch-bot #127, #129). Before (11-16.17): Long code lines wrap at spaces in Lilia's own PDF (breaklines), as in the .tex download (the connector's checks). Before (11-16.16): A slide deck's headings open frames titled with them (imported decks' frame titles print). Before (11-16.15): revtex (APS, AIP) contents: a third pass when the number widths moved. Before (11-16.14): A CV prints no name-and-date title above its Personal information block. Before (11-16.13): Automatic sends a document with emoji to LaTeX, which draws them (Typst has no emoji font). Before (11-16.12): Emoji print as pictures under LaTeX (twemojis; the document sweep, E-015). Before (11-16.11): A table of contents' own title also prints under revtex (APS, AIP), which reads \\tocname (the document sweep, chunk 5). Before (11-16.10): A table of contents prints under its own title when the author set one (the document sweep). Before (11-16.9): Code blocks in Arabic and Hebrew documents read left to right (the document sweep, chunk 3). Before (11-16.8): An equation with unbalanced braces prints a note instead of stopping the PDF (the document sweep, E-025). Before (11-16.7): An author-escaped \\$ is escaped once, not twice (the document sweep, chunk 2). Before (11-16.6): An escaped \\$ beside maths leaves the formula whole; \\ce in an abstract loads mhchem (the document sweep, chunk 2). Before (11-16.5): A document compiles from its last compile's .aux (PassState): one pass when nothing moved; XeLaTeX sees the system font cache in the sandbox. Before (11-16.4): ℝ ℕ ℤ ℚ ℂ ℍ ℙ ℓ print under LaTeX (the document sweep, E-015). Before (11-16.3): Ticks, crosses, warnings and stars print under LaTeX, and combining accents are composed (the document sweep). Before (11-16.2): Text cut off the page (Overfull \\vbox while \\output) is reported as a problem (the document sweep, E-033). Before (11-16.1): A numbered list on slides (beamer) prints instead of stopping the PDF (the document sweep, B-010). Before (11-15.7): Many floats in a row no longer lose figures (maxdeadcycles), and a custom \\renewcommand of a command the class lacks is provided first (the document sweep). Before (15.6): A figure with no image keeps its caption in Typst, and subfigures go to LaTeX (the document sweep). Before (15.5): A tblr table loads tabularray (#113 note). Before (15.4): Typst prints a note for raw LaTeX and CV blocks; Automatic sends CV blocks to LaTeX (#113, #114). Before (15.3): A CV photo prints from its file (#114). Before (15.2): A compile error in the Custom preamble quotes the author's line (#100 follow-up). Before (15.1): Typst prints a raw-LaTeX block as a note
    // Before: "2026-11-06.2" // language detection skips ids and single letters (.1: matrices, cases and split keep their environment)

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
        catch (OperationCanceledException) when (!ReferenceEquals(running, mine)
            && !Lilia.Engines.CompileSlots.Abandoned.IsCancellationRequested)
        {
            // The compile this joined was stopped because ITS request went away (CompileSlots); this one
            // is still wanted, so it compiles for itself (taking the stopped one out of the way first).
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<Entry>>>(scoped, running));
            return await GetOrCompileAsync(key, compile);
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
