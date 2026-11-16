using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// A moderncv source imported as a CV (the launch bot's #112, 15 Nov 2026). The import left the name and contact details
/// blank (they were read into the metadata, then never used), printed <c>\makecvtitle</c> as text, and flattened
/// <c>\cventry</c> and moderncv's two-argument <c>\cvitem</c> into brace text such as
/// <c>1835--1838}{Mathematics}{University of London}…</c>.
///
/// <para>Two steps around the parse. <see cref="Mark"/>, on the source: each <c>\cventry</c> becomes a marker paragraph
/// carrying its six arguments, <c>\makecvtitle</c> a marker of its own, and the <c>\cvitem</c> family readable text.
/// <see cref="Build"/>, on the parsed document: the markers become a Personal information block and CV entries, each
/// <c>\section</c> a CV section, and the document is named after the person (moderncv's <c>\title</c> is their
/// headline, not the document's title).</para>
/// </summary>
public static class ModernCv
{
    // Letters only, so no text pass touches them; the arguments follow as hex.
    private const string EntryMark = "LILIACVENTRYX";

    /// <summary>The marker paragraph for one CV entry, in \cventry's argument order (years, title, employer, city, grade, description).</summary>
    internal static string EntryMarker(string[] cventryArgs) =>
        $"\n\n{EntryMark}{Convert.ToHexString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cventryArgs)))}\n\n";
    internal const string TitleMark = "LILIACVTITLEX";

    /// <summary>
    /// A moderncv source: its class, or its own commands used in the body. Only the body: Lilia's own LaTeX export
    /// defines \makecvtitle and \cventry in every preamble (LaTeXPreamble.CvShims), so an exported paper imported
    /// again is not a CV.
    /// </summary>
    public static bool Is(string content) =>
        Regex.IsMatch(content, @"\\documentclass\s*(\[[^\]]*\])?\s*\{moderncv\}")
        || Regex.IsMatch(Body(content), @"\\(makecvtitle|cventry)\b");

    /// <summary>What follows \begin{document}, or all of it when there is none (a pasted fragment).</summary>
    private static string Body(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at < 0 ? content : content[at..];
    }

    /// <summary>
    /// Before the parse, on the body only (the preamble's definitions stay as written): markers for <c>\cventry</c>
    /// and <c>\makecvtitle</c>, text for the <c>\cvitem</c> family.
    /// </summary>
    public static string Mark(string content)
    {
        var at = content.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var name = Regex.Match(content, @"\\name\s*\{([^{}]*)\}\s*\{([^{}]*)\}") is { Success: true } n ? $"{n.Groups[1].Value.Trim()} {n.Groups[2].Value.Trim()}".Trim() : "";
        return at < 0 ? MarkBody(Letter(content, name)) : content[..at] + MarkBody(Letter(content[at..], name));
    }

    /// <summary>
    /// moderncv's cover letter (the document sweep, D-018, 16 Nov 2026): \recipient, \opening, \closing, \enclosure and the
    /// body's \date are declarations that \makelettertitle and \makeletterclosing print; imported, those two were unknown
    /// commands that stopped the PDF. They become paragraphs where the letter prints them: the recipient, the date and the
    /// opening at the title, the closing, the person's name and the enclosure at the end. Lilia has no letter block.
    /// </summary>
    internal static string Letter(string body, string personName)
    {
        if (!Regex.IsMatch(body, @"\\makelettertitle\b")) return body;
        string[] recipient = [], opening = [], closing = [], date = [], enclosure = [];
        var enclosureLabel = Regex.Match(body, @"\\enclosure\s*\[([^\]]*)\]") is { Success: true } e ? e.Groups[1].Value.Trim() : "Enclosure";
        body = Expand(body, "recipient", 2, a => { recipient = a; return ""; });
        body = Expand(body, "opening", 1, a => { opening = a; return ""; });
        body = Expand(body, "closing", 1, a => { closing = a; return ""; });
        body = Expand(body, "enclosure", 1, a => { enclosure = a; return ""; });
        body = Expand(body, "date", 1, a => { date = a; return ""; });

        static string Para(string text) => string.IsNullOrWhiteSpace(text) ? "" : $"\n\n{text.Trim()}\n\n";
        static string Lines(string text) => string.Join(", ", text.Split(@"\\").Select(Clean).Where(l => l.Length > 0));
        var title = (recipient.Length == 2 ? Para($"**{Clean(recipient[0])}**") + Para(Lines(recipient[1])) : "")
                    + (date.Length == 1 ? Para(Clean(date[0])) : "")
                    + (opening.Length == 1 ? Para(Clean(opening[0])) : "");
        var end = (closing.Length == 1 ? Para(Clean(closing[0])) : "")
                  + Para(personName)
                  + (enclosure.Length == 1 ? Para($"{enclosureLabel}: {Clean(enclosure[0])}") : "");
        body = Regex.Replace(body, @"\\makelettertitle\b", title.Replace("$", "$$"));
        return Regex.IsMatch(body, @"\\makeletterclosing\b")
            ? Regex.Replace(body, @"\\makeletterclosing\b", end.Replace("$", "$$"))
            : body.Replace(@"\end{document}", end + @"\end{document}");
    }

