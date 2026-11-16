using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// AltaCV (altacv.cls, and its fork maltacv), one of the most used CV templates on Overleaf, imported as a CV (the document
/// sweep, chunks 1 and 6: D-009, D-016, V-009, V-016, 16 Nov 2026). Its macros came in as raw text: "\personalinfo{ …",
/// "1.5cm{0.5cm}{ 6/8em/accent!30/{Sleep, …" (a \wheelchart), "English{5}" (a \cvskill), "Fantastic Achievement{and some
/// details}" (a \cvachievement), and it wasn't recognised as a CV at all.
///
/// <para>Before the parse (<see cref="Mark"/>), on the body only: \cvsection becomes a section; \cvevent, \cvexperience
/// and \cvuniversity become CV entries (moderncv's entry marker, so <see cref="ModernCv.Build(ImportDocument, bool)"/>
/// builds them); achievements, skills with their rating and referees become entries too; a run of \cvtag or \cvlistitem
/// becomes one line; a \wheelchart becomes a list; the header (\name, \tagline, \personalinfo, \makecvheader) becomes the
/// Personal information block; layout (\divider, paracol, \columnratio, \mynames) goes. After the parse
/// (<see cref="Build"/>), the header's name, tagline, email, phone, place, homepage and profiles fill that block.</para>
/// </summary>
public static class AltaCv
{
    /// <summary>An AltaCV source: its class (altacv or maltacv), or its header and sections used in the body.</summary>
    public static bool Is(string content) =>
        Regex.IsMatch(content, @"(?m)^[^%\n]*\\documentclass\s*(\[[^\]]*\])?\s*\{m?altacv\}")
        || (Regex.IsMatch(Body(content), @"\\makecvheader\b") && Regex.IsMatch(Body(content), @"\\cv(section|event)\b"));

    private static string Body(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? content : content[at..];
    }

    /// <summary>What the header says: the person's name, tagline and \personalinfo fields, for <see cref="Build"/>.</summary>
    public sealed record Header(string Name, string Tagline, IReadOnlyList<(string Field, string Value)> Fields);

    /// <summary>The header, read from the body before <see cref="Mark"/> removes it.</summary>
    public static Header ReadHeader(string content)
    {
        var body = Body(content);
        var name = Arg(body, "name");
        var tagline = Arg(body, "tagline");
        var fields = new List<(string, string)>();
        ModernCv.Expand(body, "personalinfo", 1, a =>
        {
            var info = Regex.Replace(a[0], @"(?m)%.*$", "");
            foreach (Match m in Regex.Matches(info, @"\\([A-Za-z]+)\*?\s*\{([^{}]*)\}"))
                if (m.Groups[1].Value is not ("NewInfoField" or "printinfo"))
                    fields.Add((m.Groups[1].Value, m.Groups[2].Value.Trim()));
            return "";
        }, argumentsAcrossLines: true);
        return new Header(ModernCv.Clean(name), ModernCv.Clean(tagline), fields);
    }

    private static string Arg(string body, string command)
    {
        var value = "";
        ModernCv.Expand(Regex.Replace(body, @"(?m)%.*$", ""), command, 1, a => { if (value.Length == 0) value = a[0]; return ""; });
        return value;
    }

    /// <summary>Before the parse, on the body only: entries, sections, lists and the header marker in place of AltaCV's macros.</summary>
    public static string Mark(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? MarkBody(content) : content[..at] + MarkBody(content[at..]);
    }

