using System.Text;
using Lilia.Core.Models.MathAst;

namespace Lilia.Core.Services.Accessibility;

/// <summary>
/// Walks a Math AST and generates MathML Presentation markup for screen readers
/// and accessible rendering.
/// </summary>
public class MathMLGenerator
{
    private static readonly Dictionary<string, string> GreekLetters = new()
    {
        ["alpha"] = "\u03B1", ["beta"] = "\u03B2", ["gamma"] = "\u03B3",
        ["delta"] = "\u03B4", ["epsilon"] = "\u03B5", ["zeta"] = "\u03B6",
        ["eta"] = "\u03B7", ["theta"] = "\u03B8", ["iota"] = "\u03B9",
        ["kappa"] = "\u03BA", ["lambda"] = "\u03BB", ["mu"] = "\u03BC",
        ["nu"] = "\u03BD", ["xi"] = "\u03BE", ["pi"] = "\u03C0",
        ["rho"] = "\u03C1", ["sigma"] = "\u03C3", ["tau"] = "\u03C4",
        ["upsilon"] = "\u03C5", ["phi"] = "\u03C6", ["chi"] = "\u03C7",
        ["psi"] = "\u03C8", ["omega"] = "\u03C9",
        ["Alpha"] = "\u0391", ["Beta"] = "\u0392", ["Gamma"] = "\u0393",
        ["Delta"] = "\u0394", ["Epsilon"] = "\u0395", ["Zeta"] = "\u0396",
        ["Eta"] = "\u0397", ["Theta"] = "\u0398", ["Iota"] = "\u0399",
        ["Kappa"] = "\u039A", ["Lambda"] = "\u039B", ["Mu"] = "\u039C",
        ["Nu"] = "\u039D", ["Xi"] = "\u039E", ["Pi"] = "\u03A0",
        ["Rho"] = "\u03A1", ["Sigma"] = "\u03A3", ["Tau"] = "\u03A4",
        ["Upsilon"] = "\u03A5", ["Phi"] = "\u03A6", ["Chi"] = "\u03A7",
        ["Psi"] = "\u03A8", ["Omega"] = "\u03A9",
        ["varepsilon"] = "\u03B5", ["vartheta"] = "\u03D1", ["varphi"] = "\u03D5",
        ["varrho"] = "\u03F1", ["varsigma"] = "\u03C2",
        ["infty"] = "\u221E", ["nabla"] = "\u2207", ["partial"] = "\u2202",
        ["forall"] = "\u2200", ["exists"] = "\u2203", ["emptyset"] = "\u2205",
        ["ell"] = "\u2113", ["hbar"] = "\u210F", ["Re"] = "\u211C", ["Im"] = "\u2111",
    };

    private static readonly Dictionary<string, string> OperatorSymbols = new()
    {
        ["\\times"] = "\u00D7", ["\\cdot"] = "\u22C5", ["\\pm"] = "\u00B1",
        ["\\mp"] = "\u2213", ["\\div"] = "\u00F7", ["\\ast"] = "\u2217",
        ["\\star"] = "\u22C6", ["\\circ"] = "\u2218", ["\\bullet"] = "\u2022",
        ["\\oplus"] = "\u2295", ["\\otimes"] = "\u2297",
    };

    private static readonly Dictionary<string, string> RelationSymbols = new()
    {
        ["\\leq"] = "\u2264", ["\\geq"] = "\u2265", ["\\neq"] = "\u2260",
        ["\\approx"] = "\u2248", ["\\equiv"] = "\u2261", ["\\sim"] = "\u223C",
        ["\\simeq"] = "\u2243", ["\\cong"] = "\u2245", ["\\propto"] = "\u221D",
        ["\\subset"] = "\u2282", ["\\supset"] = "\u2283",
        ["\\subseteq"] = "\u2286", ["\\supseteq"] = "\u2287",
        ["\\in"] = "\u2208", ["\\ni"] = "\u220B", ["\\notin"] = "\u2209",
        ["\\ll"] = "\u226A", ["\\gg"] = "\u226B",
    };

