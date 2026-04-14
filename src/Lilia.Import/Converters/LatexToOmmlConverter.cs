using System.Text;
using System.Xml.Linq;
using Lilia.Import.Interfaces;

namespace Lilia.Import.Converters;

/// <summary>
/// Converts LaTeX math expressions to OMML (Office Math Markup Language) for DOCX embedding.
/// Structural mirror of OmmlToLatexConverter — handles the same construct set in reverse.
/// Returns the inner &lt;m:oMath&gt; element as an XML string (caller adds oMathPara wrapper for display mode).
/// </summary>
public class LatexToOmmlConverter : ILatexToOmmlConverter
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    // LaTeX command → Unicode char (inverse of OmmlToLatexConverter's tables)
    private static readonly Dictionary<string, string> CommandToChar = new(StringComparer.Ordinal)
    {
        // Greek lowercase
        [@"\alpha"] = "α", [@"\beta"] = "β", [@"\gamma"] = "γ", [@"\delta"] = "δ",
        [@"\epsilon"] = "ε", [@"\zeta"] = "ζ", [@"\eta"] = "η", [@"\theta"] = "θ",
        [@"\iota"] = "ι", [@"\kappa"] = "κ", [@"\lambda"] = "λ", [@"\mu"] = "μ",
        [@"\nu"] = "ν", [@"\xi"] = "ξ", [@"\pi"] = "π", [@"\rho"] = "ρ",
        [@"\sigma"] = "σ", [@"\tau"] = "τ", [@"\upsilon"] = "υ", [@"\phi"] = "φ",
        [@"\chi"] = "χ", [@"\psi"] = "ψ", [@"\omega"] = "ω",
        // Greek uppercase
        [@"\Gamma"] = "Γ", [@"\Delta"] = "Δ", [@"\Theta"] = "Θ", [@"\Lambda"] = "Λ",
        [@"\Xi"] = "Ξ", [@"\Pi"] = "Π", [@"\Sigma"] = "Σ", [@"\Upsilon"] = "Υ",
        [@"\Phi"] = "Φ", [@"\Psi"] = "Ψ", [@"\Omega"] = "Ω",
        [@"\Alpha"] = "Α", [@"\Beta"] = "Β", [@"\Eta"] = "Η", [@"\Iota"] = "Ι",
        [@"\Kappa"] = "Κ", [@"\Mu"] = "Μ", [@"\Nu"] = "Ν", [@"\Rho"] = "Ρ",
        [@"\Tau"] = "Τ", [@"\Chi"] = "Χ",
        // Greek variants
        [@"\varphi"] = "ϕ", [@"\varepsilon"] = "ϵ", [@"\vartheta"] = "ϑ",
        [@"\varrho"] = "ϱ", [@"\varsigma"] = "ς", [@"\varpi"] = "ϖ",
        // Common symbols
        [@"\infty"] = "∞", [@"\partial"] = "∂", [@"\nabla"] = "∇",
        [@"\pm"] = "±", [@"\mp"] = "∓", [@"\times"] = "×", [@"\div"] = "÷",
        [@"\leq"] = "≤", [@"\geq"] = "≥", [@"\neq"] = "≠", [@"\approx"] = "≈",
        [@"\equiv"] = "≡", [@"\in"] = "∈", [@"\notin"] = "∉",
        [@"\subset"] = "⊂", [@"\supset"] = "⊃", [@"\subseteq"] = "⊆", [@"\supseteq"] = "⊇",
        [@"\cup"] = "∪", [@"\cap"] = "∩", [@"\emptyset"] = "∅",
        [@"\forall"] = "∀", [@"\exists"] = "∃", [@"\neg"] = "¬",
        [@"\land"] = "∧", [@"\lor"] = "∨", [@"\oplus"] = "⊕", [@"\otimes"] = "⊗",
        [@"\rightarrow"] = "→", [@"\leftarrow"] = "←", [@"\leftrightarrow"] = "↔",
        [@"\Rightarrow"] = "⇒", [@"\Leftarrow"] = "⇐", [@"\Leftrightarrow"] = "⇔",
        [@"\uparrow"] = "↑", [@"\downarrow"] = "↓",
        [@"\cdot"] = "·", [@"\ldots"] = "…", [@"\cdots"] = "⋯", [@"\vdots"] = "⋮", [@"\ddots"] = "⋱",
        [@"\propto"] = "∝", [@"\perp"] = "⊥", [@"\parallel"] = "∥",
        [@"\angle"] = "∠", [@"\circ"] = "∘", [@"\bullet"] = "•",
        [@"\sim"] = "∼", [@"\simeq"] = "≃", [@"\cong"] = "≅",
        [@"\ll"] = "≪", [@"\gg"] = "≫",
        [@"\to"] = "→", [@"\gets"] = "←",
        [@"\le"] = "≤", [@"\ge"] = "≥", [@"\ne"] = "≠",
        [@"\ell"] = "ℓ", [@"\hbar"] = "ℏ",
        [@"\dagger"] = "†", [@"\ddagger"] = "‡",
        [@"\prime"] = "′",
    };

    private static readonly Dictionary<string, string> NaryOps = new(StringComparer.Ordinal)
    {
        [@"\sum"] = "∑", [@"\prod"] = "∏", [@"\int"] = "∫",
        [@"\iint"] = "∬", [@"\iiint"] = "∭", [@"\oint"] = "∮",
        [@"\bigcup"] = "⋃", [@"\bigcap"] = "⋂",
        [@"\bigvee"] = "⋁", [@"\bigwedge"] = "⋀",
    };

    private static readonly HashSet<string> MathFunctions = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "cot", "sec", "csc",
        "sinh", "cosh", "tanh", "coth",
        "arcsin", "arccos", "arctan",
        "log", "ln", "lg", "exp",
        "lim", "limsup", "liminf", "min", "max", "inf", "sup",
        "det", "dim", "ker", "gcd", "deg", "hom",
    };

    /// <inheritdoc />
    public (string Omml, bool Success, string? Error) Convert(string latex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(latex))
            {
                var empty = new XElement(M + "oMath");
                empty.SetAttributeValue(XNamespace.Xmlns + "m", M.NamespaceName);
                return (empty.ToString(SaveOptions.DisableFormatting), true, null);
            }

            latex = StripOuterDelimiters(latex.Trim());
            var tokens = Tokenize(latex);
            var parser = new OmmlParser(tokens, M, MathFunctions, NaryOps, CommandToChar);
            var elements = parser.ParseMathList();

            var oMath = new XElement(M + "oMath", elements);
            oMath.SetAttributeValue(XNamespace.Xmlns + "m", M.NamespaceName);
            return (oMath.ToString(SaveOptions.DisableFormatting), true, null);
        }
        catch (LatexParseException ex)
        {
            return ("", false, ex.Message);
        }
        catch (Exception ex)
        {
            return ("", false, $"Conversion error: {ex.Message}");
        }
    }

    private static string StripOuterDelimiters(string s)
    {
        if (s.StartsWith(@"\[") && s.EndsWith(@"\]")) return s[2..^2].Trim();
        if (s.StartsWith("$$") && s.EndsWith("$$") && s.Length > 3) return s[2..^2].Trim();
        if (s.StartsWith('$') && s.EndsWith('$') && s.Length > 1) return s[1..^1].Trim();
        return s;
    }

    // ─── Tokenizer ────────────────────────────────────────────────────────────

    internal enum TokKind
    {
        Command, LeftBrace, RightBrace, LeftBracket, RightBracket,
        Caret, Underscore, Ampersand, DoubleBackslash, Char, End
    }

    internal record Token(TokKind Kind, string Value);

    internal static List<Token> Tokenize(string latex)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < latex.Length)
        {
            char c = latex[i];
            if (c == '\\')
            {
                if (i + 1 < latex.Length && latex[i + 1] == '\\')
                {
                    tokens.Add(new Token(TokKind.DoubleBackslash, @"\\")); i += 2; continue;
                }
                if (i + 1 < latex.Length && !char.IsLetter(latex[i + 1]))
                {
                    tokens.Add(new Token(TokKind.Command, "\\" + latex[i + 1])); i += 2; continue;
                }
                int start = i++;
                while (i < latex.Length && char.IsLetter(latex[i])) i++;
                var cmd = latex[start..i];
                while (i < latex.Length && latex[i] == ' ') i++;
                tokens.Add(new Token(TokKind.Command, cmd));
                continue;
            }
            switch (c)
            {
                case '{': tokens.Add(new Token(TokKind.LeftBrace, "{")); i++; break;
                case '}': tokens.Add(new Token(TokKind.RightBrace, "}")); i++; break;
                case '[': tokens.Add(new Token(TokKind.LeftBracket, "[")); i++; break;
                case ']': tokens.Add(new Token(TokKind.RightBracket, "]")); i++; break;
                case '^': tokens.Add(new Token(TokKind.Caret, "^")); i++; break;
                case '_': tokens.Add(new Token(TokKind.Underscore, "_")); i++; break;
                case '&': tokens.Add(new Token(TokKind.Ampersand, "&")); i++; break;
                case ' ': case '\t': case '\r': case '\n': i++; break;
                default: tokens.Add(new Token(TokKind.Char, c.ToString())); i++; break;
            }
        }
        tokens.Add(new Token(TokKind.End, ""));
        return tokens;
    }
}

