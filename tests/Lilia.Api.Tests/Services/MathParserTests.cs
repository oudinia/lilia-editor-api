using FluentAssertions;
using Lilia.Core.Models.MathAst;
using Lilia.Core.Services.MathParser;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for LaTeXMathParser — verifying that LaTeX math strings are parsed
/// into the correct AST node types.
/// </summary>
public class MathParserTests
{
    private readonly LaTeXMathParser _parser = new();

    #region Simple Atoms

    [Fact]
    public void Parse_SimpleNumber_ReturnsNumberNode()
    {
        var result = _parser.Parse("42");

        result.Should().BeOfType<NumberNode>();
        ((NumberNode)result).Value.Should().Be("42");
    }

    [Fact]
    public void Parse_DecimalNumber_ReturnsNumberNode()
    {
        var result = _parser.Parse("3.14");

        result.Should().BeOfType<NumberNode>();
        ((NumberNode)result).Value.Should().Be("3.14");
    }

    [Fact]
    public void Parse_Variable_ReturnsVariableNode()
    {
        var result = _parser.Parse("x");

        result.Should().BeOfType<VariableNode>();
        ((VariableNode)result).Name.Should().Be("x");
    }

    #endregion

    #region Fractions

    [Fact]
    public void Parse_Fraction_ReturnsFractionNode()
    {
        var result = _parser.Parse(@"\frac{a}{b}");

        result.Should().BeOfType<FractionNode>();
        var frac = (FractionNode)result;
        frac.Numerator.Should().BeOfType<VariableNode>();
        frac.Denominator.Should().BeOfType<VariableNode>();
        ((VariableNode)frac.Numerator).Name.Should().Be("a");
        ((VariableNode)frac.Denominator).Name.Should().Be("b");
    }

    #endregion

    #region Superscript and Subscript

    [Fact]
    public void Parse_Superscript_ReturnsSuperscriptNode()
    {
        var result = _parser.Parse("x^2");

        result.Should().BeOfType<SuperscriptNode>();
        var sup = (SuperscriptNode)result;
        sup.Base.Should().BeOfType<VariableNode>();
        sup.Exponent.Should().BeOfType<NumberNode>();
        ((NumberNode)sup.Exponent).Value.Should().Be("2");
    }

    [Fact]
    public void Parse_Subscript_ReturnsSubscriptNode()
    {
        var result = _parser.Parse("x_i");

        result.Should().BeOfType<SubscriptNode>();
        var sub = (SubscriptNode)result;
        sub.Base.Should().BeOfType<VariableNode>();
        sub.Subscript.Should().BeOfType<VariableNode>();
        ((VariableNode)sub.Subscript).Name.Should().Be("i");
    }

    [Fact]
    public void Parse_BothSubscriptAndSuperscript_ReturnsSubSuperscriptNode()
    {
        var result = _parser.Parse("x_i^2");

        result.Should().BeOfType<SubSuperscriptNode>();
        var ss = (SubSuperscriptNode)result;
        ((VariableNode)ss.Base).Name.Should().Be("x");
    }

    #endregion

    #region Sqrt

    [Fact]
    public void Parse_Sqrt_ReturnsSqrtNode()
    {
        var result = _parser.Parse(@"\sqrt{x}");

        result.Should().BeOfType<SqrtNode>();
        var sqrt = (SqrtNode)result;
        sqrt.Radicand.Should().BeOfType<VariableNode>();
        sqrt.Index.Should().BeNull();
    }

    [Fact]
    public void Parse_SqrtWithIndex_ReturnsSqrtNodeWithIndex()
    {
        var result = _parser.Parse(@"\sqrt[3]{x}");

        result.Should().BeOfType<SqrtNode>();
        var sqrt = (SqrtNode)result;
        sqrt.Radicand.Should().BeOfType<VariableNode>();
        sqrt.Index.Should().NotBeNull();
        sqrt.Index.Should().BeOfType<NumberNode>();
        ((NumberNode)sqrt.Index!).Value.Should().Be("3");
    }

    #endregion

    #region Greek Letters

    [Theory]
    [InlineData(@"\alpha", "\\alpha")]
    [InlineData(@"\beta", "\\beta")]
    [InlineData(@"\gamma", "\\gamma")]
    [InlineData(@"\Omega", "\\Omega")]
    [InlineData(@"\infty", "\\infty")]
    public void Parse_GreekLetter_ReturnsSymbolNode(string latex, string expectedName)
    {
        var result = _parser.Parse(latex);

        result.Should().BeOfType<SymbolNode>();
        ((SymbolNode)result).Name.Should().Be(expectedName);
    }

    #endregion

    #region Nested Expressions

