using System.Text;
using Lilia.Core.Models.MathAst;

namespace Lilia.Core.Services.MathParser;

/// <summary>
/// Walks a MathNode AST and emits equivalent Typst math syntax.
/// </summary>
public class TypstMathEmitter
{
    private static readonly Dictionary<string, string> GreekToTypst = new()
    {
        ["\\alpha"] = "alpha", ["\\beta"] = "beta", ["\\gamma"] = "gamma",
        ["\\delta"] = "delta", ["\\epsilon"] = "epsilon", ["\\varepsilon"] = "epsilon.alt",
        ["\\zeta"] = "zeta", ["\\eta"] = "eta", ["\\theta"] = "theta",
        ["\\vartheta"] = "theta.alt", ["\\iota"] = "iota", ["\\kappa"] = "kappa",
        ["\\lambda"] = "lambda", ["\\mu"] = "mu", ["\\nu"] = "nu",
        ["\\xi"] = "xi", ["\\pi"] = "pi", ["\\varpi"] = "pi.alt",
        ["\\rho"] = "rho", ["\\varrho"] = "rho.alt", ["\\sigma"] = "sigma",
        ["\\varsigma"] = "sigma.alt", ["\\tau"] = "tau", ["\\upsilon"] = "upsilon",
        ["\\phi"] = "phi.alt", ["\\varphi"] = "phi", ["\\chi"] = "chi",
        ["\\psi"] = "psi", ["\\omega"] = "omega",
        ["\\Gamma"] = "Gamma", ["\\Delta"] = "Delta", ["\\Theta"] = "Theta",
        ["\\Lambda"] = "Lambda", ["\\Xi"] = "Xi", ["\\Pi"] = "Pi",
        ["\\Sigma"] = "Sigma", ["\\Upsilon"] = "Upsilon", ["\\Phi"] = "Phi",
        ["\\Psi"] = "Psi", ["\\Omega"] = "Omega",
        ["\\infty"] = "infinity", ["\\partial"] = "diff", ["\\nabla"] = "nabla",
        ["\\ell"] = "ell", ["\\hbar"] = "planck.reduce",
        ["\\forall"] = "forall", ["\\exists"] = "exists",
        ["\\nexists"] = "exists.not", ["\\emptyset"] = "emptyset",
        ["\\varnothing"] = "nothing", ["\\aleph"] = "aleph",
        ["\\wp"] = "wp", ["\\Re"] = "Re", ["\\Im"] = "Im"
    };

    private static readonly Dictionary<string, string> OperatorToTypst = new()
    {
        ["+"] = "+", ["-"] = "-", ["*"] = "*", ["/"] = "/",
        ["\\times"] = "times", ["\\cdot"] = "dot", ["\\div"] = "div",
        ["\\pm"] = "plus.minus", ["\\mp"] = "minus.plus",
        ["\\star"] = "star", ["\\circ"] = "circle.stroked.small",
        ["\\bullet"] = "bullet", ["\\oplus"] = "plus.circle",
        ["\\otimes"] = "times.circle", ["\\wedge"] = "and",
        ["\\vee"] = "or", ["\\cap"] = "sect", ["\\cup"] = "union",
        ["\\setminus"] = "without", ["\\land"] = "and", ["\\lor"] = "or",
        ["\\neg"] = "not", ["\\lnot"] = "not",
        ["\\to"] = "arrow.r", ["\\rightarrow"] = "arrow.r",
        ["\\leftarrow"] = "arrow.l", ["\\Rightarrow"] = "arrow.r.double",
        ["\\Leftarrow"] = "arrow.l.double", ["\\Leftrightarrow"] = "arrow.l.r.double",
        ["\\mapsto"] = "arrow.r.bar", ["\\implies"] = "==>",
        ["\\iff"] = "<==>",
        [","] = ",", [";"] = ";", ["!"] = "!", ["|"] = "bar.v"
    };