    private static readonly Dictionary<string, string> BigOperatorSymbols = new()
    {
        ["\\sum"] = "\u2211", ["\\prod"] = "\u220F", ["\\coprod"] = "\u2210",
        ["\\int"] = "\u222B", ["\\iint"] = "\u222C", ["\\iiint"] = "\u222D",
        ["\\oint"] = "\u222E",
        ["\\bigcup"] = "\u22C3", ["\\bigcap"] = "\u22C2",
        ["\\bigvee"] = "\u22C1", ["\\bigwedge"] = "\u22C0",
        ["\\bigoplus"] = "\u2A01", ["\\bigotimes"] = "\u2A02",
    };

    /// <summary>
    /// Generate MathML Presentation markup for the given AST node.
    /// Returns a complete <c>&lt;math&gt;</c> element with the <c>xmlns</c> attribute.
    /// </summary>
    public string Generate(MathNode node)
    {
        var sb = new StringBuilder();
        sb.Append("<math xmlns=\"http://www.w3.org/1998/Math/MathML\">");
        EmitNode(node, sb);
        sb.Append("</math>");
        return sb.ToString();
    }

    /// <summary>
    /// Generate the inner MathML content without the wrapping <c>&lt;math&gt;</c> element.
    /// </summary>
    public string GenerateInner(MathNode node)
    {
        var sb = new StringBuilder();
        EmitNode(node, sb);
        return sb.ToString();
    }

    private void EmitNode(MathNode node, StringBuilder sb)
    {
        switch (node)
        {
            case NumberNode n:
                sb.Append($"<mn>{Escape(n.Value)}</mn>");
                break;

            case VariableNode v:
                sb.Append($"<mi>{Escape(v.Name)}</mi>");
                break;

            case SymbolNode s:
                var symbolName = s.Name.TrimStart('\\');
                var unicode = GreekLetters.GetValueOrDefault(symbolName, s.Name);
                sb.Append($"<mi>{Escape(unicode)}</mi>");
                break;

            case OperatorNode o:
                var opChar = OperatorSymbols.GetValueOrDefault(o.Symbol, o.Symbol);
                sb.Append($"<mo>{Escape(opChar)}</mo>");
                break;

            case RelationNode r:
                var relChar = RelationSymbols.GetValueOrDefault(r.Symbol, r.Symbol);
                sb.Append($"<mo>{Escape(relChar)}</mo>");
                break;

            case FractionNode f:
                sb.Append("<mfrac><mrow>");
                EmitNode(f.Numerator, sb);
                sb.Append("</mrow><mrow>");
                EmitNode(f.Denominator, sb);
                sb.Append("</mrow></mfrac>");
                break;

            case SuperscriptNode sup:
                sb.Append("<msup><mrow>");
                EmitNode(sup.Base, sb);
                sb.Append("</mrow><mrow>");
                EmitNode(sup.Exponent, sb);
                sb.Append("</mrow></msup>");
                break;

            case SubscriptNode sub:
                sb.Append("<msub><mrow>");
                EmitNode(sub.Base, sb);
                sb.Append("</mrow><mrow>");
                EmitNode(sub.Subscript, sb);
                sb.Append("</mrow></msub>");
                break;

            case SubSuperscriptNode ss:
                sb.Append("<msubsup><mrow>");
                EmitNode(ss.Base, sb);
                sb.Append("</mrow><mrow>");
                EmitNode(ss.Subscript, sb);
                sb.Append("</mrow><mrow>");
                EmitNode(ss.Superscript, sb);
                sb.Append("</mrow></msubsup>");
                break;

            case SqrtNode sqrt:
                if (sqrt.Index != null)
                {
                    sb.Append("<mroot><mrow>");
                    EmitNode(sqrt.Radicand, sb);
                    sb.Append("</mrow><mrow>");
                    EmitNode(sqrt.Index, sb);
                    sb.Append("</mrow></mroot>");
                }
                else
                {
                    sb.Append("<msqrt><mrow>");
                    EmitNode(sqrt.Radicand, sb);
                    sb.Append("</mrow></msqrt>");
                }
                break;

            case BigOperatorNode big:
                var bigOp = BigOperatorSymbols.GetValueOrDefault(big.Operator, big.Operator);
                if (big.Lower != null && big.Upper != null)
                {
                    sb.Append("<munderover>");
                    sb.Append($"<mo>{Escape(bigOp)}</mo>");
                    sb.Append("<mrow>");
                    EmitNode(big.Lower, sb);
                    sb.Append("</mrow><mrow>");
                    EmitNode(big.Upper, sb);
                    sb.Append("</mrow></munderover>");
                }
                else if (big.Lower != null)
                {
                    sb.Append("<munder>");
                    sb.Append($"<mo>{Escape(bigOp)}</mo>");
                    sb.Append("<mrow>");
                    EmitNode(big.Lower, sb);
                    sb.Append("</mrow></munder>");
                }
                else if (big.Upper != null)
                {
                    sb.Append("<mover>");
                    sb.Append($"<mo>{Escape(bigOp)}</mo>");
                    sb.Append("<mrow>");
                    EmitNode(big.Upper, sb);
                    sb.Append("</mrow></mover>");
                }
                else
                {
                    sb.Append($"<mo>{Escape(bigOp)}</mo>");
                }
                if (big.Operand != null)
                {
                    EmitNode(big.Operand, sb);
                }
                break;

            case MatrixNode m:
                sb.Append("<mtable>");
                foreach (var row in m.Rows)
                {
                    sb.Append("<mtr>");
                    foreach (var cell in row)
                    {
                        sb.Append("<mtd>");
                        EmitNode(cell, sb);
                        sb.Append("</mtd>");
                    }
                    sb.Append("</mtr>");
                }
                sb.Append("</mtable>");
                break;

            case TextNode t:
                sb.Append($"<mtext>{Escape(t.Text)}</mtext>");
                break;

            case FunctionNode fn:
                sb.Append($"<mi>{Escape(fn.Name)}</mi>");
                if (fn.Argument != null)
                {
                    sb.Append("<mo>&#x2061;</mo>"); // function application invisible operator
                    EmitNode(fn.Argument, sb);
                }
                break;

            case AccentNode a:
                sb.Append("<mover accent=\"true\"><mrow>");
                EmitNode(a.Base, sb);
                sb.Append("</mrow>");
                sb.Append($"<mo>{Escape(MapAccent(a.Accent))}</mo>");
                sb.Append("</mover>");
                break;

            case DelimiterNode d:
                sb.Append($"<mo>{Escape(MapDelimiter(d.Left))}</mo>");
                EmitNode(d.Content, sb);
                sb.Append($"<mo>{Escape(MapDelimiter(d.Right))}</mo>");
                break;

            case SpaceNode:
                sb.Append("<mspace width=\"0.5em\"/>");
                break;

            case GroupNode g:
                sb.Append("<mrow>");
                foreach (var child in g.Children)
                {
                    EmitNode(child, sb);
                }
                sb.Append("</mrow>");
                break;

            case RawNode raw:
                sb.Append($"<mtext>[LaTeX: {Escape(raw.Latex)}]</mtext>");
                break;

            default:
                sb.Append("<mtext>[unknown]</mtext>");
                break;
        }
    }