    [Fact]
    public void Parse_NestedFraction_ReturnsCorrectTree()
    {
        // \frac{x^2}{y_i}
        var result = _parser.Parse(@"\frac{x^2}{y_i}");

        result.Should().BeOfType<FractionNode>();
        var frac = (FractionNode)result;

        frac.Numerator.Should().BeOfType<SuperscriptNode>();
        var sup = (SuperscriptNode)frac.Numerator;
        ((VariableNode)sup.Base).Name.Should().Be("x");
        ((NumberNode)sup.Exponent).Value.Should().Be("2");

        frac.Denominator.Should().BeOfType<SubscriptNode>();
        var sub = (SubscriptNode)frac.Denominator;
        ((VariableNode)sub.Base).Name.Should().Be("y");
        ((VariableNode)sub.Subscript).Name.Should().Be("i");
    }

    #endregion

    #region Matrix

    [Fact]
    public void Parse_Matrix_ReturnsMatrixNode()
    {
        var latex = @"\begin{pmatrix} a & b \\ c & d \end{pmatrix}";
        var result = _parser.Parse(latex);

        result.Should().BeOfType<MatrixNode>();
        var matrix = (MatrixNode)result;
        matrix.MatrixType.Should().Be("pmatrix");
        matrix.Rows.Should().HaveCount(2);
        matrix.Rows[0].Should().HaveCount(2);
        matrix.Rows[1].Should().HaveCount(2);
    }

    #endregion

    #region Big Operators

    [Fact]
    public void Parse_Sum_ReturnsBigOperatorNode()
    {
        var result = _parser.Parse(@"\sum_{i=1}^{n}");

        result.Should().BeOfType<BigOperatorNode>();
        var big = (BigOperatorNode)result;
        big.Operator.Should().Be("\\sum");
        big.Lower.Should().NotBeNull();
        big.Upper.Should().NotBeNull();
    }

    [Fact]
    public void Parse_Integral_ReturnsBigOperatorNode()
    {
        var result = _parser.Parse(@"\int_{0}^{1}");

        result.Should().BeOfType<BigOperatorNode>();
        var big = (BigOperatorNode)result;
        big.Operator.Should().Be("\\int");
        big.Lower.Should().NotBeNull();
        big.Upper.Should().NotBeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_EmptyString_ReturnsGroupNode()
    {
        var result = _parser.Parse("");

        result.Should().BeOfType<GroupNode>();
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsGroupNode()
    {
        var result = _parser.Parse("   ");

        result.Should().BeOfType<GroupNode>();
    }

    [Fact]
    public void Parse_InvalidLatex_DoesNotThrow()
    {
        // Unbalanced braces — should not crash
        var act = () => _parser.Parse(@"\frac{x}{");

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_UnknownCommand_HandledGracefully()
    {
        var result = _parser.Parse(@"\unknowncmd{arg}");

        result.Should().NotBeNull();
        // Unknown commands are wrapped in RawNode
        result.Should().BeOfType<RawNode>();
    }

    [Fact]
    public void Parse_Operators_ReturnsGroupWithOperatorNodes()
    {
        var result = _parser.Parse("a + b");

        // a + b produces 3 nodes: variable, operator, variable
        result.Should().BeOfType<GroupNode>();
        var group = (GroupNode)result;
        group.Children.Should().HaveCount(3);
        group.Children[0].Should().BeOfType<VariableNode>();
        group.Children[1].Should().BeOfType<OperatorNode>();
        group.Children[2].Should().BeOfType<VariableNode>();
    }

    [Fact]
    public void Parse_Function_ReturnsFunctionNode()
    {
        var result = _parser.Parse(@"\sin{x}");

        result.Should().BeOfType<FunctionNode>();
        var fn = (FunctionNode)result;
        fn.Name.Should().Be("\\sin");
        fn.Argument.Should().BeOfType<VariableNode>();
    }

    [Fact]
    public void Parse_FunctionWithParens_ReturnsFunctionNodeWithArgument()
    {
        // \sin(x) — the parser wraps the paren content as the function argument
        var result = _parser.Parse(@"\sin(x)");

        result.Should().BeOfType<FunctionNode>();
        var fn = (FunctionNode)result;
        fn.Name.Should().Be("\\sin");
        fn.Argument.Should().NotBeNull();
    }

    [Fact]
    public void Parse_Accent_ReturnsAccentNode()
    {
        var result = _parser.Parse(@"\hat{x}");

        result.Should().BeOfType<AccentNode>();
        var accent = (AccentNode)result;
        accent.Accent.Should().Be("\\hat");
        accent.Base.Should().BeOfType<VariableNode>();
    }

    #endregion
}