    private static readonly Dictionary<string, string> RelationToTypst = new()
    {
        ["="] = "=", ["<"] = "<", [">"] = ">",
        ["\\leq"] = "lt.eq", ["\\le"] = "lt.eq",
        ["\\geq"] = "gt.eq", ["\\ge"] = "gt.eq",
        ["\\neq"] = "eq.not", ["\\ne"] = "eq.not",
        ["\\approx"] = "approx", ["\\equiv"] = "equiv",
        ["\\sim"] = "tilde.op", ["\\simeq"] = "tilde.eq",
        ["\\cong"] = "tilde.equiv", ["\\propto"] = "prop",
        ["\\ll"] = "lt.double", ["\\gg"] = "gt.double",
        ["\\subset"] = "subset", ["\\supset"] = "supset",
        ["\\subseteq"] = "subset.eq", ["\\supseteq"] = "supset.eq",
        ["\\in"] = "in", ["\\notin"] = "in.not", ["\\ni"] = "in.rev",
        ["\\prec"] = "prec", ["\\succ"] = "succ",
        ["\\preceq"] = "prec.eq", ["\\succeq"] = "succ.eq",
        ["\\perp"] = "perp", ["\\parallel"] = "parallel",
        ["\\mid"] = "divides", ["\\nmid"] = "divides.not",
        ["\\vdash"] = "tack.r", ["\\models"] = "tack.r.double"
    };

    private static readonly Dictionary<string, string> BigOpToTypst = new()
    {
        ["\\sum"] = "sum", ["\\prod"] = "product", ["\\coprod"] = "product.co",
        ["\\int"] = "integral", ["\\iint"] = "integral.double",
        ["\\iiint"] = "integral.triple", ["\\oint"] = "integral.cont",
        ["\\bigcup"] = "union.big", ["\\bigcap"] = "sect.big",
        ["\\bigsqcup"] = "union.sq.big", ["\\bigvee"] = "or.big",
        ["\\bigwedge"] = "and.big", ["\\bigoplus"] = "plus.circle.big",
        ["\\bigotimes"] = "times.circle.big", ["\\bigodot"] = "dot.circle.big",
        ["\\lim"] = "lim", ["\\limsup"] = "limsup", ["\\liminf"] = "liminf",
        ["\\sup"] = "sup", ["\\inf"] = "inf", ["\\max"] = "max", ["\\min"] = "min"
    };

    private static readonly Dictionary<string, string> AccentToTypst = new()
    {
        ["\\hat"] = "hat", ["\\bar"] = "macron", ["\\tilde"] = "tilde",
        ["\\vec"] = "arrow", ["\\dot"] = "dot", ["\\ddot"] = "dot.double",
        ["\\acute"] = "acute", ["\\grave"] = "grave", ["\\breve"] = "breve",
        ["\\check"] = "caron", ["\\widehat"] = "hat",
        ["\\widetilde"] = "tilde", ["\\overline"] = "overline",
        ["\\underline"] = "underline", ["\\overbrace"] = "overbrace",
        ["\\underbrace"] = "underbrace"
    };

    private static readonly Dictionary<string, string> FunctionToTypst = new()
    {
        ["\\sin"] = "sin", ["\\cos"] = "cos", ["\\tan"] = "tan",
        ["\\cot"] = "cot", ["\\sec"] = "sec", ["\\csc"] = "csc",
        ["\\arcsin"] = "arcsin", ["\\arccos"] = "arccos", ["\\arctan"] = "arctan",
        ["\\sinh"] = "sinh", ["\\cosh"] = "cosh", ["\\tanh"] = "tanh", ["\\coth"] = "coth",
        ["\\log"] = "log", ["\\ln"] = "ln", ["\\lg"] = "lg", ["\\exp"] = "exp",
        ["\\det"] = "det", ["\\dim"] = "dim", ["\\ker"] = "ker",
        ["\\hom"] = "hom", ["\\deg"] = "deg", ["\\gcd"] = "gcd",
        ["\\arg"] = "arg", ["\\mod"] = "mod"
    };

