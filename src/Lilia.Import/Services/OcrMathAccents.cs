using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// A text accent that OCR put inside a formula: <c>$\'e$</c>, <c>$\^x$</c>, <c>\[ \"u \]</c>. TeX's text accents
/// (<c>\' \` \^ \" \~ \= \. \u \v \H \c \k \d \b \t \r</c>) stop the PDF in maths ("Please use \mathaccent for accents in
/// math mode": the launch bot's bulk import of handwritten notes, 21 Nov 2026, document A line 600). Each becomes its maths
/// accent (<c>\acute{e}</c>, <c>\hat{x}</c>, <c>\ddot{u}</c>, <c>\tilde</c>, <c>\bar</c>, <c>\dot</c>, <c>\breve</c>,
/// <c>\check</c>, <c>\mathring</c>), which prints the same mark over the same letter. A word under an accent, or an accent
/// maths has none of (<c>\H</c>, <c>\c</c>, <c>\k</c>, <c>\d</c>, <c>\b</c>, <c>\t</c>), is set as text, <c>\text{\H{o}}</c>.
/// Only an accent with nothing to sit on changes what is printed (<c>f\'</c> becomes the prime <c>f'</c>; any other is
/// dropped): that one is reported. Text inside the formula (<c>\text{caf\'e}</c>) is text, where the accent is right, and is
/// left alone. Used on Mathpix's output only (scan and PDF import), with <see cref="OcrMathGuard"/>.
/// </summary>
public static partial class OcrMathAccents
{
    private static readonly Dictionary<string, string> MathAccent = new()
    {
        ["'"] = @"\acute", ["`"] = @"\grave", ["^"] = @"\hat", ["\""] = @"\ddot", ["~"] = @"\tilde", ["="] = @"\bar",
        ["."] = @"\dot", ["u"] = @"\breve", ["v"] = @"\check", ["r"] = @"\mathring",
    };

    /// <summary>A formula's LaTeX (an equation block): every text accent outside its <c>\text{…}</c> as a maths one.</summary>
    /// <param name="uncertain">Gets each accent whose printing changed (one with nothing to sit on).</param>
    public static string InMath(string latex, ICollection<string> uncertain) => Replace(latex, uncertain);

    /// <summary>Text that may hold maths: only the accents inside its maths are changed (outside, they are right).</summary>
    public static string InText(string text, ICollection<string> uncertain)
    {
        if (string.IsNullOrEmpty(text) || !Accent().IsMatch(text)) return text;
        return OcrMathGuard.MathRegion().Replace(text, m =>
        {
            var body = m.Groups["body"];
            return m.Value[..(body.Index - m.Index)] + Replace(body.Value, uncertain) + m.Value[(body.Index - m.Index + body.Length)..];
        });
    }

    private static string Replace(string math, ICollection<string> uncertain)
    {
        if (string.IsNullOrEmpty(math) || !Accent().IsMatch(math)) return math;
        var text = TextGroups(math);
        return Accent().Replace(math, m =>
        {
            if (text.Any(r => m.Index >= r.Start && m.Index < r.End)) return m.Value;
            var mark = m.Groups["m"].Value;
            var command = @"\" + mark;
            if (!m.Groups["arg"].Success)
            {
                // Nothing to sit on: \' after a letter is read as its prime; any other mark is dropped.
                uncertain.Add(command);
                return mark == "'" ? "'" : "";
            }
            var arg = m.Groups["arg"].Value;
            var body = m.Groups["body"].Success ? m.Groups["body"].Value.Trim() : arg;
            var isWord = Regex.IsMatch(body, @"^\p{L}{2,}$");
            if (MathAccent.TryGetValue(mark, out var accent) && !isWord)
                return accent + "{" + Dotless(body) + "}";
            // A word, or an accent maths has none of: the text accent, as text. Its letter must be text too.
            if (Regex.IsMatch(body, @"^(?:\p{L}+|\\[ij])$")) return @"\text{" + command + "{" + body + "}}";
            uncertain.Add(command);
            return "{" + body + "}";
        });
    }

    /// <summary>\i and \j are text's dotless letters; maths writes them \imath and \jmath.</summary>
    private static string Dotless(string body) => body switch
    {
        @"\i" => @"\imath",
        @"\j" => @"\jmath",
        _ => body,
    };

    /// <summary>Where the formula's own text is (\text{…}, \mbox{…} …), as start and end indexes.</summary>
    internal static List<(int Start, int End)> TextGroups(string math)
    {
        var ranges = new List<(int, int)>();
        foreach (Match m in TextCommand().Matches(math))
        {
            var open = m.Index + m.Length - 1;
            var depth = 0;
            for (var i = open; i < math.Length; i++)
            {
                if (math[i] == '\\') { i++; continue; }
                if (math[i] == '{') depth++;
                else if (math[i] == '}' && --depth == 0) { ranges.Add((open, i)); break; }
            }
        }
        return ranges;
    }

    /// <summary>
    /// A text accent (its backslash not itself escaped, so <c>\\'</c> is a line break and a prime): a symbol mark, or a
    /// letter mark not followed by a letter (\v is an accent, \vec is not), and what it sits on: a group, a command or one
    /// character. No argument (the formula's end, a closing brace) is an accent with nothing to sit on.
    /// </summary>
    [GeneratedRegex(@"(?<=(?<!\\)(?:\\\\)*)\\(?:(?<m>['`^""~=.])|(?<m>[uvHckdbtr])(?![A-Za-z]))[ \t]*(?<arg>\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}|\\[A-Za-z]+|[A-Za-z0-9])?")]
    private static partial Regex Accent();

    [GeneratedRegex(@"\\(?:text|textrm|textit|textbf|textsf|texttt|textnormal|textup|textsl|textsc|mbox|hbox|intertext)\s*\{")]
    private static partial Regex TextCommand();
}
