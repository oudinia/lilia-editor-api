using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// A publisher paper's front matter, read back into Lilia's fields on import (publisher templates,
/// Overleaf import, 25 Oct 2026): the authors with their affiliation lines and emails, the keywords,
/// and for ACM the CCS concepts and the rights block. Each class writes these its own way; this is the
/// inverse of what Lilia writes for it (PublisherClasses.FrontMatter), so a paper written in Overleaf
/// for ACM, IEEE, Elsevier, Springer, APS/AIP, AMS, IOP or SIAM comes in with them, not stripped.
///
/// <para>The author text is Lilia's Title block form: authors separated by <c>\and</c>, each name
/// followed by its lines (institution, then country last; an email line) separated by <c>\\</c>.</para>
/// </summary>
public static partial class PublisherFrontMatterReader
{
    public sealed record Author(string Name, List<string> Lines);

    public sealed record Result(IReadOnlyList<Author> Authors, IReadOnlyList<string> Keywords, string? AcmCcs, string? AcmRights)
    {
        /// <summary>The Title block's author text.</summary>
        public string AuthorText => string.Join(" \\and ", Authors.Select(a => string.Join("\\\\", new[] { a.Name }.Concat(a.Lines))));
    }

    /// <summary>The front matter of <paramref name="latex"/> for <paramref name="documentClass"/>, or null for a class this doesn't read.</summary>
    public static Result? Read(string latex, string? documentClass)
    {
        var src = StripComments(latex);
        var cls = (documentClass ?? "").Trim();
        var authors = cls switch
        {
            "acmart" => Sequential(src, "affiliation", AcmAffiliation),
            "revtex4-2" or "revtex4-1" => Sequential(src, "affiliation", Plain),
            "amsart" or "amsproc" or "amsbook" => Sequential(src, "address", Plain),
            "IEEEtran" => Ieee(src),
            "elsarticle" => Elsevier(src),
            "llncs" => Llncs(src),
            "iopjournal" => Iop(src),
            _ when cls.StartsWith("siamart", StringComparison.Ordinal) || cls.StartsWith("siamonline", StringComparison.Ordinal) => Siam(src),
            _ => null,
        };
        if (authors is null) return null;
        return new Result(authors, Keywords(src), cls == "acmart" ? Ccs(src) : null, cls == "acmart" ? Rights(src) : null);
    }

    // ── authors ────────────────────────────────────────────────────────

    /// <summary>\author{Name} then its \affiliation / \address / \email, in order (ACM, REVTeX, AMS).</summary>
    private static List<Author> Sequential(string src, string affiliationCommand, Func<string, IEnumerable<string>> affiliationLines)
    {
        var authors = new List<Author>();
        var front = FrontPart(src);
        foreach (var (cmd, arg) in Commands(front, "author", affiliationCommand, "email"))
        {
            if (cmd == "author") { authors.Add(new Author(Clean(arg), [])); continue; }
            if (authors.Count == 0) continue;
            if (cmd == "email") authors[^1].Lines.AddRange(Split(arg, ',').Select(Clean).Where(e => e.Contains('@')));
            else authors[^1].Lines.AddRange(affiliationLines(arg));
        }
        // A country (ACM) or an email reads last; keep emails after the affiliation lines.
        foreach (var a in authors)
        {
            var emails = a.Lines.Where(l => l.Contains('@')).ToList();
            a.Lines.RemoveAll(l => l.Contains('@'));
            a.Lines.AddRange(emails);
        }
        return authors.Where(a => a.Name.Length > 0).ToList();
    }

    /// <summary>ACM's \affiliation{\institution{…}\department{…}\city{…}\country{…}}: institution (with department), then country.</summary>
    private static IEnumerable<string> AcmAffiliation(string arg)
    {
        var inst = string.Join(", ", new[] { "department", "institution" }.SelectMany(c => Commands(arg, c).Select(x => Clean(x.Arg))).Where(s => s.Length > 0));
        var country = Commands(arg, "country").Select(x => Clean(x.Arg)).FirstOrDefault(s => s.Length > 0);
        if (inst.Length == 0 && country is null) { var plain = Clean(arg); if (plain.Length > 0) yield return plain; yield break; }
        if (inst.Length > 0) yield return inst;
        if (country is not null) yield return country;
    }