    private static readonly Dictionary<string, string> SpaceToTypst = new()
    {
        ["\\quad"] = "quad", ["\\qquad"] = "wide",
        ["\\,"] = "thin", ["\\;"] = "med", ["\\:"] = "med",
        ["\\!"] = "neg(thin)", ["\\ "] = "quad",
        ["\\enspace"] = "quad", ["\\thinspace"] = "thin",
        ["\\medspace"] = "med", ["\\thickspace"] = "thick"
    };

    private static readonly Dictionary<string, (string left, string right)> DelimiterToTypst = new()
    {
        ["("] = ("(", ")"), [")"] = ("(", ")"),
        ["["] = ("[", "]"), ["]"] = ("[", "]"),
        ["{"] = ("{", "}"), ["}"] = ("{", "}"),
        ["|"] = ("|", "|"),
        ["\\langle"] = ("angle.l", "angle.r"),
        ["\\rangle"] = ("angle.l", "angle.r"),
        ["\\lfloor"] = ("floor.l", "floor.r"),
        ["\\rfloor"] = ("floor.l", "floor.r"),
        ["\\lceil"] = ("ceil.l", "ceil.r"),
        ["\\rceil"] = ("ceil.l", "ceil.r"),
        ["\\lvert"] = ("bar.v", "bar.v"),
        ["\\rvert"] = ("bar.v", "bar.v"),
        ["\\lVert"] = ("bar.v.double", "bar.v.double"),
        ["\\rVert"] = ("bar.v.double", "bar.v.double"),
        ["."] = ("", "") // invisible delimiter
    };

    private static readonly Dictionary<string, string> MatrixToTypst = new()
    {
        ["pmatrix"] = "pmat", ["bmatrix"] = "bmat",
        ["vmatrix"] = "vmat", ["Vmatrix"] = "dmat",
        ["Bmatrix"] = "bmat", ["matrix"] = "mat",
        ["smallmatrix"] = "mat"
    };

    /// <summary>
    /// Convert a MathNode AST to Typst math syntax.
    /// </summary>
    public string Emit(MathNode node)
    {
        return node switch
        {
            NumberNode n => EmitNumber(n),
            SymbolNode s => EmitSymbol(s),
            VariableNode v => v.Name,
            FractionNode f => EmitFraction(f),
            SuperscriptNode s => EmitSuperscript(s),
            SubscriptNode s => EmitSubscript(s),
            SubSuperscriptNode s => EmitSubSuperscript(s),
            SqrtNode s => EmitSqrt(s),
            OperatorNode o => EmitOperator(o),
            RelationNode r => EmitRelation(r),
            DelimiterNode d => EmitDelimiter(d),
            MatrixNode m => EmitMatrix(m),
            BigOperatorNode b => EmitBigOperator(b),
            TextNode t => EmitText(t),
            FunctionNode f => EmitFunction(f),
            AccentNode a => EmitAccent(a),
            SpaceNode s => EmitSpace(s),
            GroupNode g => EmitGroup(g),
            RawNode r => EmitRaw(r),
            _ => ""
        };
    }

    private static string EmitNumber(NumberNode n) => n.Value;

    private string EmitSymbol(SymbolNode s)
    {
        return GreekToTypst.GetValueOrDefault(s.Name, s.Name.TrimStart('\\'));
    }

    private string EmitFraction(FractionNode f)
    {
        var num = Emit(f.Numerator);
        var den = Emit(f.Denominator);
        return $"({num})/({den})";
    }

    private string EmitSuperscript(SuperscriptNode s)
    {
        var b = WrapIfComplex(s.Base);
        var e = WrapIfComplex(s.Exponent);
        return $"{b}^({e})";
    }

    private string EmitSubscript(SubscriptNode s)
    {
        var b = WrapIfComplex(s.Base);
        var sub = WrapIfComplex(s.Subscript);
        return $"{b}_({sub})";
    }

    private string EmitSubSuperscript(SubSuperscriptNode s)
    {
        var b = WrapIfComplex(s.Base);
        var sub = WrapIfComplex(s.Subscript);
        var sup = WrapIfComplex(s.Superscript);
        return $"{b}_({sub})^({sup})";
    }