// ─── Parse exception ──────────────────────────────────────────────────────────

file sealed class LatexParseException(string message) : Exception(message);

// ─── Parser ───────────────────────────────────────────────────────────────────

file sealed class OmmlParser
{
    private readonly List<LatexToOmmlConverter.Token> _tokens;
    private int _pos;
    private readonly XNamespace _m;
    private readonly HashSet<string> _mathFunctions;
    private readonly Dictionary<string, string> _naryOps;
    private readonly Dictionary<string, string> _commandToChar;

    private LatexToOmmlConverter.Token Cur => _tokens[_pos];
    private LatexToOmmlConverter.TokKind CurKind => _tokens[_pos].Kind;

    public OmmlParser(
        List<LatexToOmmlConverter.Token> tokens,
        XNamespace m,
        HashSet<string> mathFunctions,
        Dictionary<string, string> naryOps,
        Dictionary<string, string> commandToChar)
    {
        _tokens = tokens; _pos = 0; _m = m;
        _mathFunctions = mathFunctions; _naryOps = naryOps; _commandToChar = commandToChar;
    }

    private LatexToOmmlConverter.Token Consume()
    {
        var t = _tokens[_pos];
        if (t.Kind != LatexToOmmlConverter.TokKind.End) _pos++;
        return t;
    }

    private void Expect(LatexToOmmlConverter.TokKind kind)
    {
        if (CurKind == kind) Consume();
    }

    // ── Top-level list parser ─────────────────────────────────────────────────

    public List<XElement> ParseMathList(LatexToOmmlConverter.TokKind stopAt = LatexToOmmlConverter.TokKind.End)
    {
        var result = new List<XElement>();
        while (CurKind != LatexToOmmlConverter.TokKind.End && CurKind != stopAt)
        {
            if (CurKind == LatexToOmmlConverter.TokKind.Command &&
                Cur.Value is @"\right" or @"\end") break;
            if (CurKind == LatexToOmmlConverter.TokKind.DoubleBackslash) break;

            result.AddRange(ParseAtomWithSubSup());
        }
        return result;
    }

    // Returns 1+ elements: one for simple atoms, multiple for bare groups {a+b} without sub/sup
    private List<XElement> ParseAtomWithSubSup()
    {
        List<XElement> primary;

        if (CurKind == LatexToOmmlConverter.TokKind.LeftBrace)
        {
            Consume(); // {
            primary = ParseMathList(LatexToOmmlConverter.TokKind.RightBrace);
            Expect(LatexToOmmlConverter.TokKind.RightBrace);
        }
        else if (CurKind == LatexToOmmlConverter.TokKind.Char)
        {
            primary = [MakeRun(Consume().Value)];
        }
        else if (CurKind == LatexToOmmlConverter.TokKind.Command)
        {
            var elem = ParseCommand();
            primary = elem != null ? [elem] : [];
        }
        else
        {
            Consume(); // skip unexpected token
            return [];
        }

        if (primary.Count == 0) return [];

        // Gather ^ and _ in any order
        XElement? sub = null, sup = null;
        while (CurKind is LatexToOmmlConverter.TokKind.Caret or LatexToOmmlConverter.TokKind.Underscore)
        {
            if (CurKind == LatexToOmmlConverter.TokKind.Caret && sup == null)
            {
                Consume();
                sup = new XElement(_m + "sup", ParseBraceGroup());
            }
            else if (CurKind == LatexToOmmlConverter.TokKind.Underscore && sub == null)
            {
                Consume();
                sub = new XElement(_m + "sub", ParseBraceGroup());
            }
            else break;
        }

        if (sub == null && sup == null) return primary;

        var e = new XElement(_m + "e", primary);
        if (sub != null && sup != null) return [new XElement(_m + "sSubSup", e, sub, sup)];
        if (sub != null) return [new XElement(_m + "sSub", e, sub)];
        return [new XElement(_m + "sSup", e, sup)];
    }

    // Parse a braced group {..} or single atom
    private List<XElement> ParseBraceGroup()
    {
        if (CurKind == LatexToOmmlConverter.TokKind.LeftBrace)
        {
            Consume();
            var content = ParseMathList(LatexToOmmlConverter.TokKind.RightBrace);
            Expect(LatexToOmmlConverter.TokKind.RightBrace);
            return content;
        }
        if (CurKind == LatexToOmmlConverter.TokKind.Char)
            return [MakeRun(Consume().Value)];
        if (CurKind == LatexToOmmlConverter.TokKind.Command)
        {
            var elem = ParseCommand();
            return elem != null ? [elem] : [];
        }
        return [];
    }

    // ── Command dispatcher ────────────────────────────────────────────────────

    private XElement? ParseCommand()
    {
        var cmd = Consume().Value;
        var name = cmd.Length > 1 ? cmd[1..] : cmd;

        if (_naryOps.TryGetValue(cmd, out var naryChar))
            return ParseNary(naryChar);

        if (_mathFunctions.Contains(name))
            return ParseFunc(name);

        return cmd switch
        {
            @"\frac" or @"\dfrac" or @"\tfrac" or @"\cfrac" => ParseFrac(),
            @"\sfrac"   => ParseFrac(skewed: true),
            @"\sqrt"    => ParseSqrt(),
            @"\left"    => ParseLeftRight(),
            @"\hat"     => ParseAccent("̂"),
            @"\tilde"   => ParseAccent("̃"),
            @"\bar"     => ParseAccent("̄"),
            @"\dot"     => ParseAccent("̇"),
            @"\ddot"    => ParseAccent("̈"),
            @"\vec"     => ParseAccent("⃗"),
            @"\breve"   => ParseAccent("̆"),
            @"\check"   => ParseAccent("̌"),
            @"\acute"   => ParseAccent("́"),
            @"\grave"   => ParseAccent("̀"),
            @"\overline"  => ParseBar("top"),
            @"\underline" => ParseBar("bot"),
            @"\overbrace"  => ParseGroupChr("⏞", "top"),
            @"\underbrace" => ParseGroupChr("⏟", "bot"),
            @"\overset"  => ParseLimUpp(),
            @"\underset" => ParseLimLow(),
            @"\boxed"    => ParseBorderBox(),
            @"\begin"    => ParseBegin(),
            @"\text" or @"\mathrm" or @"\mathit" or @"\mathbf"
                        or @"\mathsf" or @"\mathtt" or @"\boldsymbol"
                        => ParseTextRun(cmd),
            @"\mathbb"  => ParseMathBb(),
            @"\phantom" or @"\hphantom" or @"\vphantom"
                        => ConsumeArg(),
            @"\," or @"\;" or @"\:" or @"\!" or @"\>" or @"\space"
            or @"\quad" or @"\qquad"
                        => null,
            @"\not"     => ParseNot(),
            _ => _commandToChar.TryGetValue(cmd, out var ch) ? MakeRun(ch) : MakeRun(name)
        };
    }

    // ── Fraction ──────────────────────────────────────────────────────────────

    private XElement ParseFrac(bool skewed = false)
    {
        var num = new XElement(_m + "num", ParseBraceGroup());
        var den = new XElement(_m + "den", ParseBraceGroup());
        if (skewed)
        {
            var fPr = new XElement(_m + "fPr",
                new XElement(_m + "type", new XAttribute(_m + "val", "skw")));
            return new XElement(_m + "f", fPr, num, den);
        }
        return new XElement(_m + "f", num, den);
    }

    // ── Radical ───────────────────────────────────────────────────────────────

    private XElement ParseSqrt()
    {
        List<XElement>? degContent = null;
        if (CurKind == LatexToOmmlConverter.TokKind.LeftBracket)
        {
            Consume();
            degContent = ParseMathList(LatexToOmmlConverter.TokKind.RightBracket);
            Expect(LatexToOmmlConverter.TokKind.RightBracket);
        }

        var eContent = ParseBraceGroup();
        var radPr = new XElement(_m + "radPr");
        if (degContent == null)
            radPr.Add(new XElement(_m + "degHide", new XAttribute(_m + "val", "1")));

        var deg = degContent != null
            ? new XElement(_m + "deg", degContent)
            : new XElement(_m + "deg");

        return new XElement(_m + "rad", radPr, deg, new XElement(_m + "e", eContent));
    }

    // ── N-ary operators ───────────────────────────────────────────────────────

    private XElement ParseNary(string naryChar)
    {
        var naryPr = new XElement(_m + "naryPr",
            new XElement(_m + "chr", new XAttribute(_m + "val", naryChar)));

        XElement? sub = null, sup = null;
        while (CurKind is LatexToOmmlConverter.TokKind.Underscore or LatexToOmmlConverter.TokKind.Caret)
        {
            if (CurKind == LatexToOmmlConverter.TokKind.Underscore && sub == null)
            { Consume(); sub = new XElement(_m + "sub", ParseBraceGroup()); }
            else if (CurKind == LatexToOmmlConverter.TokKind.Caret && sup == null)
            { Consume(); sup = new XElement(_m + "sup", ParseBraceGroup()); }
            else break;
        }

        var eElems = ParseAtomWithSubSup(); // integrand/summand: next atom only
        return new XElement(_m + "nary",
            naryPr,
            sub ?? new XElement(_m + "sub"),
            sup ?? new XElement(_m + "sup"),
            new XElement(_m + "e", eElems));
    }

    // ── \left ... \right ──────────────────────────────────────────────────────

    private XElement ParseLeftRight()
    {
        var leftStr = ConsumeDelimToken();
        var content = ParseMathList();

        if (CurKind == LatexToOmmlConverter.TokKind.Command && Cur.Value == @"\right")
            Consume();
        var rightStr = ConsumeDelimToken();

        var dPr = new XElement(_m + "dPr",
            new XElement(_m + "begChr", new XAttribute(_m + "val", DelimChar(leftStr, true))),
            new XElement(_m + "endChr", new XAttribute(_m + "val", DelimChar(rightStr, false))));

        return new XElement(_m + "d", dPr, new XElement(_m + "e", content));
    }

    private string ConsumeDelimToken()
    {
        if (CurKind == LatexToOmmlConverter.TokKind.Char) return Consume().Value;
        if (CurKind == LatexToOmmlConverter.TokKind.Command) return Consume().Value;
        if (CurKind == LatexToOmmlConverter.TokKind.LeftBracket) { Consume(); return "["; }
        if (CurKind == LatexToOmmlConverter.TokKind.RightBracket) { Consume(); return "]"; }
        if (CurKind == LatexToOmmlConverter.TokKind.LeftBrace) { Consume(); return @"\{"; }
        if (CurKind == LatexToOmmlConverter.TokKind.RightBrace) { Consume(); return @"\}"; }
        return ".";
    }

    private static string DelimChar(string s, bool isLeft) => s switch
    {
        "(" or ")" or "|" => s,
        "[" => "[", "]" => "]",
        @"\{" or "{" => "{", @"\}" or "}" => "}",
        @"\|" or "‖" => "‖",
        @"\lfloor" => "⌊", @"\rfloor" => "⌋",
        @"\lceil"  => "⌈", @"\rceil"  => "⌉",
        @"\langle" => "⟨", @"\rangle" => "⟩",
        "." or @"\." => "",
        _ => isLeft ? "(" : ")"
    };

    // ── Accents ───────────────────────────────────────────────────────────────

    private XElement ParseAccent(string chr)
    {
        var accPr = new XElement(_m + "accPr",
            new XElement(_m + "chr", new XAttribute(_m + "val", chr)));
        return new XElement(_m + "acc", accPr, new XElement(_m + "e", ParseBraceGroup()));
    }

    // ── Bar (overline / underline) ────────────────────────────────────────────

    private XElement ParseBar(string pos)
    {
        var barPr = new XElement(_m + "barPr",
            new XElement(_m + "pos", new XAttribute(_m + "val", pos)));
        return new XElement(_m + "bar", barPr, new XElement(_m + "e", ParseBraceGroup()));
    }

    // ── Group characters (overbrace / underbrace) ─────────────────────────────

    private XElement ParseGroupChr(string chr, string pos)
    {
        var pr = new XElement(_m + "groupChrPr",
            new XElement(_m + "chr", new XAttribute(_m + "val", chr)),
            new XElement(_m + "pos", new XAttribute(_m + "val", pos)));
        return new XElement(_m + "groupChr", pr, new XElement(_m + "e", ParseBraceGroup()));
    }

    // ── \overset / \underset ─────────────────────────────────────────────────

    private XElement ParseLimUpp()
    {
        var lim = new XElement(_m + "lim", ParseBraceGroup());
        var e   = new XElement(_m + "e",   ParseBraceGroup());
        return new XElement(_m + "limUpp", e, lim);
    }

    private XElement ParseLimLow()
    {
        var lim = new XElement(_m + "lim", ParseBraceGroup());
        var e   = new XElement(_m + "e",   ParseBraceGroup());
        return new XElement(_m + "limLow", e, lim);
    }

    // ── \boxed ───────────────────────────────────────────────────────────────

    private XElement ParseBorderBox() =>
        new(_m + "borderBox", new XElement(_m + "e", ParseBraceGroup()));

    // ── Math functions (sin, cos, lim, …) ────────────────────────────────────

    private XElement ParseFunc(string name)
    {
        var fName = new XElement(_m + "fName",
            new XElement(_m + "r",
                new XElement(_m + "rPr",
                    new XElement(_m + "sty", new XAttribute(_m + "val", "r"))),
                new XElement(_m + "t", name)));
        return new XElement(_m + "func", new XElement(_m + "funcPr"), fName,
            new XElement(_m + "e"));
    }

    // ── Text runs (\text, \mathrm, …) ────────────────────────────────────────

    private XElement ParseTextRun(string cmd)
    {
        // Collect raw text from braced argument
        if (CurKind != LatexToOmmlConverter.TokKind.LeftBrace)
        {
            var ch = CurKind == LatexToOmmlConverter.TokKind.Char ? Consume().Value : "?";
            return MakeRun(ch);
        }
        Consume(); // {
        var sb = new StringBuilder();
        int depth = 1;
        while (CurKind != LatexToOmmlConverter.TokKind.End && depth > 0)
        {
            if (CurKind == LatexToOmmlConverter.TokKind.LeftBrace)  { depth++; sb.Append('{'); Consume(); }
            else if (CurKind == LatexToOmmlConverter.TokKind.RightBrace) { depth--; if (depth > 0) sb.Append('}'); Consume(); }
            else sb.Append(Consume().Value);
        }

        var sty = cmd switch
        {
            @"\mathbf" or @"\boldsymbol" => "b",
            @"\mathit" => "i",
            _ => "r"  // roman (upright)
        };
        var rPr = new XElement(_m + "rPr",
            new XElement(_m + "sty", new XAttribute(_m + "val", sty)));
        return new XElement(_m + "r", rPr, new XElement(_m + "t", sb.ToString()));
    }

    // ── \mathbb ───────────────────────────────────────────────────────────────

    private XElement ParseMathBb()
    {
        if (CurKind != LatexToOmmlConverter.TokKind.LeftBrace) return MakeRun("?");
        Consume();
        if (CurKind != LatexToOmmlConverter.TokKind.Char) { ConsumeToClose(); return MakeRun("?"); }
        var letter = Consume().Value;
        Expect(LatexToOmmlConverter.TokKind.RightBrace);
        var ch = letter switch
        {
            "N" => "ℕ", "Z" => "ℤ", "Q" => "ℚ", "R" => "ℝ",
            "C" => "ℂ", "H" => "ℍ", "F" => "𝔽", "k" => "𝕜",
            _ => letter
        };
        return MakeRun(ch);
    }

    // ── \not ─────────────────────────────────────────────────────────────────

    private XElement ParseNot()
    {
        // \not\in → ∉, otherwise best-effort
        if (CurKind == LatexToOmmlConverter.TokKind.Command)
        {
            var next = Cur.Value;
            Consume();
            return next switch
            {
                @"\in"  => MakeRun("∉"),
                @"\subset" => MakeRun("⊄"),
                @"\subseteq" => MakeRun("⊈"),
                @"\equiv" => MakeRun("≢"),
                _ => MakeRun("̸")
            };
        }
        return MakeRun("̸");
    }

    // ── \begin{env} ───────────────────────────────────────────────────────────

    private XElement ParseBegin()
    {
        Expect(LatexToOmmlConverter.TokKind.LeftBrace);
        var env = new StringBuilder();
        while (CurKind != LatexToOmmlConverter.TokKind.RightBrace &&
               CurKind != LatexToOmmlConverter.TokKind.End)
            env.Append(Consume().Value);
        Expect(LatexToOmmlConverter.TokKind.RightBrace);

        return env.ToString().ToLowerInvariant() switch
        {
            "matrix"  => ParseMatrix(""),
            "pmatrix" => ParseMatrix("pmatrix"),
            "bmatrix" => ParseMatrix("bmatrix"),
            "bmatrix*" => ParseMatrix("bmatrix"),
            "vmatrix" => ParseMatrix("vmatrix"),
            "Vmatrix" => ParseMatrix("Vmatrix"),
            "Bmatrix" or "cases" => ParseMatrix("Bmatrix"),
            "aligned" or "align" or "align*" or "aligned*" or "split" => ParseAligned(),
            _ => ParseMatrix("")
        };
    }

    private XElement ParseMatrix(string variant)
    {
        var (beg, end) = variant switch
        {
            "pmatrix" => ("(", ")"),
            "bmatrix" => ("[", "]"),
            "Bmatrix" => ("{", "}"),
            "vmatrix" => ("|", "|"),
            "Vmatrix" => ("‖", "‖"),
            _ => ("", "")
        };

        var rows = new List<List<List<XElement>>>();
        var curRow = new List<List<XElement>>();
        var curCell = new List<XElement>();

        while (CurKind != LatexToOmmlConverter.TokKind.End)
        {
            if (CurKind == LatexToOmmlConverter.TokKind.Command && Cur.Value == @"\end")
            {
                Consume();
                Expect(LatexToOmmlConverter.TokKind.LeftBrace);
                ConsumeToClose();
                break;
            }
            if (CurKind == LatexToOmmlConverter.TokKind.DoubleBackslash)
            {
                Consume();
                curRow.Add(curCell);
                rows.Add(curRow);
                curRow = []; curCell = [];
                continue;
            }
            if (CurKind == LatexToOmmlConverter.TokKind.Ampersand)
            {
                Consume();
                curRow.Add(curCell);
                curCell = [];
                continue;
            }
            curCell.AddRange(ParseAtomWithSubSup());
        }
        curRow.Add(curCell);
        if (curRow.Any(c => c.Count > 0)) rows.Add(curRow);

        var mEl = new XElement(_m + "m");
        foreach (var row in rows)
        {
            var mr = new XElement(_m + "mr");
            foreach (var cell in row)
                mr.Add(new XElement(_m + "e", cell));
            mEl.Add(mr);
        }

        if (string.IsNullOrEmpty(beg)) return mEl;

        var dPr = new XElement(_m + "dPr",
            new XElement(_m + "begChr", new XAttribute(_m + "val", beg)),
            new XElement(_m + "endChr", new XAttribute(_m + "val", end)));
        return new XElement(_m + "d", dPr, new XElement(_m + "e", mEl));
    }

    private XElement ParseAligned()
    {
        var equations = new List<List<XElement>>();
        var cur = new List<XElement>();

        while (CurKind != LatexToOmmlConverter.TokKind.End)
        {
            if (CurKind == LatexToOmmlConverter.TokKind.Command && Cur.Value == @"\end")
            {
                Consume();
                Expect(LatexToOmmlConverter.TokKind.LeftBrace);
                ConsumeToClose();
                break;
            }
            if (CurKind == LatexToOmmlConverter.TokKind.DoubleBackslash)
            {
                Consume(); equations.Add(cur); cur = []; continue;
            }
            if (CurKind == LatexToOmmlConverter.TokKind.Ampersand)
            {
                Consume(); continue; // skip alignment tab
            }
            cur.AddRange(ParseAtomWithSubSup());
        }
        equations.Add(cur);

        var eqArr = new XElement(_m + "eqArr");
        foreach (var eq in equations)
            eqArr.Add(new XElement(_m + "e", eq));
        return eqArr;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private XElement? ConsumeArg()
    {
        ParseBraceGroup();
        return null;
    }

    private void ConsumeToClose()
    {
        while (CurKind != LatexToOmmlConverter.TokKind.RightBrace &&
               CurKind != LatexToOmmlConverter.TokKind.End)
            Consume();
        Expect(LatexToOmmlConverter.TokKind.RightBrace);
    }

    private XElement MakeRun(string text) =>
        new(_m + "r", new XElement(_m + "t", text));
}
