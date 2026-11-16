using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// The "simple hipster CV" template (simplehipstercv.cls, a mash-up of hipstercv, Friggeri and Twenty Seconds CV), imported
/// as a CV (the document sweep, D-015 and V-015, 16 Nov 2026). Its own macros came in as brace text
/// ("headercolour{Jack}{Sparrow}{Captain}{white}", "cvgreen{white}{About me}") and the template wasn't recognised as a CV.
///
/// <para>Before the parse (<see cref="Mark"/>), on the body only: \simpleheader marks the Personal information block; a
/// sidebar label <c>\bg{colour}{colour}{About me}\\</c> becomes a section, and \bg elsewhere (a skill in a table) its text;
/// \cvevent{dates}{title}{role}{place}{text}{logo} and \cvdegree{year}{degree}{grade}{place}{text}{logo} become CV entries,
/// taken out of the tabular that lays them out; the class's drawing and layout commands go (\infobubble, \barrule,
/// \pictofraction, \roundpic, \icon, \lorem, paracol). Its fields are \simpleheader's name and title and the
/// \infobubble contacts (<see cref="ReadHeader"/>, <see cref="Build"/>).</para>
/// </summary>
public static class HipsterCv
{
    /// <summary>A simple hipster CV source: its class.</summary>
    public static bool Is(string content) =>
        Regex.IsMatch(content, @"(?m)^[^%\n]*\\documentclass\s*(\[[^\]]*\])?\s*\{simplehipstercv\}", RegexOptions.Singleline);

    /// <summary>The header: \simpleheader's name and title, and the \infobubble contacts.</summary>
    public sealed record Header(string Name, string Title, string Email, string Phone, IReadOnlyList<(string Network, string Handle)> Socials);

    public static Header ReadHeader(string content)
    {
        var body = Regex.Replace(Body(content), @"(?m)(?<!\\)%.*$", "");
        string name = "", title = "", email = "", phone = "";
        ModernCv.Expand(body, "simpleheader", 5, a =>
        {
            if (name.Length == 0)
            {
                name = ModernCv.Clean($"{a[1]} {a[2]}");
                title = ModernCv.Clean(a[3]);
            }
            return "";
        });
        var socials = new List<(string, string)>();
        ModernCv.Expand(body, "infobubble", 4, a =>
        {
            var value = ModernCv.Clean(a[3]);
            if (value.Length == 0) return "";
            var icon = Regex.Match(a[0], @"\\fa([A-Za-z]+)") is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : "";
            switch (icon)
            {
                case "at" or "envelope" or "envelopeo":
                    if (email.Length == 0) email = value;
                    break;
                case "phone" or "mobile":
                    if (phone.Length == 0) phone = value;
                    break;
                case "":
                    break;
                default:
                    socials.Add((icon, value));
                    break;
            }
            return "";
        });
        return new Header(name, title, email, phone, socials);
    }

    /// <summary>Before the parse, on the body only.</summary>
    public static string Mark(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? MarkBody(content) : content[..at] + MarkBody(content[at..]);
    }

    private static readonly Regex CvTabular = new(
        @"\\begin\{tabular\}\s*\{(?:[^{}]|\{[^{}]*\})*\}(?<rows>(?:(?!\\end\{tabular\}).)*?\\cv(?:event|degree)\b.*?)\\end\{tabular\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static string MarkBody(string body)
    {
        body = ModernCv.Expand(body, "simpleheader", 5, _ => $"\n\n{ModernCv.TitleMark}\n\n");
        body = ModernCv.Expand(body, "infobubble", 4, _ => "");

        // The class's drawings and layout print nothing in Lilia's CV. First, so an entry's text doesn't keep them (\lorem).
        body = ModernCv.Expand(body, "pictofraction", 6, _ => "");
        body = ModernCv.Expand(body, "barrule", 3, _ => "");
        body = ModernCv.Expand(body, "icon", 3, _ => " ");
        body = ModernCv.Expand(body, "roundpic", 1, _ => "");
        body = Regex.Replace(body, @"\\columnratio\s*\{[^{}]*\}(\[[^\]]*\])?", "");
        body = Regex.Replace(body, @"\\begin\{paracol\}\s*\{[^{}]*\}|\\end\{paracol\}", "\n");
        body = Regex.Replace(body, @"\\(?:switchcolumn|paracolbackgroundoptions|setasidefontcolour|lorem)\b\*?", "");
        body = Regex.Replace(body, @"\\hbadness\s*=?\s*\d+", "");

        // A table laying out entries: the entries without the table (their row ends go with it).
        body = CvTabular.Replace(body, m => "\n\n" + Regex.Replace(m.Groups["rows"].Value, @"\\\\(\[[^\]]*\])?", "\n") + "\n\n");
        body = ModernCv.Expand(body, "cvevent", 6, a => ModernCv.EntryMarker([a[0], a[1], a[2], Place(a[3]), "", a[4]]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "cvdegree", 6, a => ModernCv.EntryMarker([a[0], a[1], Place(a[3]), "", a[2], a[4]]), argumentsAcrossLines: true);

        // A sidebar label (\bg{…}{…}{About me}\\[0.5em]) is a section; \bg anywhere else (a skill in a table) is its text.
        body = Regex.Replace(body, @"\\bg\s*\{[^{}]*\}\s*\{[^{}]*\}\s*\{([^{}]*)\}\s*\\\\(\[[^\]]*\])?", m => $"\n\n\\section{{{m.Groups[1].Value.Trim()}}}\n\n");
        body = ModernCv.Expand(body, "bg", 3, a => a[2]);

        return body;
    }

    /// <summary>A place without the colour the template gives it ("East Indies \color{cvred}").</summary>
    private static string Place(string place) => Regex.Replace(place, @"\\color\s*\{[^{}]*\}", "").Trim();

    private static string Body(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? content : content[at..];
    }

    /// <summary>After the parse: the header fills the Personal information block, then entries and sections.</summary>
    public static void Build(ImportDocument document, Header header)
    {
        var meta = document.Metadata;
        if (header.Name.Length > 0) meta.PersonName = header.Name;
        if (header.Email.Length > 0) meta.Email = header.Email;
        if (header.Phone.Length > 0) meta.Phones.Add(("mobile", header.Phone));
        if (header.Socials.Count > 0) { meta.Socials.Clear(); meta.Socials.AddRange(header.Socials); }
        if (header.Title.Length > 0) document.Title = header.Title;
        ModernCv.Build(document, withPerson: true);
    }
}
