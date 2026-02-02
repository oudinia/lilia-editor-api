using System.Text;
using System.Xml.Linq;
using Lilia.Import.Interfaces;

namespace Lilia.Import.Converters;

/// <summary>
/// Converts Office Math Markup Language (OMML) to LaTeX.
/// Supports common math constructs: fractions, superscripts, subscripts,
/// roots, matrices, Greek letters, operators, and more.
/// </summary>
public class OmmlToLatexConverter : IOmmlConverter
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // Greek letter mappings (OMML character to LaTeX command)
    private static readonly Dictionary<char, string> GreekLetters = new()
    {
        ['α'] = @"\alpha", ['β'] = @"\beta", ['γ'] = @"\gamma", ['δ'] = @"\delta",
        ['ε'] = @"\epsilon", ['ζ'] = @"\zeta", ['η'] = @"\eta", ['θ'] = @"\theta",
        ['ι'] = @"\iota", ['κ'] = @"\kappa", ['λ'] = @"\lambda", ['μ'] = @"\mu",
        ['ν'] = @"\nu", ['ξ'] = @"\xi", ['π'] = @"\pi", ['ρ'] = @"\rho",
        ['σ'] = @"\sigma", ['τ'] = @"\tau", ['υ'] = @"\upsilon", ['φ'] = @"\phi",
        ['χ'] = @"\chi", ['ψ'] = @"\psi", ['ω'] = @"\omega",
        ['Α'] = @"\Alpha", ['Β'] = @"\Beta", ['Γ'] = @"\Gamma", ['Δ'] = @"\Delta",
        ['Ε'] = @"\Epsilon", ['Ζ'] = @"\Zeta", ['Η'] = @"\Eta", ['Θ'] = @"\Theta",
        ['Ι'] = @"\Iota", ['Κ'] = @"\Kappa", ['Λ'] = @"\Lambda", ['Μ'] = @"\Mu",
        ['Ν'] = @"\Nu", ['Ξ'] = @"\Xi", ['Π'] = @"\Pi", ['Ρ'] = @"\Rho",
        ['Σ'] = @"\Sigma", ['Τ'] = @"\Tau", ['Υ'] = @"\Upsilon", ['Φ'] = @"\Phi",
        ['Χ'] = @"\Chi", ['Ψ'] = @"\Psi", ['Ω'] = @"\Omega",
        ['ϕ'] = @"\varphi", ['ϵ'] = @"\varepsilon", ['ϑ'] = @"\vartheta",
        ['ϱ'] = @"\varrho", ['ς'] = @"\varsigma", ['ϖ'] = @"\varpi"
    };

    // Symbol mappings
    private static readonly Dictionary<char, string> Symbols = new()
    {
        ['∞'] = @"\infty", ['∂'] = @"\partial", ['∇'] = @"\nabla",
        ['∑'] = @"\sum", ['∏'] = @"\prod", ['∫'] = @"\int",
        ['∮'] = @"\oint", ['√'] = @"\sqrt", ['∝'] = @"\propto",
        ['±'] = @"\pm", ['∓'] = @"\mp", ['×'] = @"\times", ['÷'] = @"\div",
        ['≤'] = @"\leq", ['≥'] = @"\geq", ['≠'] = @"\neq", ['≈'] = @"\approx",
        ['≡'] = @"\equiv", ['∈'] = @"\in", ['∉'] = @"\notin", ['⊂'] = @"\subset",
        ['⊃'] = @"\supset", ['⊆'] = @"\subseteq", ['⊇'] = @"\supseteq",
        ['∪'] = @"\cup", ['∩'] = @"\cap", ['∅'] = @"\emptyset",
        ['∀'] = @"\forall", ['∃'] = @"\exists", ['¬'] = @"\neg",
        ['∧'] = @"\land", ['∨'] = @"\lor", ['⊕'] = @"\oplus", ['⊗'] = @"\otimes",
        ['→'] = @"\rightarrow", ['←'] = @"\leftarrow", ['↔'] = @"\leftrightarrow",
        ['⇒'] = @"\Rightarrow", ['⇐'] = @"\Leftarrow", ['⇔'] = @"\Leftrightarrow",
        ['↑'] = @"\uparrow", ['↓'] = @"\downarrow",
        ['·'] = @"\cdot", ['…'] = @"\ldots", ['⋯'] = @"\cdots",
        ['ℕ'] = @"\mathbb{N}", ['ℤ'] = @"\mathbb{Z}", ['ℚ'] = @"\mathbb{Q}",
        ['ℝ'] = @"\mathbb{R}", ['ℂ'] = @"\mathbb{C}",
        ['′'] = "'", ['″'] = "''", ['‴'] = "'''"
    };

    /// <inheritdoc />
    public (string latex, bool success, string? error) Convert(string ommlXml)
    {
        try
        {
            var doc = XDocument.Parse(ommlXml);
            var root = doc.Root;

            if (root == null)
                return ("", false, "Empty OMML content");

            var latex = ConvertElement(root);
            return (latex.Trim(), true, null);
        }
        catch (Exception ex)
        {
            return ("", false, $"OMML parsing error: {ex.Message}");
        }
    }

    private string ConvertElement(XElement element)
    {
        var localName = element.Name.LocalName;

        return localName switch
        {
            "oMath" => ConvertOmath(element),
            "oMathPara" => ConvertOmathPara(element),
            "r" => ConvertRun(element),
            "t" => ConvertText(element),
            "f" => ConvertFraction(element),
            "rad" => ConvertRadical(element),
            "sSup" => ConvertSuperscript(element),
            "sSub" => ConvertSubscript(element),
            "sSubSup" => ConvertSubSup(element),
            "nary" => ConvertNary(element),
            "d" => ConvertDelimiter(element),
            "m" => ConvertMatrix(element),
            "eqArr" => ConvertEquationArray(element),
            "box" => ConvertBox(element),
            "bar" => ConvertBar(element),
            "acc" => ConvertAccent(element),
            "limLow" => ConvertLimitLower(element),
            "limUpp" => ConvertLimitUpper(element),
            "func" => ConvertFunction(element),
            "groupChr" => ConvertGroupChar(element),
            "borderBox" => ConvertBorderBox(element),
            _ => ConvertChildren(element)
        };
    }

    private string ConvertChildren(XElement element)
    {
        var sb = new StringBuilder();
        foreach (var child in element.Elements())
        {
            sb.Append(ConvertElement(child));
        }
        return sb.ToString();
    }

    private string ConvertOmath(XElement element) => ConvertChildren(element);

    private string ConvertOmathPara(XElement element)
    {
        var equations = element.Elements(M + "oMath").Select(ConvertElement);
        return string.Join(@" \\ ", equations);
    }

    private string ConvertRun(XElement element) => ConvertChildren(element);

    private string ConvertText(XElement element)
    {
        var text = element.Value;
        var sb = new StringBuilder();

        foreach (var c in text)
        {
            if (GreekLetters.TryGetValue(c, out var greek))
                sb.Append(greek + " ");
            else if (Symbols.TryGetValue(c, out var symbol))
                sb.Append(symbol + " ");
            else if (c == ' ')
                sb.Append(@"\ ");
            else if (NeedsEscape(c))
                sb.Append('\\').Append(c);
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool NeedsEscape(char c) => c is '#' or '$' or '%' or '&' or '_' or '{' or '}';

    private string ConvertFraction(XElement element)
    {
        var num = element.Element(M + "num");
        var den = element.Element(M + "den");

        var numLatex = num != null ? ConvertChildren(num) : "";
        var denLatex = den != null ? ConvertChildren(den) : "";

        // Check fraction type
        var fracPr = element.Element(M + "fPr");
        var type = fracPr?.Element(M + "type")?.Attribute(M + "val")?.Value;

        return type switch
        {
            "lin" => $"({numLatex})/({denLatex})",  // Linear fraction
            "skw" => $@"\sfrac{{{numLatex}}}{{{denLatex}}}",  // Skewed
            "noBar" => $@"\genfrac{{}}{{}}{{0pt}}{{}}{{{numLatex}}}{{{denLatex}}}",  // No bar
            _ => $@"\frac{{{numLatex}}}{{{denLatex}}}"  // Standard fraction
        };
    }

    private string ConvertRadical(XElement element)
    {
        var deg = element.Element(M + "deg");
        var e = element.Element(M + "e");

        var eLatex = e != null ? ConvertChildren(e) : "";

        if (deg != null)
        {
            var degLatex = ConvertChildren(deg);
            if (!string.IsNullOrWhiteSpace(degLatex))
            {
                return $@"\sqrt[{degLatex}]{{{eLatex}}}";
            }
        }

        return $@"\sqrt{{{eLatex}}}";
    }

    private string ConvertSuperscript(XElement element)
    {
        var e = element.Element(M + "e");
        var sup = element.Element(M + "sup");

        var eLatex = e != null ? ConvertChildren(e) : "";
        var supLatex = sup != null ? ConvertChildren(sup) : "";

        // Wrap base if it's complex
        if (eLatex.Length > 1 && !eLatex.StartsWith("{"))
            eLatex = $"{{{eLatex}}}";

        return $"{eLatex}^{{{supLatex}}}";
    }

    private string ConvertSubscript(XElement element)
    {
        var e = element.Element(M + "e");
        var sub = element.Element(M + "sub");

        var eLatex = e != null ? ConvertChildren(e) : "";
        var subLatex = sub != null ? ConvertChildren(sub) : "";

        if (eLatex.Length > 1 && !eLatex.StartsWith("{"))
            eLatex = $"{{{eLatex}}}";

        return $"{eLatex}_{{{subLatex}}}";
    }

    private string ConvertSubSup(XElement element)
    {
        var e = element.Element(M + "e");
        var sub = element.Element(M + "sub");
        var sup = element.Element(M + "sup");

        var eLatex = e != null ? ConvertChildren(e) : "";
        var subLatex = sub != null ? ConvertChildren(sub) : "";
        var supLatex = sup != null ? ConvertChildren(sup) : "";

        if (eLatex.Length > 1 && !eLatex.StartsWith("{"))
            eLatex = $"{{{eLatex}}}";

        return $"{eLatex}_{{{subLatex}}}^{{{supLatex}}}";
    }

    private string ConvertNary(XElement element)
    {
        var naryPr = element.Element(M + "naryPr");
        var sub = element.Element(M + "sub");
        var sup = element.Element(M + "sup");
        var e = element.Element(M + "e");

        // Get the operator character
        var chr = naryPr?.Element(M + "chr")?.Attribute(M + "val")?.Value ?? "∫";
        var limLoc = naryPr?.Element(M + "limLoc")?.Attribute(M + "val")?.Value;

        var op = chr switch
        {
            "∑" => @"\sum",
            "∏" => @"\prod",
            "∫" => @"\int",
            "∬" => @"\iint",
            "∭" => @"\iiint",
            "∮" => @"\oint",
            "⋃" => @"\bigcup",
            "⋂" => @"\bigcap",
            "⋁" => @"\bigvee",
            "⋀" => @"\bigwedge",
            _ => @"\int"
        };

        var subLatex = sub != null ? ConvertChildren(sub) : "";
        var supLatex = sup != null ? ConvertChildren(sup) : "";
        var eLatex = e != null ? ConvertChildren(e) : "";

        var sb = new StringBuilder(op);

        if (!string.IsNullOrWhiteSpace(subLatex))
            sb.Append($"_{{{subLatex}}}");

        if (!string.IsNullOrWhiteSpace(supLatex))
            sb.Append($"^{{{supLatex}}}");

        sb.Append(' ').Append(eLatex);

        return sb.ToString();
    }

    private string ConvertDelimiter(XElement element)
    {
        var dPr = element.Element(M + "dPr");
        var begChr = dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value ?? "(";
        var endChr = dPr?.Element(M + "endChr")?.Attribute(M + "val")?.Value ?? ")";

        // Convert delimiter characters
        var leftDelim = ConvertDelimChar(begChr, true);
        var rightDelim = ConvertDelimChar(endChr, false);

        var content = new StringBuilder();
        var eElements = element.Elements(M + "e").ToList();

        for (int i = 0; i < eElements.Count; i++)
        {
            if (i > 0) content.Append(", ");
            content.Append(ConvertChildren(eElements[i]));
        }

        return $@"\left{leftDelim} {content} \right{rightDelim}";
    }

    private static string ConvertDelimChar(string chr, bool isLeft)
    {
        return chr switch
        {
            "(" => "(",
            ")" => ")",
            "[" => "[",
            "]" => "]",
            "{" => @"\{",
            "}" => @"\}",
            "|" => "|",
            "‖" => @"\|",
            "⌈" => isLeft ? @"\lceil" : @"\rceil",
            "⌉" => @"\rceil",
            "⌊" => isLeft ? @"\lfloor" : @"\rfloor",
            "⌋" => @"\rfloor",
            "〈" or "⟨" => isLeft ? @"\langle" : @"\rangle",
            "〉" or "⟩" => @"\rangle",
            "" => ".",  // Empty delimiter
            _ => chr
        };
    }

    private string ConvertMatrix(XElement element)
    {
        var rows = element.Elements(M + "mr").ToList();
        var sb = new StringBuilder(@"\begin{matrix}");
        sb.AppendLine();

        for (int i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Elements(M + "e").ToList();
            for (int j = 0; j < cells.Count; j++)
            {
                if (j > 0) sb.Append(" & ");
                sb.Append(ConvertChildren(cells[j]));
            }
            if (i < rows.Count - 1) sb.Append(@" \\");
            sb.AppendLine();
        }

        sb.Append(@"\end{matrix}");
        return sb.ToString();
    }

    private string ConvertEquationArray(XElement element)
    {
        var equations = element.Elements(M + "e").ToList();
        var sb = new StringBuilder(@"\begin{aligned}");
        sb.AppendLine();

        for (int i = 0; i < equations.Count; i++)
        {
            sb.Append(ConvertChildren(equations[i]));
            if (i < equations.Count - 1) sb.Append(@" \\");
            sb.AppendLine();
        }

        sb.Append(@"\end{aligned}");
        return sb.ToString();
    }

    private string ConvertBox(XElement element)
    {
        var e = element.Element(M + "e");
        return e != null ? ConvertChildren(e) : "";
    }

    private string ConvertBar(XElement element)
    {
        var barPr = element.Element(M + "barPr");
        var pos = barPr?.Element(M + "pos")?.Attribute(M + "val")?.Value;
        var e = element.Element(M + "e");
        var eLatex = e != null ? ConvertChildren(e) : "";

        return pos == "bot"
            ? $@"\underline{{{eLatex}}}"
            : $@"\overline{{{eLatex}}}";
    }

    private string ConvertAccent(XElement element)
    {
        var accPr = element.Element(M + "accPr");
        var chr = accPr?.Element(M + "chr")?.Attribute(M + "val")?.Value ?? "^";
        var e = element.Element(M + "e");
        var eLatex = e != null ? ConvertChildren(e) : "";

        return chr switch
        {
            "̂" or "^" => $@"\hat{{{eLatex}}}",
            "̃" or "~" => $@"\tilde{{{eLatex}}}",
            "̄" or "¯" => $@"\bar{{{eLatex}}}",
            "̇" or "˙" => $@"\dot{{{eLatex}}}",
            "̈" => $@"\ddot{{{eLatex}}}",
            "⃗" or "→" => $@"\vec{{{eLatex}}}",
            "̆" => $@"\breve{{{eLatex}}}",
            "̌" => $@"\check{{{eLatex}}}",
            "́" => $@"\acute{{{eLatex}}}",
            "̀" => $@"\grave{{{eLatex}}}",
            _ => $@"\hat{{{eLatex}}}"
        };
    }

    private string ConvertLimitLower(XElement element)
    {
        var e = element.Element(M + "e");
        var lim = element.Element(M + "lim");

        var eLatex = e != null ? ConvertChildren(e) : "";
        var limLatex = lim != null ? ConvertChildren(lim) : "";

        return $@"\underset{{{limLatex}}}{{{eLatex}}}";
    }

    private string ConvertLimitUpper(XElement element)
    {
        var e = element.Element(M + "e");
        var lim = element.Element(M + "lim");

        var eLatex = e != null ? ConvertChildren(e) : "";
        var limLatex = lim != null ? ConvertChildren(lim) : "";

        return $@"\overset{{{limLatex}}}{{{eLatex}}}";
    }

    private string ConvertFunction(XElement element)
    {
        var fName = element.Element(M + "fName");
        var e = element.Element(M + "e");

        var funcName = fName != null ? ConvertChildren(fName).Trim() : "";
        var eLatex = e != null ? ConvertChildren(e) : "";

        // Standard math functions
        var knownFuncs = new HashSet<string>
        {
            "sin", "cos", "tan", "cot", "sec", "csc",
            "sinh", "cosh", "tanh", "coth",
            "arcsin", "arccos", "arctan",
            "log", "ln", "lg", "exp",
            "lim", "min", "max", "inf", "sup",
            "det", "dim", "ker", "gcd"
        };

        var funcLower = funcName.ToLowerInvariant();
        if (knownFuncs.Contains(funcLower))
        {
            return $@"\{funcLower} {eLatex}";
        }

        return $@"\mathrm{{{funcName}}} {eLatex}";
    }

    private string ConvertGroupChar(XElement element)
    {
        var groupChrPr = element.Element(M + "groupChrPr");
        var chr = groupChrPr?.Element(M + "chr")?.Attribute(M + "val")?.Value ?? "⏟";
        var pos = groupChrPr?.Element(M + "pos")?.Attribute(M + "val")?.Value ?? "bot";
        var e = element.Element(M + "e");
        var eLatex = e != null ? ConvertChildren(e) : "";

        return (chr, pos) switch
        {
            ("⏟", "bot") => $@"\underbrace{{{eLatex}}}",
            ("⏞", "top") => $@"\overbrace{{{eLatex}}}",
            (_, "bot") => $@"\underbrace{{{eLatex}}}",
            (_, "top") => $@"\overbrace{{{eLatex}}}",
            _ => eLatex
        };
    }

    private string ConvertBorderBox(XElement element)
    {
        var e = element.Element(M + "e");
        var eLatex = e != null ? ConvertChildren(e) : "";
        return $@"\boxed{{{eLatex}}}";
    }
}