    private static string MapAccent(string accent)
    {
        return accent switch
        {
            "\\hat" or "hat" => "\u005E",
            "\\bar" or "bar" => "\u00AF",
            "\\vec" or "vec" => "\u2192",
            "\\dot" or "dot" => "\u02D9",
            "\\ddot" or "ddot" => "\u00A8",
            "\\tilde" or "tilde" => "\u007E",
            "\\check" or "check" => "\u02C7",
            "\\breve" or "breve" => "\u02D8",
            "\\acute" or "acute" => "\u00B4",
            "\\grave" or "grave" => "\u0060",
            "\\widehat" or "widehat" => "\u005E",
            "\\widetilde" or "widetilde" => "\u007E",
            "\\overline" or "overline" => "\u00AF",
            _ => accent
        };
    }

    private static string MapDelimiter(string delim)
    {
        return delim switch
        {
            "\\langle" => "\u27E8",
            "\\rangle" => "\u27E9",
            "\\lfloor" => "\u230A",
            "\\rfloor" => "\u230B",
            "\\lceil" => "\u2308",
            "\\rceil" => "\u2309",
            "\\lvert" or "\\lVert" => "|",
            "\\rvert" or "\\rVert" => "|",
            "\\{" => "{",
            "\\}" => "}",
            "." => "", // invisible delimiter
            _ => delim
        };
    }

    private static string Escape(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