    private static IEnumerable<string> Plain(string arg) => Split(arg, "\\\\").Select(Clean).Where(s => s.Length > 0);

    /// <summary>IEEE: \author{\IEEEauthorblockN{Name}\IEEEauthorblockA{line\\line} \and …}.</summary>
    private static List<Author> Ieee(string src)
    {
        var author = Commands(FrontPart(src), "author").Select(x => x.Arg).FirstOrDefault();
        if (author is null) return [];
        var result = new List<Author>();
        foreach (var part in SplitAnd(author))
        {
            var name = Commands(part, "IEEEauthorblockN").Select(x => Clean(IeeeOrdinal().Replace(x.Arg, ""))).FirstOrDefault() ?? Clean(part);
            var lines = Commands(part, "IEEEauthorblockA").SelectMany(x => Plain(x.Arg)).ToList();
            if (name.Length > 0) result.Add(new Author(name, lines));
        }
        return result;
    }

    /// <summary>Elsevier: \author[1,2]{Name} and \affiliation[1]{organization={…}, country={…}} (or \address[1]{…}).</summary>
    private static List<Author> Elsevier(string src)
    {
        var labelled = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unlabelled = new List<string>();
        var authors = new List<(string Name, string[] Labels, List<string> Emails)>();
        foreach (Match m in ElsevierCommand().Matches(src))
        {
            var cmd = m.Groups["cmd"].Value;
            var labels = m.Groups["opt"].Success ? m.Groups["opt"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
            var arg = Balanced(src, m.Index + m.Length - 1);
            if (arg is null) continue;
            if (cmd == "author") authors.Add((Clean(Regex.Replace(arg, @"\\(?:corref|fnref|ead)\{[^}]*\}", "")), labels, []));
            // \ead{…} follows its author (\ead[url]{…} is a home page, not an email).
            else if (cmd == "ead") { if (authors.Count > 0 && !labels.Contains("url")) authors[^1].Emails.Add(Clean(arg)); }
            else
            {
                var lines = ElsevierAffiliation(arg).ToList();
                if (labels.Length == 0) unlabelled.AddRange(lines);
                foreach (var l in labels) labelled[l] = lines;
            }
        }
        return authors.Where(a => a.Name.Length > 0).Select(a => new Author(a.Name,
            (a.Labels.Length > 0 ? a.Labels.SelectMany(l => labelled.GetValueOrDefault(l) ?? []).ToList() : [.. unlabelled])
                .Concat(a.Emails).ToList())).ToList();
    }

    private static IEnumerable<string> ElsevierAffiliation(string arg)
    {
        if (!arg.Contains('=')) { foreach (var l in Plain(arg)) yield return l; yield break; }
        string? Field(string key) => Regex.Match(arg, key + @"\s*=\s*\{([^{}]*)\}") is { Success: true } f ? Clean(f.Groups[1].Value) : null;
        var inst = string.Join(", ", new[] { Field("organization"), Field("addressline"), Field("city") }.Where(s => !string.IsNullOrEmpty(s)));
        if (inst.Length > 0) yield return inst;
        if (Field("country") is { Length: > 0 } c) yield return c;
    }

    /// <summary>
    /// Springer LNCS: \author{A\inst{1} \and B\inst{2}} with \institute{X \and Y}. Emails sit in the institutes
    /// (\email{a@x}, or one for several: \email{\{a,b\}@x}); each goes to the institute's author whose surname
    /// it carries, or else to its authors in order.
    /// </summary>
    private static List<Author> Llncs(string src)
    {
        var author = Commands(FrontPart(src), "author").Select(x => x.Arg).FirstOrDefault();
        if (author is null) return [];
        var instituteParts = Commands(src, "institute").Select(x => SplitAnd(x.Arg).ToList()).FirstOrDefault() ?? [];
        var institutes = instituteParts.Select(i => string.Join(", ", Plain(Regex.Replace(i, @"\\email\{(?:[^{}]|\{[^{}]*\})*\}|\\url\{[^}]*\}", "")))).ToList();
        var instituteEmails = instituteParts.Select(i => Commands(i, "email").SelectMany(e => EmailAddresses(e.Arg)).ToList()).ToList();
        var authors = SplitAnd(author).Select(part =>
        {
            var numbers = Regex.Matches(part, @"\\inst\{([^}]*)\}").SelectMany(m => m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries))
                .Select(n => int.TryParse(n, out var i) ? i : 0).Where(i => i > 0 && i <= institutes.Count).ToList();
            if (numbers.Count == 0 && institutes.Count == 1) numbers = [1];
            var name = Clean(Regex.Replace(part, @"\\inst\{[^}]*\}|\\orcidID\{[^}]*\}", ""));
            return (Author: new Author(name, numbers.Select(i => institutes[i - 1]).ToList()), Numbers: numbers);
        }).Where(a => a.Author.Name.Length > 0).ToList();

        var emailOf = new Dictionary<Author, string>();
        for (var inst = 1; inst <= instituteEmails.Count; inst++)
        {
            var members = authors.Where(a => a.Numbers.Contains(inst)).Select(a => a.Author).ToList();
            var left = new List<string>();
            foreach (var email in instituteEmails[inst - 1])
            {
                var local = Fold(email.Split('@')[0]);
                var owner = members.FirstOrDefault(m => !emailOf.ContainsKey(m) && Fold(m.Name.Split(' ').Last()) is { Length: > 1 } surname && local.Contains(surname));
                if (owner is not null) emailOf[owner] = email; else left.Add(email);
            }
            foreach (var (member, email) in members.Where(m => !emailOf.ContainsKey(m)).Zip(left)) emailOf[member] = email;
        }
        foreach (var (a, email) in emailOf) a.Lines.Add(email);
        return authors.Select(a => a.Author).ToList();
    }

