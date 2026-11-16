using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Twenty Seconds CV (twentysecondcv.cls) and its forks, imported as a CV (the document sweep, chunks 1 and 6: D-011,
/// V-011, 16 Nov 2026). Its \twentyitem came in as raw text ("…nt Machine Learning Engineer {}  {…") and the template
/// wasn't recognised as a CV.
///
/// <para>\twentyitem has four arguments in the original ({dates}{title}{place}{details}) and six in a common fork
/// ({from}{to}{title}{organisation}{place}{details}); each becomes a CV entry (moderncv's entry marker), its details
/// following as ordinary text so a list in them stays a list. \twentyitemshort{dates}{text} becomes a line; the
/// twenty environments and the rows' \\ go; \makeprofile places the Personal information block, filled from the
/// preamble's \cvname, \cvjobtitle, \cvmail, \cvnumberphone, \cvsite, \cvaddress, \cvlinkedin and \cvgithub.</para>
/// </summary>
public static class TwentySecondsCv
{
    public static bool Is(string content) =>
        Regex.IsMatch(content, @"(?m)^[^%\n]*\\documentclass\s*(\[[^\]]*\])?\s*\{twentysecondcv\}", RegexOptions.Singleline);

    public sealed record Header(IReadOnlyDictionary<string, string> Fields);

    public static Header ReadHeader(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var preamble = Regex.Replace(at < 0 ? content : content[..at], @"(?m)(?<!\\)%.*$", "");
        var fields = new Dictionary<string, string>();
        foreach (var name in new[] { "cvname", "cvjobtitle", "cvmail", "cvnumberphone", "cvsite", "cvaddress", "cvlinkedin", "cvgithub" })
        {
            var value = "";
            ModernCv.Expand(preamble, name, 1, a => { if (value.Length == 0) value = a[0]; return ""; });
            value = ModernCv.Clean(Regex.Replace(value, @"\\\\(\[[^\]]*\])?", " "));
            if (value.Length > 0) fields[name] = value;
        }
        return new Header(fields);
    }

    public static string Mark(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? MarkBody(content) : content[..at] + MarkBody(content[at..]);
    }

    private static string MarkBody(string body)
    {
        body = Regex.Replace(body, @"\\makeprofile\b", $"\n\n{ModernCv.TitleMark}\n\n");
        // The fork's six arguments first, then the original's four.
        body = ModernCv.Expand(body, "twentyitem", 6, a =>
            ModernCv.EntryMarker([$"{a[0]} {a[1]}".Trim(), a[2], a[3], a[4], "", ""]) + Details(a[5]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "twentyitem", 4, a => ModernCv.EntryMarker([a[0], a[1], a[2], "", "", ""]) + Details(a[3]), argumentsAcrossLines: true);
        body = ModernCv.Expand(body, "twentyitemshort", 2, a => $"\n\n\\textbf{{{a[0]}}} {a[1]}\n\n", argumentsAcrossLines: true);
        body = Regex.Replace(body, @"\\begin\{twenty(short)?\}|\\end\{twenty(short)?\}", "\n");
        // The rows' line breaks, left alone on their lines.
        body = Regex.Replace(body, @"(?m)^\s*\\\\\s*$", "");
        return body;
    }

    /// <summary>An entry's details as text after it, unwrapped from the extra braces forks put around a list.</summary>
    private static string Details(string details)
    {
        var d = details.Trim();
        while (d.StartsWith('{') && d.EndsWith('}')) d = d[1..^1].Trim();
        return d.Length == 0 ? "" : $"\n\n{d}\n\n";
    }

    public static void Build(ImportDocument document, Header header)
    {
        var meta = document.Metadata;
        var f = header.Fields;
        if (f.TryGetValue("cvname", out var name)) meta.PersonName = name;
        if (f.TryGetValue("cvmail", out var mail)) meta.Email = mail;
        if (f.TryGetValue("cvnumberphone", out var phone)) meta.Phones.Add(("mobile", phone));
        if (f.TryGetValue("cvsite", out var site)) meta.Homepage = site;
        if (f.TryGetValue("cvaddress", out var address)) meta.Location = address;
        if (f.TryGetValue("cvlinkedin", out var linkedin)) meta.Socials.Add(("linkedin", linkedin));
        if (f.TryGetValue("cvgithub", out var github)) meta.Socials.Add(("github", github));
        if (f.TryGetValue("cvjobtitle", out var job)) document.Title = job;
        ModernCv.Build(document, withPerson: true);
    }
}
