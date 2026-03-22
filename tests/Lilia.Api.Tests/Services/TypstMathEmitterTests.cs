using FluentAssertions;
using Lilia.Core.Models.MathAst;
using Lilia.Core.Services.MathParser;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for TypstMathEmitter — verifying that MathNode AST nodes are
/// correctly converted to Typst math syntax.
/// </summary>
public class TypstMathEmitterTests
{
    private readonly TypstMathEmitter _emitter = new();

    #region Simple Nodes

    [Fact]
    public void Emit_NumberNode_ReturnsValue()
    {
        var node = new NumberNode { Value = "42" };

        var result = _emitter.Emit(node);

        result.Should().Be("42");
    }

    [Fact]
    public void Emit_VariableNode_ReturnsName()
    {
        var node = new VariableNode { Name = "x" };

        var result = _emitter.Emit(node);

        result.Should().Be("x");
    }

    #endregion

    #region Fractions

    [Fact]
    public void Emit_FractionNode_ReturnsTypstFraction()
    {
        var node = new FractionNode
        {
            Numerator = new VariableNode { Name = "a" },
            Denominator = new VariableNode { Name = "b" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("(a)/(b)");
    }

    [Fact]
    public void Emit_NestedFraction_WrapsCorrectly()
    {
        var node = new FractionNode
        {
            Numerator = new NumberNode { Value = "1" },
            Denominator = new FractionNode
            {
                Numerator = new VariableNode { Name = "x" },
                Denominator = new VariableNode { Name = "y" }
            }
        };

        var result = _emitter.Emit(node);

        result.Should().Contain("(1)");
        result.Should().Contain("(x)/(y)");
    }

    #endregion

    #region Superscript and Subscript

    [Fact]
    public void Emit_SuperscriptNode_ReturnsTypstSuperscript()
    {
        var node = new SuperscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Exponent = new NumberNode { Value = "2" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("x^(2)");
    }

    [Fact]
    public void Emit_SubscriptNode_ReturnsTypstSubscript()
    {
        var node = new SubscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Subscript = new VariableNode { Name = "i" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("x_(i)");
    }

    [Fact]
    public void Emit_SubSuperscriptNode_ReturnsBoth()
    {
        var node = new SubSuperscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Subscript = new VariableNode { Name = "i" },
            Superscript = new NumberNode { Value = "2" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("x_(i)^(2)");
    }

    #endregion

    #region Greek Symbols

    [Theory]
    [InlineData("\\alpha", "alpha")]
    [InlineData("\\beta", "beta")]
    [InlineData("\\Omega", "Omega")]
    [InlineData("\\infty", "infinity")]
    [InlineData("\\partial", "diff")]
    [InlineData("\\nabla", "nabla")]
    public void Emit_GreekSymbol_ReturnsCorrectTypstName(string latexName, string expectedTypst)
    {
        var node = new SymbolNode { Name = latexName };

        var result = _emitter.Emit(node);

        result.Should().Be(expectedTypst);
    }

    #endregion

    #region Sqrt

    [Fact]
    public void Emit_SqrtNode_ReturnsSqrt()
    {
        var node = new SqrtNode
        {
            Radicand = new VariableNode { Name = "x" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("sqrt(x)");
    }

    [Fact]
    public void Emit_SqrtNodeWithIndex_ReturnsRoot()
    {
        var node = new SqrtNode
        {
            Radicand = new VariableNode { Name = "x" },
            Index = new NumberNode { Value = "3" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("root(3, x)");
    }

    #endregion

    #region Big Operators

    [Fact]
    public void Emit_BigOperatorWithBounds_ReturnsCorrectTypst()
    {
        var node = new BigOperatorNode
        {
            Operator = "\\sum",
            Lower = new GroupNode
            {
                Children =
                [
                    new VariableNode { Name = "i" },
                    new RelationNode { Symbol = "=" },
                    new NumberNode { Value = "1" }
                ]
            },
            Upper = new VariableNode { Name = "n" }
        };

        var result = _emitter.Emit(node);

        result.Should().Contain("sum");
        result.Should().Contain("_(");
        result.Should().Contain("^(");
    }

    #endregion

    #region Functions

    [Fact]
    public void Emit_FunctionNode_ReturnsTypstFunction()
    {
        var node = new FunctionNode
        {
            Name = "\\sin",
            Argument = new VariableNode { Name = "x" }
        };

        var result = _emitter.Emit(node);

        result.Should().Be("sin(x)");
    }

    [Fact]
    public void Emit_FunctionNodeWithoutArgument_ReturnsNameOnly()
    {
        var node = new FunctionNode { Name = "\\log" };

        var result = _emitter.Emit(node);

        result.Should().Be("log");
    }

    #endregion

    #region Operators and Relations

    [Theory]
    [InlineData("+", "+")]
    [InlineData("-", "-")]
    [InlineData("\\times", "times")]
    [InlineData("\\cdot", "dot")]
    public void Emit_OperatorNode_ReturnsCorrectTypst(string symbol, string expected)
    {
        var node = new OperatorNode { Symbol = symbol };

        var result = _emitter.Emit(node);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("=", "=")]
    [InlineData("\\leq", "lt.eq")]
    [InlineData("\\geq", "gt.eq")]
    [InlineData("\\neq", "eq.not")]
    [InlineData("\\in", "in")]
    public void Emit_RelationNode_ReturnsCorrectTypst(string symbol, string expected)
    {
        var node = new RelationNode { Symbol = symbol };

        var result = _emitter.Emit(node);

        result.Should().Be(expected);
    }

    #endregion

    #region Roundtrip: LaTeX Parse then Typst Emit

    [Theory]
    [InlineData("42", "42")]
    [InlineData("x", "x")]
    public void Roundtrip_SimpleExpressions_ProduceExpectedTypst(string latex, string expectedTypst)
    {
        var parser = new LaTeXMathParser();
        var ast = parser.Parse(latex);
        var result = _emitter.Emit(ast);

        result.Should().Be(expectedTypst);
    }

    [Fact]
    public void Roundtrip_Fraction_ProducesTypstFraction()
    {
        var parser = new LaTeXMathParser();
        var ast = parser.Parse(@"\frac{a}{b}");
        var result = _emitter.Emit(ast);

        result.Should().Be("(a)/(b)");
    }

    [Fact]
    public void Roundtrip_Superscript_ProducesTypstSuperscript()
    {
        var parser = new LaTeXMathParser();
        var ast = parser.Parse("x^2");
        var result = _emitter.Emit(ast);

        result.Should().Be("x^(2)");
    }

    [Fact]
    public void Roundtrip_Sqrt_ProducesTypstSqrt()
    {
        var parser = new LaTeXMathParser();
        var ast = parser.Parse(@"\sqrt{x}");
        var result = _emitter.Emit(ast);

        result.Should().Be("sqrt(x)");
    }

    [Fact]
    public void Roundtrip_Greek_ProducesTypstGreek()
    {
        var parser = new LaTeXMathParser();
        var ast = parser.Parse(@"\alpha");
        var result = _emitter.Emit(ast);

        result.Should().Be("alpha");
    }

    #endregion

    #region Matrix

    [Fact]
    public void Emit_MatrixNode_ReturnsMatSyntax()
    {
        var node = new MatrixNode
        {
            MatrixType = "pmatrix",
            Rows =
            [
                [new VariableNode { Name = "a" }, new VariableNode { Name = "b" }],
                [new VariableNode { Name = "c" }, new VariableNode { Name = "d" }]
            ]
        };

        var result = _emitter.Emit(node);

        result.Should().StartWith("mat(");
        result.Should().EndWith(")");
        result.Should().Contain("a");
        result.Should().Contain("b");
        result.Should().Contain("c");
        result.Should().Contain("d");
    }

    #endregion

    #region Text and Raw

    [Fact]
    public void Emit_TextNode_ReturnsQuotedText()
    {
        var node = new TextNode { Text = "hello world" };

        var result = _emitter.Emit(node);

        result.Should().Be("\"hello world\"");
    }

    [Fact]
    public void Emit_RawNode_ReturnsMitexFallback()
    {
        var node = new RawNode { Latex = @"\unknowncmd" };

        var result = _emitter.Emit(node);

        result.Should().Contain("mitex");
        result.Should().Contain(@"\unknowncmd");
    }

    #endregion
}
