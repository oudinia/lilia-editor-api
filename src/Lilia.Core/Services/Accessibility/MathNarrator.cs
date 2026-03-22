using System.Text;
using Lilia.Core.Models.MathAst;

namespace Lilia.Core.Services.Accessibility;

/// <summary>
/// Walks a Math AST and generates natural-language narration suitable for
/// screen readers and text-to-speech output.
/// </summary>
public static class MathNarrator
{
    private static readonly Dictionary<string, string> GreekNames = new()
    {
        ["alpha"] = "alpha", ["beta"] = "beta", ["gamma"] = "gamma",
        ["delta"] = "delta", ["epsilon"] = "epsilon", ["zeta"] = "zeta",
        ["eta"] = "eta", ["theta"] = "theta", ["iota"] = "iota",
        ["kappa"] = "kappa", ["lambda"] = "lambda", ["mu"] = "mu",
        ["nu"] = "nu", ["xi"] = "xi", ["pi"] = "pi",
        ["rho"] = "rho", ["sigma"] = "sigma", ["tau"] = "tau",
        ["upsilon"] = "upsilon", ["phi"] = "phi", ["chi"] = "chi",
        ["psi"] = "psi", ["omega"] = "omega",
        ["Alpha"] = "capital Alpha", ["Beta"] = "capital Beta", ["Gamma"] = "capital Gamma",
        ["Delta"] = "capital Delta", ["Epsilon"] = "capital Epsilon", ["Zeta"] = "capital Zeta",
        ["Eta"] = "capital Eta", ["Theta"] = "capital Theta", ["Iota"] = "capital Iota",
        ["Kappa"] = "capital Kappa", ["Lambda"] = "capital Lambda", ["Mu"] = "capital Mu",
        ["Nu"] = "capital Nu", ["Xi"] = "capital Xi", ["Pi"] = "capital Pi",
        ["Rho"] = "capital Rho", ["Sigma"] = "capital Sigma", ["Tau"] = "capital Tau",
        ["Upsilon"] = "capital Upsilon", ["Phi"] = "capital Phi", ["Chi"] = "capital Chi",
        ["Psi"] = "capital Psi", ["Omega"] = "capital Omega",
        ["varepsilon"] = "epsilon", ["vartheta"] = "theta", ["varphi"] = "phi",
        ["varrho"] = "rho", ["varsigma"] = "sigma",
        ["infty"] = "infinity", ["nabla"] = "nabla", ["partial"] = "partial",
        ["forall"] = "for all", ["exists"] = "there exists", ["emptyset"] = "empty set",
        ["ell"] = "ell", ["hbar"] = "h bar", ["Re"] = "real part", ["Im"] = "imaginary part",
    };

    private static readonly Dictionary<string, string> OperatorWords = new()
    {
        ["+"] = "plus", ["-"] = "minus",
        ["\\times"] = "times", ["\\cdot"] = "times",
        ["\\pm"] = "plus or minus", ["\\mp"] = "minus or plus",
        ["\\div"] = "divided by", ["\\ast"] = "times",
        ["\\star"] = "star", ["\\circ"] = "composed with",
        ["\\bullet"] = "bullet", ["\\oplus"] = "direct sum",
        ["\\otimes"] = "tensor product",
    };

    private static readonly Dictionary<string, string> RelationWords = new()
    {
        ["="] = "equals", ["<"] = "less than", [">"] = "greater than",
        ["\\leq"] = "less than or equal to", ["\\geq"] = "greater than or equal to",
        ["\\neq"] = "not equal to", ["\\approx"] = "approximately equal to",
        ["\\equiv"] = "is equivalent to", ["\\sim"] = "is similar to",
        ["\\simeq"] = "is similar or equal to", ["\\cong"] = "is congruent to",
        ["\\propto"] = "is proportional to",
        ["\\subset"] = "is a subset of", ["\\supset"] = "is a superset of",
        ["\\subseteq"] = "is a subset of or equal to", ["\\supseteq"] = "is a superset of or equal to",
        ["\\in"] = "is an element of", ["\\ni"] = "contains",
        ["\\notin"] = "is not an element of",
        ["\\ll"] = "is much less than", ["\\gg"] = "is much greater than",
    };

