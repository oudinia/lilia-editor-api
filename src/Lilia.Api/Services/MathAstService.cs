using Lilia.Core.Interfaces;
using Lilia.Core.Models.MathAst;
using Lilia.Core.Services.MathParser;

namespace Lilia.Api.Services;

public class MathAstService : IMathAstService
{
    private readonly LaTeXMathParser _parser = new();
    private readonly TypstMathEmitter _emitter = new();

    public MathNode Parse(string latex)
    {
        return _parser.Parse(latex);
    }

    public string ToTypst(MathNode node)
    {
        return _emitter.Emit(node);
    }

    public List<string> Validate(MathNode node)
    {
        var warnings = new List<string>();
        ValidateNode(node, warnings);
        return warnings;
    }

    private void ValidateNode(MathNode node, List<string> warnings)
    {
        switch (node)
        {
            case FractionNode f:
                if (IsEmpty(f.Numerator))
                    warnings.Add("Fraction has empty numerator");
                if (IsEmpty(f.Denominator))
                    warnings.Add("Fraction has empty denominator");
                ValidateNode(f.Numerator, warnings);
                ValidateNode(f.Denominator, warnings);
                break;

            case SuperscriptNode s:
                if (IsEmpty(s.Exponent))
                    warnings.Add("Superscript has empty exponent");
                ValidateNode(s.Base, warnings);
                ValidateNode(s.Exponent, warnings);
                break;

            case SubscriptNode s:
                if (IsEmpty(s.Subscript))
                    warnings.Add("Subscript has empty subscript");
                ValidateNode(s.Base, warnings);
                ValidateNode(s.Subscript, warnings);
                break;

            case SubSuperscriptNode s:
                if (IsEmpty(s.Subscript))
                    warnings.Add("Sub-superscript has empty subscript");
                if (IsEmpty(s.Superscript))
                    warnings.Add("Sub-superscript has empty superscript");
                ValidateNode(s.Base, warnings);
                ValidateNode(s.Subscript, warnings);
                ValidateNode(s.Superscript, warnings);
                break;

            case SqrtNode s:
                if (IsEmpty(s.Radicand))
                    warnings.Add("Square root has empty radicand");
                ValidateNode(s.Radicand, warnings);
                if (s.Index != null) ValidateNode(s.Index, warnings);
                break;

            case DelimiterNode d:
                if (d.Left == "." && d.Right == ".")
                    warnings.Add("Delimiter pair has both sides invisible");
                if (IsMismatchedDelimiters(d.Left, d.Right))
                    warnings.Add($"Potentially mismatched delimiters: '{d.Left}' and '{d.Right}'");
                ValidateNode(d.Content, warnings);
                break;

            case BigOperatorNode b:
                if (b.Lower != null) ValidateNode(b.Lower, warnings);
                if (b.Upper != null) ValidateNode(b.Upper, warnings);
                if (b.Operand != null) ValidateNode(b.Operand, warnings);
                break;

            case AccentNode a:
                if (IsEmpty(a.Base))
                    warnings.Add($"Accent '{a.Accent}' applied to empty base");
                ValidateNode(a.Base, warnings);
                break;

            case FunctionNode f:
                if (f.Argument != null) ValidateNode(f.Argument, warnings);
                break;

            case MatrixNode m:
                if (m.Rows.Count == 0)
                    warnings.Add("Matrix has no rows");
                else
                {
                    var colCount = m.Rows[0].Count;
                    for (var i = 1; i < m.Rows.Count; i++)
                    {
                        if (m.Rows[i].Count != colCount)
                            warnings.Add($"Matrix row {i + 1} has {m.Rows[i].Count} columns, expected {colCount}");
                    }
                    foreach (var row in m.Rows)
                        foreach (var cell in row)
                            ValidateNode(cell, warnings);
                }
                break;

            case GroupNode g:
                foreach (var child in g.Children)
                    ValidateNode(child, warnings);
                break;

            case RawNode r:
                warnings.Add($"Unparsed LaTeX fragment: '{r.Latex}'");
                break;
        }
    }

    private static bool IsEmpty(MathNode node)
    {
        return node is GroupNode { Children.Count: 0 };
    }

    private static bool IsMismatchedDelimiters(string left, string right)
    {
        // Only flag clearly mismatched pairs (ignoring invisible ".")
        if (left == "." || right == ".") return false;

        var matchingPairs = new Dictionary<string, string>
        {
            ["("] = ")", ["["] = "]", ["{"] = "}",
            ["\\langle"] = "\\rangle", ["\\lfloor"] = "\\rfloor",
            ["\\lceil"] = "\\rceil", ["\\lvert"] = "\\rvert",
            ["\\lVert"] = "\\rVert", ["|"] = "|"
        };

        if (matchingPairs.TryGetValue(left, out var expectedRight))
            return right != expectedRight;

        return false;
    }
}
