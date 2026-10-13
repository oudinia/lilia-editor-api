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
        InlineMathAsText(ForWord(text, labelNumber, plain: true)).Replace(PriceDollar, '$');

    private static readonly Dictionary<string, string> MathSymbols = new(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε",
        ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ",
        ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ",
        ["phi"] = "φ", ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Pi"] = "Π", ["Sigma"] = "Σ",
        ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥", ["ne"] = "≠", ["neq"] = "≠", ["approx"] = "≈",
        ["times"] = "×", ["cdot"] = "·", ["pm"] = "±", ["infty"] = "∞", ["to"] = "→", ["in"] = "∈",
        ["sim"] = "∼", ["partial"] = "∂", ["nabla"] = "∇", ["ell"] = "ℓ", ["gcd"] = "gcd", ["bmod"] = " mod ", ["mod"] = " mod ", ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp",
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["max"] = "max", ["min"] = "min", ["det"] = "det",
        ["lim"] = "lim", ["neg"] = "¬", ["land"] = "∧", ["lor"] = "∨", ["forall"] = "∀", ["exists"] = "∃",
        ["subset"] = "⊂", ["subseteq"] = "⊆", ["cup"] = "∪", ["cap"] = "∩", ["emptyset"] = "∅", ["mid"] = "|", ["leftarrow"] = "←", ["gets"] = "←", ["rightarrow"] = "→", ["ldots"] = "…", ["dots"] = "…", ["cdots"] = "⋯",
    };

    private const string Sup = "⁰¹²³⁴⁵⁶⁷⁸⁹", Sub = "₀₁₂₃₄₅₆₇₈₉";

    /// <summary>
    /// $\alpha = 0.05$ → α = 0.05 in a field Word shows as plain text (a caption). Only maths made of
    /// letters, digits, simple operators, Greek and a few symbols, with single-digit indices; anything
    /// richer is left as written rather than guessed at.
    /// </summary>
    private static string InlineMathAsText(string text) =>
        text.IndexOf('$') < 0 ? text : Regex.Replace(text, @"(?<![\\$])\$(?!\$)([^$]+?)\$(?!\$)", m =>
        {
            var body = m.Groups[1].Value;
            var converted = Regex.Replace(body, @"\\([A-Za-z]+)", c => MathSymbols.TryGetValue(c.Groups[1].Value, out var u) ? u : c.Value);
            converted = Regex.Replace(converted, @"\^\{?(\d+)\}?", d => string.Concat(d.Groups[1].Value.Select(ch => Sup[ch - '0'])));
            converted = Regex.Replace(converted, @"_\{?(\d+)\}?", d => string.Concat(d.Groups[1].Value.Select(ch => Sub[ch - '0'])));
            return Regex.IsMatch(converted, @"[\\^_{}]") ? m.Value : Regex.Replace(converted, " {2,}", " ").Trim();
        });

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
                // The reference's number where the export knows it ("cite:key"), else its key (MCP-98).
                return "[" + string.Join(", ", (args.FirstOrDefault() ?? "").Split(',').Select(k => k.Trim())
                    .Select(k => labelNumber?.Invoke("cite:" + k) ?? k)) + "]";
            case "SI" or "qty":
                return Arg(0).Replace("\\pm", "±").Replace(" ", "") + " " + UnitText(args.Count > 1 ? args[1] : "");
            case "si" or "unit":
                return UnitText(args.FirstOrDefault() ?? "");
            case "num":
                return Arg(0);
            case "ce":
                return ChemText(args.FirstOrDefault() ?? "");
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
        // An environment's \begin{CJK}{UTF8}{gbsn} and \end{CJK} print nothing; the text between
        // them is read as it is.
        if (name is "begin" or "end" or "selectlanguage" or "usepackage") return "";
        // Anything else keeps its text — the last argument (\foreignlanguage{russian}{Привет},
        // \textcolor{red}{x}) — and loses the command: readable, never a stray backslash.
        return args.Count == 0 ? "" : Arg(args.Count - 1);
    }

    private static readonly Regex SiCommand = new(@"\\(?<c>SI|qty|si|unit|num)\s*(?:\[[^\]]*\])?\{(?<a>(?:[^{}]|\{[^{}]*\})*)\}(?:\{(?<b>(?:[^{}]|\{[^{}]*\})*)\})?", RegexOptions.Compiled);

    /// <summary>
    /// Maths for Word's equation converter, which knows no siunitx: <c>\SI{9.81}{\metre\per\second}</c>
    /// becomes <c>9.81\,\mathrm{m/s}</c>.
    /// </summary>
    private static readonly Regex CeCommand = new(@"\\ce\{(?<f>[^{}]*)\}", RegexOptions.Compiled);

    /// <summary>A formula as mhchem prints it: H2O → H₂O, SO4^2- → SO₄²⁻, in LaTeX the converter knows.</summary>
    private static string Chem(string formula)
    {
        // Reaction arrows first (MCP-100: "->[cat]" stayed as typed): conditions over the arrow.
        var f = Regex.Replace(formula.Trim(), @"->\[([^\]]*)\](?:\[([^\]]*)\])?", m => $"\u0001{m.Groups[1].Value}\u0002");
        f = Regex.Replace(f, @"(?<=[A-Za-z)\]])(\d+)", "_{$1}");
        f = Regex.Replace(f, @"\^(\d*[+-])", "^{$1}");
        f = f.Replace("<=>", "\\rightleftharpoons ").Replace("<->", "\\leftrightarrow ").Replace("->", "\\rightarrow ").Replace("<-", "\\leftarrow ");
        f = Regex.Replace(f, "\u0001([^\u0002]*)\u0002", m => $"\\overset{{\\mathrm{{{m.Groups[1].Value}}}}}{{\\rightarrow}}");
        return "\\mathrm{" + f.Replace(" ", "\\ ") + "}";
    }

    private const string Subscripts = "₀₁₂₃₄₅₆₇₈₉";
    private static readonly Dictionary<char, char> Superscripts = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻',
    };

    /// <summary>\ce in prose, as text: H₂O, Fe³⁺, A → B (MCP-100: "->" and "Fe^3+" stayed as typed).</summary>
    private static string ChemText(string formula)
    {
        var f = Regex.Replace(formula, @"->\[([^\]]*)\](?:\[([^\]]*)\])?", m => $" →({m.Groups[1].Value}) ");
        f = f.Replace("<=>", "⇌").Replace("<->", "↔").Replace("->", "→").Replace("<-", "←");
        f = Regex.Replace(f, @"\^\{?(\d*[+-])\}?", m => string.Concat(m.Groups[1].Value.Select(c => Superscripts[c])));
        f = Regex.Replace(f, @"(?<=[A-Za-z)\]])(\d+)", d => string.Concat(d.Value.Select(c => Subscripts[c - '0'])));
        return Regex.Replace(f, @"\s+", " ").Trim();
    }

    public static string MathForWord(string? latex)
    {
        if (string.IsNullOrEmpty(latex) || latex.IndexOf('\\') < 0) return latex ?? "";
        latex = CeCommand.Replace(latex, m => Chem(m.Groups["f"].Value));
        return SiCommand.Replace(latex, m =>
        {
            var a = m.Groups["a"].Value.Trim();
            return m.Groups["c"].Value switch
            {
                "SI" or "qty" when m.Groups["b"].Success => $"{a}\\,\\mathrm{{{UnitText(m.Groups["b"].Value)}}}",
                "si" or "unit" => $"\\mathrm{{{UnitText(a)}}}",
                _ => a,
            };
        });
    }

    private static string Number(string key, Func<string, string?>? labelNumber) =>
        labelNumber?.Invoke(LabelKey.Safe(key)) ?? labelNumber?.Invoke(key)
        // A bare name defined with its kind's prefix (eq:loss for "loss"), as the LaTeX resolves it (MCP-106).
        ?? (key.Contains(':') ? null : new[] { "eq", "fig", "tbl", "tab", "alg", "thm", "sec" }
            .Select(p => labelNumber?.Invoke($"{p}:{LabelKey.Safe(key)}")).FirstOrDefault(n => n is not null))
        ?? $"[{key}]";

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