    private static readonly Dictionary<string, string> BigOperatorWords = new()
    {
        ["\\sum"] = "sum", ["\\prod"] = "product", ["\\coprod"] = "coproduct",
        ["\\int"] = "integral", ["\\iint"] = "double integral", ["\\iiint"] = "triple integral",
        ["\\oint"] = "contour integral",
        ["\\bigcup"] = "union", ["\\bigcap"] = "intersection",
        ["\\bigvee"] = "disjunction", ["\\bigwedge"] = "conjunction",
        ["\\bigoplus"] = "direct sum", ["\\bigotimes"] = "tensor product",
    };

    private static readonly Dictionary<string, string> AccentWords = new()
    {
        ["\\hat"] = "hat", ["hat"] = "hat",
        ["\\bar"] = "bar", ["bar"] = "bar",
        ["\\vec"] = "vector", ["vec"] = "vector",
        ["\\dot"] = "dot", ["dot"] = "dot",
        ["\\ddot"] = "double dot", ["ddot"] = "double dot",
        ["\\tilde"] = "tilde", ["tilde"] = "tilde",
        ["\\check"] = "check", ["check"] = "check",
        ["\\breve"] = "breve", ["breve"] = "breve",
        ["\\acute"] = "acute", ["acute"] = "acute",
        ["\\grave"] = "grave", ["grave"] = "grave",
        ["\\widehat"] = "hat", ["widehat"] = "hat",
        ["\\widetilde"] = "tilde", ["widetilde"] = "tilde",
        ["\\overline"] = "overline", ["overline"] = "overline",
    };

    /// <summary>
    /// Generate a natural-language narration for the given AST node.
    /// </summary>
    public static string Narrate(MathNode node)
    {
        return NarrateNode(node).Trim();
    }

    private static string NarrateNode(MathNode node)
    {
        return node switch
        {
            NumberNode n => n.Value,
            VariableNode v => v.Name,
            SymbolNode s => NarrateSymbol(s),
            OperatorNode o => NarrateOperator(o),
            RelationNode r => NarrateRelation(r),
            FractionNode f => NarrateFraction(f),
            SuperscriptNode sup => NarrateSuperscript(sup),
            SubscriptNode sub => NarrateSubscript(sub),
            SubSuperscriptNode ss => NarrateSubSuperscript(ss),
            SqrtNode sqrt => NarrateSqrt(sqrt),
            BigOperatorNode big => NarrateBigOperator(big),
            MatrixNode m => NarrateMatrix(m),
            TextNode t => t.Text,
            FunctionNode fn => NarrateFunction(fn),
            AccentNode a => NarrateAccent(a),
            DelimiterNode d => NarrateDelimiter(d),
            SpaceNode => " ",
            GroupNode g => NarrateGroup(g),
            RawNode => "mathematical expression",
            _ => "unknown expression"
        };
    }

    private static string NarrateSymbol(SymbolNode s)
    {
        var name = s.Name.TrimStart('\\');
        return GreekNames.GetValueOrDefault(name, name);
    }

    private static string NarrateOperator(OperatorNode o)
    {
        return OperatorWords.GetValueOrDefault(o.Symbol, o.Symbol);
    }

    private static string NarrateRelation(RelationNode r)
    {
        return RelationWords.GetValueOrDefault(r.Symbol, r.Symbol);
    }

    private static string NarrateFraction(FractionNode f)
    {
        var num = NarrateNode(f.Numerator);
        var den = NarrateNode(f.Denominator);
        return $"{num} over {den}";
    }

    private static string NarrateSuperscript(SuperscriptNode sup)
    {
        var baseText = NarrateNode(sup.Base);
        var expText = NarrateNode(sup.Exponent);

        return expText switch
        {
            "2" => $"{baseText} squared",
            "3" => $"{baseText} cubed",
            _ => $"{baseText} to the power of {expText}"
        };
    }