    private string EmitSqrt(SqrtNode s)
    {
        var radicand = Emit(s.Radicand);
        if (s.Index != null)
        {
            var index = Emit(s.Index);
            return $"root({index}, {radicand})";
        }
        return $"sqrt({radicand})";
    }

    private static string EmitOperator(OperatorNode o)
    {
        return OperatorToTypst.GetValueOrDefault(o.Symbol, o.Symbol);
    }

    private static string EmitRelation(RelationNode r)
    {
        return RelationToTypst.GetValueOrDefault(r.Symbol, r.Symbol);
    }

    private string EmitDelimiter(DelimiterNode d)
    {
        var content = Emit(d.Content);

        var left = MapDelimSide(d.Left, isLeft: true);
        var right = MapDelimSide(d.Right, isLeft: false);

        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            return content;

        return $"lr({left}{content}{right})";
    }

    private static string MapDelimSide(string delim, bool isLeft)
    {
        if (delim == ".") return ""; // invisible

        if (DelimiterToTypst.TryGetValue(delim, out var pair))
            return isLeft ? pair.left : pair.right;

        return delim;
    }

    private string EmitMatrix(MatrixNode m)
    {
        // cases environment
        if (m.MatrixType == "cases")
        {
            var sb = new StringBuilder("cases(");
            for (var i = 0; i < m.Rows.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(string.Join(" ", m.Rows[i].Select(Emit)));
            }
            sb.Append(')');
            return sb.ToString();
        }

        // aligned/gathered — emit lines separated by \
        if (m.MatrixType is "aligned" or "gathered")
        {
            var lines = m.Rows.Select(row => string.Join(" & ", row.Select(Emit)));
            return string.Join(" \\\\ ", lines);
        }

        // Standard matrix
        var matCmd = MatrixToTypst.GetValueOrDefault(m.MatrixType, "mat");
        var matSb = new StringBuilder($"mat(");

        for (var i = 0; i < m.Rows.Count; i++)
        {
            if (i > 0) matSb.Append("; ");
            matSb.Append(string.Join(", ", m.Rows[i].Select(Emit)));
        }

        matSb.Append(')');
        return matSb.ToString();
    }

    private string EmitBigOperator(BigOperatorNode b)
    {
        var op = BigOpToTypst.GetValueOrDefault(b.Operator, b.Operator.TrimStart('\\'));
        var sb = new StringBuilder(op);

        if (b.Lower != null)
            sb.Append($"_({Emit(b.Lower)})");
        if (b.Upper != null)
            sb.Append($"^({Emit(b.Upper)})");

        if (b.Operand != null)
            sb.Append($" {Emit(b.Operand)}");

        return sb.ToString();
    }

    private static string EmitText(TextNode t) => $"\"{t.Text}\"";

    private string EmitFunction(FunctionNode f)
    {
        var name = FunctionToTypst.GetValueOrDefault(f.Name, f.Name.TrimStart('\\'));

        if (f.Argument != null)
            return $"{name}({Emit(f.Argument)})";

        return name;
    }

    private string EmitAccent(AccentNode a)
    {
        var accentName = AccentToTypst.GetValueOrDefault(a.Accent, a.Accent.TrimStart('\\'));
        var baseStr = Emit(a.Base);
        return $"{accentName}({baseStr})";
    }

    private static string EmitSpace(SpaceNode s)
    {
        if (SpaceToTypst.TryGetValue(s.Size, out var typst))
            return $"#h({typst})";
        return " ";
    }

    private string EmitGroup(GroupNode g)
    {
        return string.Join(" ", g.Children.Select(Emit));
    }

    private static string EmitRaw(RawNode r) => $"mitex(`{r.Latex}`)";

    /// <summary>
    /// Wrap the emission of a node in parens if it produces multi-token output.
    /// </summary>
    private string WrapIfComplex(MathNode node)
    {
        if (node is GroupNode { Children.Count: > 1 })
            return Emit(node);

        return Emit(node);
    }
}
