using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// The "resume" LaTeX template family (Jake's resume and its many forks: \resumeSubheading, \resumeItem,
/// \resumeProjectHeading, the \resume…ListStart/End wrappers), imported as a CV (the document sweep, D-013 and D-014,
/// 16 Nov 2026). Its macros came in as raw text: "\resumeSubheading {Amazon}{May 2017 -- Aug. 2017} {Software…".
///
/// <para>Forks order \resumeSubheading's four arguments differently (Jake's is {org}{place}{role}{dates}; others put
/// the dates second), so the arguments are read by what they hold: the one that looks like a date range is the period,
/// one that looks like a place ("City, ST", "Remote") the location, and the rest, in Jake's order, the organisation and
/// the role. Each heading becomes a CV entry (through moderncv's entry marker, so <see cref="ModernCv.Build(ImportDocument, bool)"/>
/// builds it, and each \section becomes a CV section); each \resumeItem a bullet.</para>
/// </summary>
public static class ResumeTemplate
{
    /// <summary>A resume-template source: its body uses \resumeSubheading or \resumeProjectHeading.</summary>
    public static bool Is(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var body = at < 0 ? content : content[at..];
        return Regex.IsMatch(body, @"\\resume(Subheading|ProjectHeading|Project)\b");
    }

    private static readonly Regex DateLike = new(@"\b(19|20)\d{2}\b|\b(present|current|now|today|ongoing)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlaceLike = new(@"^[^,]{2,40},\s*[A-Za-z .]{2,30}$|^(remote|online|hybrid)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The four \resumeSubheading arguments as \cventry's six (years, title, employer, city, grade, description).</summary>
    internal static string[] SubheadingAsEntry(string[] a)
    {
        var args = a.Select(x => x.Trim()).ToList();
        var period = args.FirstOrDefault(x => DateLike.IsMatch(x)) ?? "";
        var rest = new List<string>(args);
        if (period.Length > 0) rest.Remove(period);
        var location = rest.FirstOrDefault(x => PlaceLike.IsMatch(ModernCv.Clean(x)));
        if (location is not null) rest.Remove(location);
        var org = rest.ElementAtOrDefault(0) ?? "";
        string role;
        if (location is null)
        {
            // Jake's order: {org}{place}{role}{dates}.
            location = rest.ElementAtOrDefault(1) ?? "";
            role = rest.ElementAtOrDefault(2) ?? "";
        }
        else role = rest.ElementAtOrDefault(1) ?? "";
        return [period, role, org, location, "", ""];
    }

    /// <summary>Before the parse, on the body only: entries, bullets and plain lists in place of the template's macros.</summary>
    public static string Mark(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? MarkBody(content) : content[..at] + MarkBody(content[at..]);
    }

    private static string MarkBody(string body)
    {
        body = ModernCv.Expand(body, "resumeSubheading", 4, a => ModernCv.EntryMarker(SubheadingAsEntry(a)), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "resumeSubSubheading", 2, a => ModernCv.EntryMarker([a[1], a[0], "", "", "", ""]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "resumeProjectHeading", 2, a => ModernCv.EntryMarker([a[1], a[0], "", "", "", ""]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "resumeProject", 4, a => ModernCv.EntryMarker([a[3], a[0], a[1], "", "", a[2]]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "resumePOR", 3, a => ModernCv.EntryMarker([a[2], a[0], a[1], "", "", ""]), argumentsAcrossLines: true);
        // \resumeItem has one argument in Jake's resume and two ({title}{text}) in some forks; \resumeSubItem wraps it.
        body = Regex.Replace(body, @"\\resumeSubItem\b", @"\resumeItem");
        body = ModernCv.Expand(body, "resumeItem", 2, a => $"\\item {(a[0].Length > 0 ? $"\\textbf{{{a[0]}}}: " : "")}{a[1]}\n");
        body = ModernCv.Expand(body, "resumeItem", 1, a => $"\\item {a[0]}\n");
        body = Regex.Replace(body, @"\\resume(Item|HeadingSkill)ListStart\b|\\resumeHeadingSkillStart\b", "\n\\begin{itemize}\n");
        body = Regex.Replace(body, @"\\resume(Item|HeadingSkill)ListEnd\b|\\resumeHeadingSkillEnd\b", "\n\\end{itemize}\n");
        body = Regex.Replace(body, @"\\resumeSubHeadingList(Start|End)\b", "\n");
        return body;
    }

    /// <summary>After the parse: CV entries and sections, no Personal information block (the header is plain text).</summary>
    public static void Build(ImportDocument document) => ModernCv.Build(document, withPerson: false);
}
