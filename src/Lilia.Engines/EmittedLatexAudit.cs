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
    public sealed record Use(string Key, int Line, Guid? BlockId, string Command = "");

    public IReadOnlyDictionary<string, int> Labels { get; }
    public IReadOnlyList<Use> References { get; }
    public IReadOnlyList<Use> Citations { get; }
    public IReadOnlySet<string> BibliographyKeys { get; }
    /// <summary>Labels with no number to print: no counter was stepped where they stand (MCP-92: a
    /// \label after a verbatim embed at the top of a report; "see also Listing ." in the PDF).</summary>
    public IReadOnlySet<string> UnnumberedLabels { get; }
    private readonly IReadOnlyList<(int Line, Guid Block)> _markers;

    private static readonly Regex LabelRx = new(@"\\label\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    // A listing labels itself in its options: \begin{lstlisting}[…, label={lst:py}] (13 Oct sweep:
    // every \ref to a listing was reported dangling though it resolves).
    private static readonly Regex ListingLabelRx = new(@"\\(?:begin\{lstlisting\}|lstinputlisting)\[[^\]]*?\blabel=\{?(?<k>[^,{}\]]+)\}?", RegexOptions.Compiled);
    private static readonly Regex RefRx = new(@"\\(?<cmd>eqref|autoref|cref|Cref|pageref|nameref|ref)\*?\{(?<k>[^{}]+)\}", RegexOptions.Compiled);
    // What gives the next \label a number, in source order (Numbering).
    private static readonly Regex NumberingRx = new(
        @"\\(?:(?<begin>begin)\{(?<env>[^{}]+)\}|(?<end>end)\{(?<env>[^{}]+)\}|(?<caption>caption)\b|(?<section>part|chapter|section|subsection|subsubsection|paragraph|subparagraph)(?<star>\*)?\s*[\[{]|(?<item>item)\b|(?<step>refstepcounter)\b|label\{(?<label>[^{}]+)\})",
        RegexOptions.Compiled);
    private static readonly Regex NewTheoremRx = new(@"\\newtheorem(?<star>\*)?\{(?<env>[^{}]+)\}", RegexOptions.Compiled);
    private static readonly HashSet<string> NumberedMath = new(StringComparer.Ordinal)
        { "equation", "align", "gather", "multline", "flalign", "alignat", "eqnarray" };
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
        var unnumbered = new HashSet<string>(StringComparer.Ordinal);
        var theorems = new HashSet<string>(StringComparer.Ordinal);
        var envs = new List<(string Name, bool Numbered)>();
        var inBody = false;
        var sectioned = false;
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
            if (!inBody)
            {
                foreach (Match m in NewTheoremRx.Matches(text))
                    if (!m.Groups["star"].Success) theorems.Add(m.Groups["env"].Value.Trim());
                if (text.Contains(@"\begin{document}", StringComparison.Ordinal)) inBody = true;
            }
            else
            {
                foreach (Match m in NumberingRx.Matches(text))
                {
                    if (m.Groups["begin"].Success)
                    {
                        var env = m.Groups["env"].Value.Trim();
                        envs.Add((env, NumberedMath.Contains(env) || theorems.Contains(env)));
                    }
                    else if (m.Groups["end"].Success)
                    {
                        var env = m.Groups["env"].Value.Trim();
                        var at = envs.FindLastIndex(e => e.Name == env);
                        if (at >= 0) envs.RemoveRange(at, envs.Count - at);
                    }
                    else if (m.Groups["caption"].Success || m.Groups["step"].Success
                        || (m.Groups["item"].Success && envs.Count > 0 && envs[^1].Name == "enumerate"))
                    {
                        if (envs.Count > 0) envs[^1] = (envs[^1].Name, true);
                        else sectioned = true;
                    }
                    else if (m.Groups["section"].Success && !m.Groups["star"].Success)
                        sectioned = true;
                    else if (m.Groups["label"].Success && !sectioned && !envs.Any(e => e.Numbered))
                        unnumbered.Add(m.Groups["label"].Value.Trim());
                }
            }
            foreach (var m in LabelRx.Matches(text).Concat(ListingLabelRx.Matches(text)))
                labels[m.Groups["k"].Value.Trim()] = labels.GetValueOrDefault(m.Groups["k"].Value.Trim()) + 1;
            foreach (Match m in RefRx.Matches(text))
                foreach (var k in m.Groups["k"].Value.Split(','))
                    if (k.Trim().Length > 0) refs.Add(new Use(k.Trim(), lineNo, block, m.Groups["cmd"].Value));
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
        UnnumberedLabels = unnumbered;
        _markers = markers;
    }

    /// <summary>A reference to a label the document never writes: it prints "??".</summary>
    public IEnumerable<Use> DanglingReferences => References.Where(r => !Labels.ContainsKey(r.Key));

    /// <summary>A reference that prints a number, to a label that has none: it prints nothing
    /// ("Listing ."). \pageref and \nameref still print something.</summary>
    public IEnumerable<Use> EmptyReferences => References.Where(r =>
        r.Command is not ("pageref" or "nameref") && UnnumberedLabels.Contains(r.Key) && Labels.GetValueOrDefault(r.Key) == 1);

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