    /// <summary>The addresses in an <c>\email</c>: <c>\{a,b\}@x</c> → a@x, b@x; otherwise as listed.</summary>
    private static IEnumerable<string> EmailAddresses(string arg)
    {
        if (Regex.Match(arg, @"\\?\{([^{}]*?)\\?\}\s*@\s*(\S+)") is { Success: true } shared)
            return shared.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(local => $"{local}@{shared.Groups[2].Value.Trim()}");
        return Regex.Split(arg, @"[,;\s]+").Select(Clean).Where(e => e.Contains('@'));
    }

    /// <summary>Lower case letters only, accents off: "Novák" → "novak", "p.vanderberg" → "pvanderberg".</summary>
    private static string Fold(string s) =>
        new string(s.Normalize(NormalizationForm.FormD).Where(char.IsAsciiLetter).Select(char.ToLowerInvariant).ToArray());

    /// <summary>IOP: \author{A$^1$, B$^2$ and C$^{1,*}$} with \affil{$^1$Inst}.</summary>
    private static List<Author> Iop(string src)
    {
        var author = Commands(src, "author").Select(x => x.Arg).FirstOrDefault();
        if (author is null) return [];
        var affils = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, arg) in Commands(src, "affil"))
            if (Regex.Match(arg, @"^\s*\$\^\{?([^}$]*)\}?\$\s*(.*)$", RegexOptions.Singleline) is { Success: true } m)
                foreach (var key in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries)) affils[key] = Clean(m.Groups[2].Value);
        var names = Regex.Split(Regex.Replace(author, @"\\orcid\{[^}]*\}", ""), @",\s*|\s+and\s+");
        return names.Select(n =>
        {
            var marks = Regex.Match(n, @"\$\^\{?([^}$]*)\}?\$") is { Success: true } m ? m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries) : [];
            var name = Clean(Regex.Replace(n, @"\$\^\{?[^}$]*\}?\$", ""));
            return new Author(name, marks.Where(affils.ContainsKey).Select(k => affils[k]).Distinct().ToList());
        }).Where(a => a.Name.Length > 0).ToList();
    }

    /// <summary>SIAM: \author{Name\thanks{Affiliation (\email{x}).} \and …}.</summary>
    private static List<Author> Siam(string src)
    {
        var author = Commands(FrontPart(src), "author").Select(x => x.Arg).FirstOrDefault();
        if (author is null) return [];
        return SplitAnd(author).Select(part =>
        {
            var thanks = Commands(part, "thanks").Select(x => x.Arg).ToList();
            var name = Clean(RemoveCommand(part, "thanks"));
            var lines = new List<string>();
            foreach (var t in thanks)
            {
                var emails = Commands(t, "email").Select(x => Clean(x.Arg)).ToList();
                var text = Clean(Regex.Replace(RemoveCommand(RemoveCommand(t, "email"), "url"), @"\(\s*[,;]?\s*\)", "")).Trim(' ', '.', ',');
                if (text.Length > 0) lines.Add(text);
                lines.AddRange(emails);
            }
            return new Author(name, lines);
        }).Where(a => a.Name.Length > 0).ToList();
    }

    // ── keywords, CCS, rights ──────────────────────────────────────────

    private static IReadOnlyList<string> Keywords(string src)
    {
        var raw = Commands(src, "keywords").Select(x => x.Arg).FirstOrDefault()
            ?? Environment(src, "IEEEkeywords") ?? Environment(src, "keyword") ?? Environment(src, "keywords");
        if (raw is null) return [];
        // Elsevier's keyword environment ends with classification codes (\MSC[2020] 70J30 \sep 78A60, \PACS, \JEL):
        // not keywords.
        raw = Regex.Replace(raw, @"\\(?:MSC|PACS|JEL)\b.*", "", RegexOptions.Singleline);
        return Regex.Split(raw, @"\\sep|\\and|[,;]").Select(Clean).Where(k => k.Length > 0).ToList();
    }

    private static string? Ccs(string src)
    {
        var sb = new StringBuilder();
        if (Regex.Match(src, @"\\begin\{CCSXML\}.*?\\end\{CCSXML\}", RegexOptions.Singleline) is { Success: true } xml) sb.AppendLine(xml.Value);
        foreach (Match m in Regex.Matches(src, @"\\ccsdesc(?:\[\d+\])?\{[^{}]*\}")) sb.AppendLine(m.Value);
        return sb.Length > 0 ? sb.ToString().Trim() : null;
    }

    private static readonly string[] RightsCommands =
        ["setcopyright", "acmDOI", "acmISBN", "acmConference", "acmBooktitle", "acmYear", "copyrightyear", "acmPrice",
         "acmJournal", "acmVolume", "acmNumber", "acmArticle", "acmArticleSeq", "acmMonth", "acmSubmissionID", "received", "acmPubDate"];

    private static string? Rights(string src)
    {
        var lines = new List<string>();
        foreach (Match m in Regex.Matches(src, @"\\(?<cmd>" + string.Join("|", RightsCommands) + @")\b"))
        {
            var end = m.Index + m.Length;
            var full = new StringBuilder(m.Value);
            while (true)
            {
                var i = end;
                while (i < src.Length && char.IsWhiteSpace(src[i]) && src[i] != '\n') i++;
                if (i >= src.Length || src[i] is not ('[' or '{')) break;
                var close = src[i] == '[' ? MatchingBracket(src, i) : MatchingBrace(src, i);
                if (close < 0) break;
                full.Append(src, i, close - i + 1);
                end = close + 1;
            }
            lines.Add(full.ToString());
        }
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    // ── helpers ────────────────────────────────────────────────────────

    /// <summary>Up to \maketitle (or the abstract), where the front matter lives; the whole text without either.</summary>
    private static string FrontPart(string src)
    {
        var cut = new[] { src.IndexOf(@"\maketitle", StringComparison.Ordinal), src.IndexOf(@"\begin{abstract}", StringComparison.Ordinal) }
            .Where(i => i >= 0).DefaultIfEmpty(src.Length).Min();
        return src[..cut];
    }

    /// <summary>Each occurrence of the named commands with its (last) brace argument, in order. Optional [..] skipped.</summary>
    private static IEnumerable<(string Cmd, string Arg)> Commands(string src, params string[] names)
    {
        foreach (Match m in Regex.Matches(src, @"\\(?<cmd>" + string.Join("|", names.Select(Regex.Escape)) + @")\*?(?![A-Za-z@])"))
        {
            var i = m.Index + m.Length;
            while (i < src.Length && char.IsWhiteSpace(src[i])) i++;
            while (i < src.Length && src[i] == '[') { var c = MatchingBracket(src, i); if (c < 0) break; i = c + 1; while (i < src.Length && char.IsWhiteSpace(src[i])) i++; }
            if (i >= src.Length || src[i] != '{') continue;
            var close = MatchingBrace(src, i);
            if (close < 0) continue;
            yield return (m.Groups["cmd"].Value, src[(i + 1)..close]);
        }
    }

    private static string? Balanced(string src, int open)
    {
        if (open >= src.Length || src[open] != '{') return null;
        var close = MatchingBrace(src, open);
        return close < 0 ? null : src[(open + 1)..close];
    }

    private static string? Environment(string src, string name) =>
        Regex.Match(src, @"\\begin\{" + name + @"\}(.*?)\\end\{" + name + @"\}", RegexOptions.Singleline) is { Success: true } m ? m.Groups[1].Value : null;

    private static int MatchingBrace(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    private static int MatchingBracket(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') { var c = MatchingBrace(s, i); if (c < 0) return -1; i = c; continue; }
            if (s[i] == '[') depth++;
            else if (s[i] == ']' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Split at a top-level \and (not one inside braces).</summary>
    private static IEnumerable<string> SplitAnd(string s) => Split(s, "\\and");

    private static IEnumerable<string> Split(string s, string sep)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
            else if (depth == 0 && string.CompareOrdinal(s, i, sep, 0, sep.Length) == 0
                && (sep[0] != '\\' || i + sep.Length >= s.Length || !char.IsLetter(s[i + sep.Length])))
            {
                parts.Add(s[start..i]);
                i += sep.Length - 1;
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    private static IEnumerable<string> Split(string s, char sep) => Split(s, sep.ToString());

    private static string RemoveCommand(string s, string name)
    {
        var sb = new StringBuilder();
        var i = 0;
        foreach (Match m in Regex.Matches(s, @"\\" + name + @"(?![A-Za-z@])"))
        {
            if (m.Index < i) continue;
            var open = m.Index + m.Length;
            while (open < s.Length && char.IsWhiteSpace(s[open])) open++;
            var close = open < s.Length && s[open] == '{' ? MatchingBrace(s, open) : -1;
            if (close < 0) continue;
            sb.Append(s, i, m.Index - i);
            i = close + 1;
        }
        sb.Append(s, i, s.Length - i);
        return sb.ToString();
    }

    /// <summary>Plain text: line breaks, ~, simple formatting and stray braces out; spaces collapsed.</summary>
    private static string Clean(string s)
    {
        s = Regex.Replace(s, @"\\\\|\\newline|~", " ");
        s = Regex.Replace(s, @"\\(?:textbf|textit|emph|textsc|mbox|text|url|href\{[^}]*\})\s*\{([^{}]*)\}", "$1");
        s = Regex.Replace(s, @"\\(?:IEEEauthorrefmark|orcid|orcidID|corref|fnref|footnotemark)\{[^}]*\}", "");
        s = Regex.Replace(s, @"\\[,;:! ]", " ");
        s = s.Replace("{", "").Replace("}", "");
        return Regex.Replace(s, @"\s+", " ").Trim().Trim(',', ';');
    }

    private static string StripComments(string s) => Regex.Replace(s, @"(?<!\\)%.*$", "", RegexOptions.Multiline);

    [GeneratedRegex(@"\\(?<cmd>author|affiliation|address|ead)\*?(?:\[(?<opt>[^\]]*)\])?\s*\{")]
    private static partial Regex ElsevierCommand();

    /// <summary>IEEE's template numbers its authors "1\textsuperscript{st} Name": the number is not part of the name.</summary>
    [GeneratedRegex(@"^\s*\d+\s*(?:\\textsuperscript\s*\{[^{}]*\}|\$\^\{?[a-z]+\}?\$)\s*")]
    private static partial Regex IeeeOrdinal();
}
