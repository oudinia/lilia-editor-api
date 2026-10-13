using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Prose with LaTeX in it, read for a format that is not LaTeX (the Word export). The PDF
/// typesets <c>\SI{0.20}{\metre}</c>, <c>\textbf{x}</c>, <c>Theorem~(thm:a)</c>; Word printed
/// them as typed (13 Oct sweep, LTX-51). Text marks become the Markdown marks the Word export
/// already reads, units are spelled, references take the number the last compile gave them, and
/// maths is left as it is for the equation converter.
/// </summary>
public static class ProseLatex
{
    /// <summary>Stands in for a price's dollar, which the Word parser would read as maths.</summary>
    public const char PriceDollar = '';

    private static readonly Dictionary<string, string> Marks = new(StringComparer.Ordinal)
    {
        ["textbf"] = "**", ["emph"] = "*", ["textit"] = "*", ["textsl"] = "*",
        ["underline"] = "__", ["texttt"] = "`",
    };

    private static readonly Dictionary<string, string> Units = new(StringComparer.Ordinal)
    {
        ["metre"] = "m", ["meter"] = "m", ["second"] = "s", ["gram"] = "g", ["kelvin"] = "K",
        ["newton"] = "N", ["joule"] = "J", ["watt"] = "W", ["pascal"] = "Pa", ["hertz"] = "Hz",
        ["ampere"] = "A", ["volt"] = "V", ["ohm"] = "Ω", ["mole"] = "mol", ["litre"] = "L",
        ["liter"] = "L", ["candela"] = "cd", ["coulomb"] = "C", ["farad"] = "F", ["tesla"] = "T",
        ["henry"] = "H", ["siemens"] = "S", ["weber"] = "Wb", ["becquerel"] = "Bq", ["gray"] = "Gy",
        ["electronvolt"] = "eV", ["degreeCelsius"] = "°C", ["celsius"] = "°C", ["degree"] = "°",
        ["minute"] = "min", ["hour"] = "h", ["day"] = "d", ["percent"] = "%", ["radian"] = "rad",
        ["steradian"] = "sr", ["bar"] = "bar", ["tonne"] = "t", ["dalton"] = "Da", ["molar"] = "M",
        ["kilo"] = "k", ["mega"] = "M", ["giga"] = "G", ["tera"] = "T", ["milli"] = "m",
        ["micro"] = "µ", ["nano"] = "n", ["pico"] = "p", ["femto"] = "f", ["centi"] = "c",
        ["deci"] = "d", ["hecto"] = "h",
    };

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["ldots"] = "…", ["dots"] = "…", ["textendash"] = "–", ["textemdash"] = "—",
        ["LaTeX"] = "LaTeX", ["TeX"] = "TeX", ["pm"] = "±", ["times"] = "×", ["quad"] = " ",
        ["qquad"] = " ", ["noindent"] = "", ["par"] = "\n", ["newline"] = "\n", ["centering"] = "",
        ["nobreakspace"] = " ", ["S"] = "§", ["copyright"] = "©", ["textdegree"] = "°",
    };

    // Ties, as TieSyntax reads them: a ~ between two characters.
    private static readonly Regex Tie = new(@"(?<=[^\s~])~(?=[^\s~])", RegexOptions.Compiled);

    /// <summary>
    /// The text for Word. <paramref name="labelNumber"/> gives a label's number from the last
    /// compile, or null when there is none; then the label itself is shown in brackets.
    /// </summary>
    public static string ForWord(string? text, Func<string, string?>? labelNumber = null) => ForWord(text, labelNumber, plain: false);

    /// <summary>
    /// For a field Word shows as plain text (a heading, a caption, a header cell): the same
    /// reading, with text marks dropped instead of written as Markdown, and a price's dollar back.
    /// </summary>
    public static string PlainForWord(string? text, Func<string, string?>? labelNumber = null) =>
        ForWord(text, labelNumber, plain: true).Replace(PriceDollar, '$');

    private static string ForWord(string? text, Func<string, string?>? labelNumber, bool plain)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (text.IndexOf('\\') < 0 && text.IndexOf('~') < 0 && text.IndexOf('@') < 0
            && text.IndexOf('-') < 0 && text.IndexOf('$') < 0) return text;

        var maths = new List<string>();
        string Hold(string m) { maths.Add(m); return $"\u0001{maths.Count - 1}\u0002"; }

        var s = text;
        // A price is not maths (CurrencyDollar), and Word reads every other $ as maths.
        s = CurrencyDollar.Protect(s, _ => PriceDollar.ToString());
        s = Regex.Replace(s, "`[^`]+`", m => Hold(m.Value)); // code is left as typed
        s = Regex.Replace(s, @"\$\$(.+?)\$\$", m => Hold(m.Value), RegexOptions.Singleline);
        s = Regex.Replace(s, @"\\\[(.+?)\\\]", m => Hold("$$" + m.Groups[1].Value + "$$"), RegexOptions.Singleline);
        s = Regex.Replace(s, @"\\\((.+?)\\\)", m => Hold("$" + m.Groups[1].Value + "$"), RegexOptions.Singleline);
        s = Regex.Replace(s, @"(?<![\\$])\$(?!\$)(.+?)(?<![\\$])\$(?!\$)", m => Hold(m.Value));

        // Lilia's shorthands first (Theorem~(thm:a), @ref, @cite) become the commands below.
        s = CrossRefSyntax.Resolve(s, piece => piece);
        s = Tie.Replace(s, " ");

        s = Commands(s, labelNumber, plain);

        s = s.Replace("---", "—").Replace("--", "–").Replace("``", "“").Replace("''", "”");
        return Regex.Replace(s, "\u0001(\\d+)\u0002", m => maths[int.Parse(m.Groups[1].Value)]);
    }

    private static string Commands(string s, Func<string, string?>? labelNumber, bool plain)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i++]); continue; }

            // Control symbols: \% \& \$ \# \_ \{ \} and \, \\ .
            var next = s[i + 1];
            if (!char.IsLetter(next))
            {
                sb.Append(next switch
                {
                    '$' => PriceDollar.ToString(),
                    ',' or ';' or ' ' => " ",
                    '\\' => "\n",
                    _ => next.ToString(),
                });
                i += 2;
                continue;
            }

            var j = i + 1;
            while (j < s.Length && char.IsLetter(s[j])) j++;
            var name = s[(i + 1)..j];
            if (j < s.Length && s[j] == '*') j++;
            // Optional [..] arguments are read past; mandatory {..} arguments are collected.
            while (j < s.Length && s[j] == '[')
            {
                var close = s.IndexOf(']', j);
                if (close < 0) break;
                j = close + 1;
            }
            var args = new List<string>();
            while (j < s.Length && s[j] == '{')
            {
                var close = ClosingBrace(s, j);
                if (close < 0) break;
                args.Add(s[(j + 1)..close]);
                j = close + 1;
            }

            sb.Append(Command(name, args, labelNumber, plain));
            i = j;
        }
        return sb.ToString();
    }

    private static string Command(string name, List<string> args, Func<string, string?>? labelNumber, bool plain)
    {
        string Arg(int k) => k < args.Count ? Commands(args[k], labelNumber, plain) : "";

        if (Marks.TryGetValue(name, out var mark)) return plain ? Arg(0) : mark + Arg(0) + mark;
        switch (name)
        {
            case "ref" or "cref" or "Cref" or "autoref" or "nameref" or "pageref":
                return string.Join(", ", (args.FirstOrDefault() ?? "").Split(',').Select(k => Number(k.Trim(), labelNumber)));
            case "eqref":
                return "(" + Number((args.FirstOrDefault() ?? "").Trim(), labelNumber) + ")";
            case "cite" or "citep" or "citet" or "parencite" or "textcite" or "autocite" or "citealp" or "citealt":
                return "[" + string.Join(", ", (args.FirstOrDefault() ?? "").Split(',').Select(k => k.Trim())) + "]";
            case "SI" or "qty":
                return Arg(0).Replace("\\pm", "±").Replace(" ", "") + " " + UnitText(args.Count > 1 ? args[1] : "");
            case "si" or "unit":
                return UnitText(args.FirstOrDefault() ?? "");
            case "num":
                return Arg(0);
            case "label" or "index" or "hspace" or "vspace" or "phantom" or "nocite":
                return "";
            case "footnote":
                return " (" + Arg(0) + ")";
            case "url":
                return args.FirstOrDefault() ?? "";
            case "href":
                return args.Count > 1 ? Arg(1) : (args.FirstOrDefault() ?? "");
            case "today":
                return DateTime.UtcNow.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (Words.TryGetValue(name, out var word)) return word;
        // Anything else keeps its text and loses the command: readable, never a stray backslash.
        return string.Concat(Enumerable.Range(0, args.Count).Select(Arg));
    }

    private static string Number(string key, Func<string, string?>? labelNumber) =>
        labelNumber?.Invoke(LabelKey.Safe(key)) ?? labelNumber?.Invoke(key) ?? $"[{key}]";

    /// <summary><c>\metre\per\second\squared</c> → m/s²; literal units (<c>m/s^2</c>) are kept.</summary>
    private static string UnitText(string units)
    {
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(units, @"\\([A-Za-z]+)|([^\\]+)"))
        {
            if (m.Groups[2].Success) { sb.Append(m.Groups[2].Value.Replace("^2", "²").Replace("^3", "³").Replace("~", " ").Trim()); continue; }
            var n = m.Groups[1].Value;
            sb.Append(n switch
            {
                "per" => "/",
                "squared" or "square" => "²",
                "cubed" or "cubic" => "³",
                "cdot" => "·",
                _ => Units.TryGetValue(n, out var u) ? u : n,
            });
        }
        return sb.ToString();
    }

    private static int ClosingBrace(string s, int open)
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
}