    private static string MarkBody(string content)
    {
        content = Expand(content, "cventry", 6, a => $"\n\n{EntryMark}{Convert.ToHexString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(a)))}\n\n");
        // The skill matrix (moderncv 2.x; the document sweep, D-018, 16 Nov 2026): each \cvskillentry[*][spacing]{category}
        // {level 1-5}{skill}{years}{comment} is a CV entry (skill as the role, category as the organisation, years as the
        // period, the level as its highlight). It was flattened into brace text that stopped the PDF. Legends, headers and
        // column settings are layout only.
        content = Regex.Replace(content, @"\\cvskillentry\*", @"\cvskillentry");
        content = Expand(content, "cvskillentry", 5, a => $"\n\n{EntryMark}{Convert.ToHexString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(SkillAsEntry(a))))}\n\n");
        content = Regex.Replace(content, @"\\(?:cvskill(?:plain)?legend|cvskillhead)\*?(?:\s*\[[^\]]*\])*(?:\s*\{[^{}]*\})?", "");
        content = Regex.Replace(content, @"\\setcvskill(?:legend)?columns(?:\s*\[[^\]]*\])*", "");
        content = Regex.Replace(content, @"\\cvskill\s*\{\s*([1-5])\s*\}", "($1/5)");
        content = Expand(content, "cvitemwithcomment", 3, a => Paragraph(Labelled(a[0], a[2].Length > 0 ? $"{a[1]} ({a[2]})" : a[1])));
        content = Expand(content, "cvdoubleitem", 4, a => Paragraph(Labelled(a[0], a[1])) + Paragraph(Labelled(a[2], a[3])));
        content = Expand(content, "cvlistdoubleitem", 2, a => Paragraph(a[0]) + Paragraph(a[1]));
        content = Expand(content, "cvlistitem", 1, a => Paragraph(a[0]));
        content = Expand(content, "cvitem", 2, a => Paragraph(Labelled(a[0], a[1])));
        content = Regex.Replace(content, @"\\makecvtitle\b", $"\n\n{TitleMark}\n\n");
        // Style and colour choices are the class's, not content.
        content = Regex.Replace(content, @"\\(moderncvstyle|moderncvcolor|moderncvicons|moderncvtheme)\s*(\[[^\]]*\])?\s*\{[^}]*\}", "");
        return content;

        static string Labelled(string label, string text) => label.Length == 0 ? text : $"**{label}** — {text}";
        static string Paragraph(string text) => text.Length == 0 ? "" : $"\n\n{text}\n\n";
    }

    /// <summary>After the parse: CV blocks in place of the markers and sections, and the person's name on the document.</summary>
    public static void Build(ImportDocument document) => Build(document, withPerson: true);

    /// <summary>
    /// <paramref name="withPerson"/>: false for a "resume" template (ResumeTemplate), whose header is plain centred text
    /// with no \name to fill a Personal information block; its entries and sections are built the same way.
    /// </summary>
    internal static void Build(ImportDocument document, bool withPerson)
    {
        var meta = document.Metadata;
        var headline = document.Title?.Trim() ?? "";
        if (withPerson && !string.IsNullOrWhiteSpace(meta.PersonName)) document.Title = meta.PersonName!.Trim();

        var person = PersonalInfo(meta, headline);
        var result = new List<ImportElement>(document.Elements.Count + 1);
        var placed = false;
        foreach (var element in document.Elements)
        {
            switch (element)
            {
                case ImportParagraph p when p.Text.Trim() == TitleMark:
                    if (!placed && withPerson) result.Add(person);
                    placed = true;
                    continue;
                case ImportParagraph p when p.Text.Trim().StartsWith(EntryMark, StringComparison.Ordinal)
                                            && Entry(p.Text.Trim()[EntryMark.Length..]) is { } entry:
                    result.Add(entry);
                    continue;
                case ImportHeading { Level: 1 } h:
                    result.Add(new ImportCvBlock
                    {
                        BlockType = "cvSection",
                        Content = new() { ["title"] = h.Text.Trim() },
                        LatexCode = $"\\section{{{h.Text.Trim()}}}",
                    });
                    continue;
                default:
                    result.Add(element);
                    continue;
            }
        }
        if (!placed && withPerson) result.Insert(0, person);
        for (var i = 0; i < result.Count; i++) result[i].Order = i;
        document.Elements = result;
    }

