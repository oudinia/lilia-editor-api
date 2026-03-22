using System.Text.Json.Serialization;

namespace Lilia.Core.Models.MathAst;

/// <summary>
/// Abstract base class for all math AST nodes.
/// Uses System.Text.Json polymorphic serialization with a "type" discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NumberNode), "number")]
[JsonDerivedType(typeof(SymbolNode), "symbol")]
[JsonDerivedType(typeof(VariableNode), "variable")]
[JsonDerivedType(typeof(FractionNode), "fraction")]
[JsonDerivedType(typeof(SuperscriptNode), "superscript")]
[JsonDerivedType(typeof(SubscriptNode), "subscript")]
[JsonDerivedType(typeof(SubSuperscriptNode), "subSuperscript")]
[JsonDerivedType(typeof(SqrtNode), "sqrt")]
[JsonDerivedType(typeof(OperatorNode), "operator")]
[JsonDerivedType(typeof(RelationNode), "relation")]
[JsonDerivedType(typeof(DelimiterNode), "delimiter")]
[JsonDerivedType(typeof(MatrixNode), "matrix")]
[JsonDerivedType(typeof(BigOperatorNode), "bigOperator")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(FunctionNode), "function")]
[JsonDerivedType(typeof(AccentNode), "accent")]
[JsonDerivedType(typeof(SpaceNode), "space")]
[JsonDerivedType(typeof(GroupNode), "group")]
[JsonDerivedType(typeof(RawNode), "raw")]
public abstract class MathNode { }

/// <summary>Numeric literal, e.g. "3.14", "-5".</summary>
public class NumberNode : MathNode
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>Named symbol like \alpha, \beta, \infty.</summary>
public class SymbolNode : MathNode
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Single-letter variable like x, y, n.</summary>
public class VariableNode : MathNode
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Fraction: \frac{numerator}{denominator}.</summary>
public class FractionNode : MathNode
{
    public MathNode Numerator { get; set; } = null!;
    public MathNode Denominator { get; set; } = null!;
}

/// <summary>Superscript: base^exponent.</summary>
public class SuperscriptNode : MathNode
{
    public MathNode Base { get; set; } = null!;
    public MathNode Exponent { get; set; } = null!;
}

/// <summary>Subscript: base_subscript.</summary>
public class SubscriptNode : MathNode
{
    public MathNode Base { get; set; } = null!;
    public MathNode Subscript { get; set; } = null!;
}

/// <summary>Combined subscript and superscript: base_sub^sup.</summary>
public class SubSuperscriptNode : MathNode
{
    public MathNode Base { get; set; } = null!;
    public MathNode Subscript { get; set; } = null!;
    public MathNode Superscript { get; set; } = null!;
}

/// <summary>Square root: \sqrt{radicand} or \sqrt[index]{radicand}.</summary>
public class SqrtNode : MathNode
{
    public MathNode Radicand { get; set; } = null!;
    public MathNode? Index { get; set; }
}

/// <summary>Binary or unary operator: +, -, \times, \cdot, \pm.</summary>
public class OperatorNode : MathNode
{
    public string Symbol { get; set; } = string.Empty;
}

/// <summary>Relation/comparison: =, &lt;, &gt;, \leq, \geq, \neq, \approx.</summary>
public class RelationNode : MathNode
{
    public string Symbol { get; set; } = string.Empty;
}

/// <summary>Paired delimiters: (), [], {}, \left...\right.</summary>
public class DelimiterNode : MathNode
{
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
    public MathNode Content { get; set; } = null!;
}

/// <summary>Matrix/array: \begin{pmatrix}...\end{pmatrix}.</summary>
public class MatrixNode : MathNode
{
    public List<List<MathNode>> Rows { get; set; } = [];
    public string MatrixType { get; set; } = "pmatrix";
}

/// <summary>Big operator: \sum, \prod, \int with optional bounds.</summary>
public class BigOperatorNode : MathNode
{
    public string Operator { get; set; } = string.Empty;
    public MathNode? Lower { get; set; }
    public MathNode? Upper { get; set; }
    public MathNode? Operand { get; set; }
}

/// <summary>Text inside math: \text{...}, \mathrm{...}.</summary>
public class TextNode : MathNode
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>Named math function: \sin, \cos, \log with optional argument.</summary>
public class FunctionNode : MathNode
{
    public string Name { get; set; } = string.Empty;
    public MathNode? Argument { get; set; }
}

/// <summary>Accent/decoration: \hat{x}, \bar{x}, \vec{v}.</summary>
public class AccentNode : MathNode
{
    public string Accent { get; set; } = string.Empty;
    public MathNode Base { get; set; } = null!;
}

/// <summary>Spacing command: \quad, \,, \;, etc.</summary>
public class SpaceNode : MathNode
{
    public string Size { get; set; } = string.Empty;
}

/// <summary>Sequence of math nodes (from brace groups or implicit sequences).</summary>
public class GroupNode : MathNode
{
    public List<MathNode> Children { get; set; } = [];
}

/// <summary>Unparseable LaTeX fallback. Never loses information.</summary>
public class RawNode : MathNode
{
    public string Latex { get; set; } = string.Empty;
}