    private static string NarrateSubscript(SubscriptNode sub)
    {
        var baseText = NarrateNode(sub.Base);
        var subText = NarrateNode(sub.Subscript);
        return $"{baseText} sub {subText}";
    }

    private static string NarrateSubSuperscript(SubSuperscriptNode ss)
    {
        var baseText = NarrateNode(ss.Base);
        var subText = NarrateNode(ss.Subscript);
        var supText = NarrateNode(ss.Superscript);
        return $"{baseText} sub {subText} to the power of {supText}";
    }

    private static string NarrateSqrt(SqrtNode sqrt)
    {
        var content = NarrateNode(sqrt.Radicand);

        if (sqrt.Index == null)
            return $"square root of {content}";

        var indexText = NarrateNode(sqrt.Index);
        return indexText switch
        {
            "3" => $"cube root of {content}",
            _ => $"{indexText}th root of {content}"
        };
    }

    private static string NarrateBigOperator(BigOperatorNode big)
    {
        var opName = BigOperatorWords.GetValueOrDefault(big.Operator, big.Operator);
        var sb = new StringBuilder(opName);

        if (big.Lower != null && big.Upper != null)
        {
            sb.Append($" from {NarrateNode(big.Lower)} to {NarrateNode(big.Upper)}");
        }
        else if (big.Lower != null)
        {
            sb.Append($" from {NarrateNode(big.Lower)}");
        }
        else if (big.Upper != null)
        {
            sb.Append($" to {NarrateNode(big.Upper)}");
        }

        if (big.Operand != null)
        {
            sb.Append($" of {NarrateNode(big.Operand)}");
        }

        return sb.ToString();
    }

    private static string NarrateMatrix(MatrixNode m)
    {
        var rows = m.Rows.Count;
        var cols = m.Rows.Count > 0 ? m.Rows[0].Count : 0;
        return $"{rows} by {cols} matrix";
    }

    private static string NarrateFunction(FunctionNode fn)
    {
        if (fn.Argument == null)
            return fn.Name;

        var arg = NarrateNode(fn.Argument);
        return $"{fn.Name} of {arg}";
    }

    private static string NarrateAccent(AccentNode a)
    {
        var baseText = NarrateNode(a.Base);
        var accentName = AccentWords.GetValueOrDefault(a.Accent, a.Accent);
        return $"{baseText} {accentName}";
    }

    private static string NarrateDelimiter(DelimiterNode d)
    {
        var content = NarrateNode(d.Content);
        var left = MapDelimiterWord(d.Left, isLeft: true);
        var right = MapDelimiterWord(d.Right, isLeft: false);

        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            return content;

        return $"{left}{content}{right}".Trim();
    }

    private static string MapDelimiterWord(string delim, bool isLeft)
    {
        return delim switch
        {
            "(" => isLeft ? "open paren " : " close paren",
            ")" => isLeft ? "open paren " : " close paren",
            "[" => isLeft ? "open bracket " : " close bracket",
            "]" => isLeft ? "open bracket " : " close bracket",
            "{" or "\\{" => isLeft ? "open brace " : " close brace",
            "}" or "\\}" => isLeft ? "open brace " : " close brace",
            "|" or "\\lvert" or "\\rvert" => isLeft ? "absolute value of " : "",
            "\\lVert" or "\\rVert" => isLeft ? "norm of " : "",
            "\\langle" => "open angle bracket ",
            "\\rangle" => " close angle bracket",
            "\\lfloor" => "floor of ",
            "\\rfloor" => "",
            "\\lceil" => "ceiling of ",
            "\\rceil" => "",
            "." => "", // invisible delimiter
            _ => delim
        };
    }

    private static string NarrateGroup(GroupNode g)
    {
        if (g.Children.Count == 0)
            return string.Empty;

        var parts = g.Children.Select(NarrateNode);
        return string.Join(" ", parts);
    }
}