    private static ImportCvBlock PersonalInfo(ImportMetadata meta, string headline) => new()
    {
        BlockType = "personalInfo",
        Content = new()
        {
            ["name"] = meta.PersonName?.Trim() ?? "",
            ["headline"] = Clean(headline),
            ["email"] = meta.Email?.Trim() ?? "",
            ["phones"] = meta.Phones.Select(p => new Dictionary<string, object?> { ["kind"] = p.Kind, ["number"] = p.Number }).ToList(),
            ["homepage"] = meta.Homepage?.Trim() ?? "",
            ["location"] = Clean(meta.Location ?? ""),
            ["socials"] = meta.Socials.Select(s => new Dictionary<string, object?> { ["network"] = s.Network, ["handle"] = s.Handle }).ToList(),
            ["extra"] = Clean(meta.ExtraInfo ?? ""),
        },
        LatexCode = @"\makecvtitle",
    };

    /// <summary>
    /// A \cvskillentry's five arguments (category, level, skill, years, comment) in \cventry's order (years, title,
    /// employer, city, grade, description), so the same entry builder reads them.
    /// </summary>
    internal static string[] SkillAsEntry(string[] a)
    {
        var category = a[0].Trim().TrimEnd(':').Trim();
        var level = int.TryParse(a[1].Trim(), out var n) && n is >= 1 and <= 5 ? $"Level {n} of 5" : a[1].Trim();
        var years = a[3].Trim();
        if (int.TryParse(years, out var y)) years = y == 1 ? "1 year" : $"{y} years";
        return [years, a[2], category, "", level, a[4]];
    }

    /// <summary>\cventry{years}{title}{employer or school}{city}{grade}{description}, from a marker's hex.</summary>
    private static ImportCvBlock? Entry(string hex)
    {
        string[]? a;
        try { a = JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(Convert.FromHexString(hex))); }
        catch (Exception) { return null; }
        if (a is not { Length: 6 }) return null;
        return new ImportCvBlock
        {
            BlockType = "cvEntry",
            Content = new()
            {
                ["period"] = Clean(a[0]),
                ["role"] = Clean(a[1]),
                ["org"] = Clean(a[2]),
                ["location"] = Clean(a[3]),
                ["highlight"] = Clean(a[4]),
                ["description"] = Clean(a[5]),
                ["tech"] = new List<string>(),
            },
            LatexCode = "\\cventry" + string.Concat(a.Select(x => "{" + x + "}")),
        };
    }

    /// <summary>The text of an argument: TeX dashes and ties as characters, <c>\\</c> as a space, spaces collapsed.</summary>
    internal static string Clean(string text)
    {
        var t = text.Replace("---", "—").Replace("--", "–").Replace("~", " ").Replace(@"\\", " ");
        t = Regex.Replace(t, @"\\(textbf|emph|textit)\{([^{}]*)\}", "$2");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>Each <c>\name[optional]{a}{b}…</c> with <paramref name="count"/> brace arguments, replaced by the formatter's text.</summary>
    internal static string Expand(string content, string name, int count, Func<string[], string> format, bool argumentsAcrossLines = false)
    {
        var pattern = new Regex(@"\\" + Regex.Escape(name) + @"(?![A-Za-z])\s*(\[[^\]]*\])?\s*\{");
        var from = 0;
        for (var guard = 0; guard < 2000; guard++)
        {
            var m = pattern.Match(content, from);
            if (!m.Success) break;
            var pos = m.Index + m.Length - 1;
            var args = new string[count];
            var ok = true;
            for (var i = 0; i < count; i++)
            {
                // Arguments sit on one line in moderncv; resume templates spread them over lines ({org}{place}, newline, {role}{dates}).
                while (pos < content.Length && i > 0 && char.IsWhiteSpace(content[pos]) && (argumentsAcrossLines || content[pos] != '\n')) pos++;
                if (pos >= content.Length || content[pos] != '{' || Close(content, pos) is not { } close) { ok = false; break; }
                args[i] = content.Substring(pos + 1, close - pos - 1).Trim();
                pos = close + 1;
            }
            if (!ok) { from = m.Index + 1; continue; }
            var text = format(args);
            content = content[..m.Index] + text + content[pos..];
            from = m.Index + text.Length;
        }
        return content;
    }

    /// <summary>The index of the brace closing the one at <paramref name="open"/>, skipping escaped braces.</summary>
    private static int? Close(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return null;
    }
}