    private static string MarkBody(string body)
    {
        // The header: read by ReadHeader; here it goes, and \makecvheader marks where the Personal information block sits.
        body = ModernCv.Expand(body, "personalinfo", 1, _ => "", argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "name", 1, _ => "");
        body = ModernCv.Expand(body, "tagline", 1, _ => "");
        body = Regex.Replace(body, @"\\photo[LR]?\s*(\[[^\]]*\])?\s*\{[^{}]*\}\s*\{[^{}]*\}", "");
        body = Regex.Replace(body, @"\\makecvheader\b", $"\n\n{ModernCv.TitleMark}\n\n");

        body = Regex.Replace(body, @"\\cvsection\*?\s*(\[[^\]]*\])?\s*\{", @"\section{");
        body = Regex.Replace(body, @"\\cvsubsection\*?\s*\{", @"\subsection{");
        // \cvevent{title}{organisation}{dates}{place}; maltacv's \cvexperience adds a fifth, its tags.
        body = ModernCv.Expand(body, "cvexperience", 5, a => ModernCv.EntryMarker([a[2], a[0], a[1], a[3], "", Tags(a[4])]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "cvevent", 4, a => ModernCv.EntryMarker([a[2], a[0], a[1], a[3], "", ""]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "cvuniversity", 4, a => ModernCv.EntryMarker([a[2], a[0], a[1], a[3], "", ""]), argumentsAcrossLines: true);
        // \cvachievement{icon}{title}{details}; \cvskill{name}{rating out of 5}; \cvref{name}{institution}{email}{address}.
        body = ModernCv.Expand(body, "cvachievement", 3, a => ModernCv.EntryMarker(["", a[1], "", "", "", a[2]]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "cvskill", 2, a => ModernCv.EntryMarker(["", a[0], "", "", $"{a[1]}/5", ""]));
        body = ModernCv.Expand(body, "cvref", 4, a => ModernCv.EntryMarker(["", a[0], a[1], "", "", $"{a[2]} {a[3]}".Trim()]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "cvref", 3, a => ModernCv.EntryMarker(["", a[0], a[1], "", "", a[2]]), argumentsAcrossLines: true);
        // A run of tags, or of maltacv's \cvlistitem{text}{}, is one line.
        body = Regex.Replace(body, @"(?:\\cvtag\s*\{[^{}]*\}(?:\s*\\\\)?\s*)+", m =>
            string.Join(" · ", Regex.Matches(m.Value, @"\\cvtag\s*\{([^{}]*)\}").Select(t => t.Groups[1].Value.Trim())) + "\n\n");
        body = Regex.Replace(body, @"(?:\\cvlistitem\s*\{[^{}]*\}\s*\{[^{}]*\}\s*(?:%[^\n]*\n\s*)*)+", m =>
            string.Join(" · ", Regex.Matches(m.Value, @"\\cvlistitem\s*\{([^{}]*)\}").Select(t => t.Groups[1].Value.Trim())) + "\n\n");
        body = ModernCv.Expand(body, "wheelchart", 3, a => Wheel(a[2]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "printinfo", 2, a => a[1]);
        // Layout and bibliography plumbing.
        body = Regex.Replace(body, @"\\divider\b|\\columnratio\s*\{[^{}]*\}|\\begin\{paracol\}\s*\{\d+\}|\\end\{paracol\}|\\switchcolumn\b\*?|\\nocite\s*\{[^{}]*\}", "");
        body = ModernCv.Expand(body, "mynames", 1, _ => "", argumentsAcrossLines: true);
        return body;
    }

    /// <summary>A \wheelchart's slices ("6/8em/accent!30/{Sleep,\\beautiful sleep}, …") as a list: each slice's text and value.</summary>
    private static string Wheel(string slices)
    {
        var items = new List<string>();
        foreach (var slice in TopLevel(slices, ','))
        {
            var parts = slice.Split('/', 4);
            if (parts.Length < 4) continue;
            var text = ModernCv.Clean(parts[3].Trim().TrimStart('{').TrimEnd('}'));
            if (text.Length > 0) items.Add($"\\item {text} ({parts[0].Trim()})");
        }
        return items.Count == 0 ? "" : "\n\\begin{itemize}\n" + string.Join("\n", items) + "\n\\end{itemize}\n";
    }

    private static string Tags(string tags) => string.Join(" · ", tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0));

    /// <summary>The pieces of <paramref name="s"/> split at <paramref name="sep"/> outside braces.</summary>
    private static IEnumerable<string> TopLevel(string s, char sep)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
            else if (s[i] == sep && depth == 0) { yield return s[start..i]; start = i + 1; }
        }
        if (start < s.Length) yield return s[start..];
    }

    /// <summary>After the parse: the header fills the Personal information block, then CV entries and sections as moderncv's.</summary>
    public static void Build(ImportDocument document, Header header)
    {
        var meta = document.Metadata;
        if (header.Name.Length > 0) meta.PersonName = header.Name;
        foreach (var (field, value) in header.Fields)
        {
            var v = ModernCv.Clean(value);
            if (v.Length == 0) continue;
            switch (field)
            {
                case "email": meta.Email ??= v; break;
                case "phone": meta.Phones.Add(("mobile", v)); break;
                case "homepage": meta.Homepage ??= v; break;
                case "location": meta.Location = v; break;
                case "mailaddress": meta.Location ??= v; break;
                case "xtwitter": meta.Socials.Add(("twitter", v)); break;
                default:
                    if (field is "linkedin" or "github" or "gitlab" or "twitter" or "orcid" or "mastodon" or "stackoverflow" or "researchgate" or "googlescholar")
                        meta.Socials.Add((field, v));
                    break;
            }
        }
        // moderncv's Build takes the headline from the title, then sets the title to the person's name.
        if (header.Tagline.Length > 0) document.Title = header.Tagline;
        ModernCv.Build(document, withPerson: true);
    }
}
