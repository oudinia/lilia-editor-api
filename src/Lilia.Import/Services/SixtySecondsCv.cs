using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// SixtySecondsCV (sixtysecondscv.cls), imported as a CV (the document sweep, chunks 1 and 6: D-020, V-020, 16 Nov 2026).
/// Its four-argument \cvitem came in as raw text ("…2009 -- 2010}{Post-Doc Panda Studies}{Panda …") and the template
/// wasn't recognised as a CV.
///
/// <para>Before the parse (<see cref="Mark"/>), on the body only: \cvsection becomes a section and \cvsubsection an
/// unnumbered subsection; \cvitem{dates}{title}{organisation}{details} a CV entry (moderncv's entry marker, so
/// <see cref="ModernCv.Build(ImportDocument, bool)"/> builds it); \cvpubitem{title}{authors}{journal}{year} an entry;
/// \cvitemshort{label}{text} a line "label: text"; the cvtable environment and the back sidebar go; \makefrontsidebar
/// marks the Personal information block. Its fields are the preamble's \cvname, \cvjobtitle, \cvmail, \cvphone,
/// \cvsite and \cvaddress (<see cref="Build"/>).</para>
/// </summary>
public static class SixtySecondsCv
{
    /// <summary>A SixtySecondsCV source: its class (options may run over many lines).</summary>
    public static bool Is(string content) =>
        Regex.IsMatch(content, @"(?m)^[^%\n]*\\documentclass\s*(\[[^\]]*\])?\s*\{sixtysecondscv\}", RegexOptions.Singleline);

    /// <summary>The header fields, from the preamble.</summary>
    public sealed record Header(string Name, string JobTitle, string Email, string Phone, string Site, string Address)
    {
        /// <summary>The sidebar's \social{icon}{link}{text}: (network, link).</summary>
        public IReadOnlyList<(string Network, string Link)> Socials { get; init; } = [];
    }

    public static Header ReadHeader(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var preamble = Regex.Replace(at < 0 ? content : content[..at], @"(?m)(?<!\\)%.*$", "");
        string Field(string name)
        {
            var value = "";
            ModernCv.Expand(preamble, name, 1, a => { if (value.Length == 0) value = a[0]; return ""; });
            return ModernCv.Clean(Regex.Replace(value, @"\\\\(\[[^\]]*\])?", " "));
        }
        // \social{\faGithub}{https://…}{text}: the icon names the network. The generic \social[network]{handle} reading
        // took the icon as a "web" handle ("web: \aiOverleafSquare"; the launch bot's #119).
        var socials = new List<(string, string)>();
        ModernCv.Expand(preamble, "social", 3, a =>
        {
            var icon = Regex.Match(a[0], @"\\(?:fa|ai)([A-Za-z]+?)(?:Square|Alt)?\b");
            var network = icon.Success ? icon.Groups[1].Value.ToLowerInvariant() : "web";
            if (a[1].Trim().Length > 0) socials.Add((network, a[1].Trim()));
            return "";
        }, argumentsAcrossLines: true);
        return new Header(Field("cvname"), Field("cvjobtitle"), Field("cvmail"), Field("cvphone"), Field("cvsite"), Field("cvaddress")) { Socials = socials };
    }

    /// <summary>Before the parse, on the body only.</summary>
    public static string Mark(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? MarkBody(content) : content[..at] + MarkBody(content[at..]);
    }

    private static string MarkBody(string body)
    {
        body = Regex.Replace(body, @"\\makefrontsidebar\b", $"\n\n{ModernCv.TitleMark}\n\n");
        // The back sidebar, the signature and the watermark print nothing in Lilia's CV: they go (\cvsignature stopped the PDF).
        body = Regex.Replace(body, @"\\(?:makebacksidebar|cvsignature|watermark)\b", "");
        body = Regex.Replace(body, @"\\cvsection\*?\s*\{", @"\section{");
        // Unnumbered, as the template prints it: numbered, "Postgraduate Training" printed as "0.1" under a CV section
        // (which numbers nothing), and the check warned of a subsection before the first section (the launch bot's #119).
        body = Regex.Replace(body, @"\\cvsubsection\*?\s*\{", @"\subsection*{");
        body = ModernCv.Expand(body, "cvpubitem", 4, a => ModernCv.EntryMarker([a[3], a[0], a[2], "", "", a[1]]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "cvitemshort", 2, a => $"\n\n\\textbf{{{a[0]}}}: {a[1]}\n\n", argumentsAcrossLines: true);
        // \cvitem{dates}{title}{organisation}{details}, the details often on the next line.
        body = ModernCv.Expand(body, "cvitem", 4, a => ModernCv.EntryMarker([a[0], a[1], a[2], "", "", a[3]]), argumentsAcrossLines: true);
        body = Regex.Replace(body, @"\\begin\{cvtable\}\s*(\[[^\]]*\])?|\\end\{cvtable\}", "\n");
        return body;
    }

    /// <summary>After the parse: the preamble's fields fill the Personal information block, then entries and sections.</summary>
    public static void Build(ImportDocument document, Header header)
    {
        var meta = document.Metadata;
        if (header.Name.Length > 0) meta.PersonName = header.Name;
        if (header.Email.Length > 0) meta.Email = header.Email;
        if (header.Phone.Length > 0) meta.Phones.Add(("mobile", header.Phone));
        if (header.Site.Length > 0) meta.Homepage = header.Site;
        if (header.Address.Length > 0) meta.Location = header.Address;
        // The sidebar's own links, in place of what the generic \social reading made of them.
        if (header.Socials.Count > 0) { meta.Socials.Clear(); meta.Socials.AddRange(header.Socials); }
        if (header.JobTitle.Length > 0) document.Title = header.JobTitle;
        ModernCv.Build(document, withPerson: true);
    }
}
