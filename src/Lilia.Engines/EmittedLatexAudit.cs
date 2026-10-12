using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// What the LaTeX Lilia actually emitted defines and points at (MCP hardening T1, 12 Oct:
/// <c>check_document</c> judged references from the stored fields, so a label the emitter never
/// printed still counted, and the PDF read "??" under a clean check).
///
/// One pass over the source, comments ignored: the labels written, the references and citations
/// made, the bibliography keys, and the <c>% block:{id}</c> marker in force on each line, so a
/// problem or a compile error can name its block.
/// </summary>
public sealed class EmittedLatexAudit
{
    public sealed record Use(string Key, int Line, Guid? BlockId);

    public IReadOnlyDictionary<string, int> Labels { get; }
    public IReadOnlyList<Use> References { get; }
    public IReadOnlyList<Use> Citations { get; }
    public IReadOnlySet<string> BibliographyKeys { get; }
    private readonly IReadOnlyList<(int Line, Guid Block)> _markers;

    private static readonly Regex LabelRx = new(@"\\label\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex RefRx = new(@"\\(?:eqref|autoref|cref|Cref|pageref|nameref|ref)\*?\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex CiteRx = new(@"\\(?:cite|citep|citet|citealp|citealt|parencite|textcite|footcite|autocite|nocite)\*?(?:\[[^\]]*\]){0,2}\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex BibItemRx = new(@"\\bibitem(?:\[[^\]]*\])?\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly Regex MarkerRx = new(@"^% block:(?<id>[0-9a-fA-F-]{36})\s*$", RegexOptions.Compiled);
    private static readonly Regex CommentRx = new(@"(?<!\\)%.*$", RegexOptions.Compiled);

    public EmittedLatexAudit(string source)
    {
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var refs = new List<Use>();
        var cites = new List<Use>();
        var bib = new HashSet<string>(StringComparer.Ordinal);
        var markers = new List<(int, Guid)>();
        Guid? block = null;
        var lines = (source ?? "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var raw = lines[i].TrimEnd('\r');
            var marker = MarkerRx.Match(raw);
            if (marker.Success && Guid.TryParse(marker.Groups["id"].Value, out var id))
            {
                block = id;
                markers.Add((lineNo, id));
                continue;
            }
            var text = CommentRx.Replace(raw, "");
            if (text.Length == 0) continue;
            foreach (Match m in LabelRx.Matches(text))
                labels[m.Groups["k"].Value.Trim()] = labels.GetValueOrDefault(m.Groups["k"].Value.Trim()) + 1;
            foreach (Match m in RefRx.Matches(text))
                foreach (var k in m.Groups["k"].Value.Split(','))
                    if (k.Trim().Length > 0) refs.Add(new Use(k.Trim(), lineNo, block));
            foreach (Match m in CiteRx.Matches(text))
                foreach (var k in m.Groups["k"].Value.Split(','))
                    if (k.Trim().Length > 0) cites.Add(new Use(k.Trim(), lineNo, block));
            foreach (Match m in BibItemRx.Matches(text))
                bib.Add(m.Groups["k"].Value.Trim());
        }
        Labels = labels;
        References = refs;
        Citations = cites;
        BibliographyKeys = bib;
        _markers = markers;
    }

    /// <summary>A reference to a label the document never writes: it prints "??".</summary>
    public IEnumerable<Use> DanglingReferences => References.Where(r => !Labels.ContainsKey(r.Key));

    /// <summary>A label written more than once: LaTeX keeps the last.</summary>
    public IEnumerable<string> DuplicateLabels => Labels.Where(l => l.Value > 1).Select(l => l.Key);

    /// <summary>A citation with no bibliography entry: it prints "?". <c>\nocite{*}</c> is not a key.</summary>
    public IEnumerable<Use> MissingCitations => Citations.Where(c => c.Key != "*" && !BibliographyKeys.Contains(c.Key));

    /// <summary>The block whose output holds <paramref name="line"/>, or null in the preamble.</summary>
    public Guid? BlockAtLine(int line)
    {
        Guid? found = null;
        foreach (var (at, id) in _markers)
        {
            if (at > line) break;
            found = id;
        }
        return found;
    }
}
