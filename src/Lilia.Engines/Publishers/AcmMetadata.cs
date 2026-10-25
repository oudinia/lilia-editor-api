using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Engines.Publishers;

/// <summary>
/// What ACM gives an author to paste in (publisher templates phase 3, TAPS, 25 Oct 2026), kept to
/// what ACM's tools produce:
/// <list type="bullet">
/// <item><b>CCS concepts</b>, from ACM's Computing Classification System tool (dl.acm.org/ccs): a
/// <c>CCSXML</c> block and its <c>\ccsdesc[relevance]{Concept~Subconcept}</c> lines. TAPS requires them
/// in full-length papers.</item>
/// <item><b>Rights and bibstrip</b>, from ACM's eRights email after acceptance: <c>\setcopyright</c>,
/// <c>\acmDOI</c>, <c>\acmISBN</c>, <c>\acmConference</c>… TAPS requires them in the final version.</item>
/// </list>
/// Any other line is set aside and reported, so the title block can't carry arbitrary LaTeX into the
/// preamble (ACM's "LaTeX Best Practices", May 2024).
/// </summary>
public static partial class AcmMetadata
{
    [GeneratedRegex(@"^\\(?:setcopyright|acmDOI|acmISBN|acmConference|acmBooktitle|acmYear|copyrightyear|acmPrice|acmJournal|acmVolume|acmNumber|acmArticle|acmArticleSeq|acmMonth|acmSubmissionID|received|acmPubDate|acmBadge)\b")]
    private static partial Regex RightsCommand();

    [GeneratedRegex(@"^\\ccsdesc(?:\[\d+\])?\{[^{}]*\}$")]
    private static partial Regex CcsDesc();

    /// <summary>The rights block's lines Lilia writes, and the ones it set aside.</summary>
    public static (string Accepted, IReadOnlyList<string> Ignored) Rights(string? text) =>
        Filter(text, line => RightsCommand().Match(line) is { Success: true } m && OnlyArguments(line, m.Length));

    /// <summary>The CCS concepts' lines Lilia writes (the CCSXML block whole, the \ccsdesc lines), and the rest.</summary>
    public static (string Accepted, IReadOnlyList<string> Ignored) Ccs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", []);
        var accepted = new StringBuilder();
        var ignored = new List<string>();
        var inXml = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('%')) continue;
            if (line == @"\begin{CCSXML}") { inXml = true; accepted.AppendLine(line); continue; }
            if (line == @"\end{CCSXML}") { inXml = false; accepted.AppendLine(line); continue; }
            // Inside the block it is XML for ACM's indexers; a backslash there would be LaTeX, not XML.
            if (inXml) { if (line.Contains('\\')) ignored.Add(line); else accepted.AppendLine(line); continue; }
            if (CcsDesc().IsMatch(line)) accepted.AppendLine(line); else ignored.Add(line);
        }
        if (inXml) accepted.AppendLine(@"\end{CCSXML}");
        return (accepted.ToString(), ignored);
    }

    /// <summary>Whether the CCS text names at least one concept (a \ccsdesc line).</summary>
    public static bool HasConcepts(string? text) => Ccs(text).Accepted.Contains(@"\ccsdesc", StringComparison.Ordinal);

    /// <summary>Whether the rights block sets the copyright (\setcopyright), the line TAPS checks first.</summary>
    public static bool HasRights(string? text) => Rights(text).Accepted.Contains(@"\setcopyright", StringComparison.Ordinal);

    private static (string, IReadOnlyList<string>) Filter(string? text, Func<string, bool> keep)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", []);
        var accepted = new StringBuilder();
        var ignored = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('%')) continue;
            if (keep(line)) accepted.AppendLine(line); else ignored.Add(line);
        }
        return (accepted.ToString(), ignored);
    }

    /// <summary>
    /// After the command, only its arguments: <c>[…]</c> and balanced <c>{…}</c> groups to the end of the
    /// line. A line that goes on with more LaTeX after them (<c>\acmDOI{x}\input{…}</c>) is set aside.
    /// </summary>
    private static bool OnlyArguments(string line, int from)
    {
        var i = from;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            var (open, close) = line[i] switch { '[' => ('[', ']'), '{' => ('{', '}'), _ => ('\0', '\0') };
            if (open == '\0') return false;
            var depth = 0;
            for (; i < line.Length; i++)
            {
                if (line[i] == '\\') { i++; continue; }
                if (line[i] == open) depth++;
                else if (line[i] == close && --depth == 0) { i++; break; }
            }
            if (depth != 0) return false;
        }
        return true;
    }
}
