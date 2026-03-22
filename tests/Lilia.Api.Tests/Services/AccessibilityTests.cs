using System.Xml.Linq;
using FluentAssertions;
using Lilia.Core.Models.MathAst;
using Lilia.Core.Services.Accessibility;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for MathMLGenerator and MathNarrator — verifying that MathNode AST
/// nodes are correctly converted to accessible MathML and natural-language narration.
/// </summary>
public class AccessibilityTests
{
    private readonly MathMLGenerator _mathml = new();

    #region MathML Generator — Simple Nodes

    [Fact]
    public void MathML_NumberNode_ReturnsCorrectMathML()
    {
        var node = new NumberNode { Value = "42" };

        var result = _mathml.GenerateInner(node);

        result.Should().Be("<mn>42</mn>");
    }

    [Fact]
    public void MathML_VariableNode_ReturnsCorrectMathML()
    {
        var node = new VariableNode { Name = "x" };

        var result = _mathml.GenerateInner(node);

        result.Should().Be("<mi>x</mi>");
    }

    [Fact]
    public void MathML_SymbolNode_ReturnsCorrectMathML()
    {
        var node = new SymbolNode { Name = "\\alpha" };

        var result = _mathml.GenerateInner(node);

        // Should contain the Unicode Greek alpha character
        result.Should().StartWith("<mi>");
        result.Should().EndWith("</mi>");
        result.Should().Contain("\u03B1");
    }

    [Fact]
    public void MathML_OperatorNode_ReturnsCorrectMathML()
    {
        var node = new OperatorNode { Symbol = "+" };

        var result = _mathml.GenerateInner(node);

        result.Should().Be("<mo>+</mo>");
    }

    #endregion

    #region MathML Generator — Compound Nodes

    [Fact]
    public void MathML_FractionNode_ReturnsCorrectMathML()
    {
        var node = new FractionNode
        {
            Numerator = new VariableNode { Name = "a" },
            Denominator = new VariableNode { Name = "b" }
        };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("<mfrac>");
        result.Should().Contain("</mfrac>");
        result.Should().Contain("<mi>a</mi>");
        result.Should().Contain("<mi>b</mi>");
    }

    [Fact]
    public void MathML_SuperscriptNode_ReturnsCorrectMathML()
    {
        var node = new SuperscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Exponent = new NumberNode { Value = "2" }
        };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("<msup>");
        result.Should().Contain("</msup>");
        result.Should().Contain("<mi>x</mi>");
        result.Should().Contain("<mn>2</mn>");
    }

    [Fact]
    public void MathML_SubscriptNode_ReturnsCorrectMathML()
    {
        var node = new SubscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Subscript = new VariableNode { Name = "i" }
        };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("<msub>");
        result.Should().Contain("</msub>");
    }

    [Fact]
    public void MathML_SqrtNode_ReturnsCorrectMathML()
    {
        var node = new SqrtNode
        {
            Radicand = new VariableNode { Name = "x" }
        };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("<msqrt>");
        result.Should().Contain("</msqrt>");
        result.Should().Contain("<mi>x</mi>");
    }

    [Fact]
    public void MathML_SqrtNodeWithIndex_ReturnsRootElement()
    {
        var node = new SqrtNode
        {
            Radicand = new VariableNode { Name = "x" },
            Index = new NumberNode { Value = "3" }
        };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("<mroot>");
        result.Should().Contain("</mroot>");
    }

    [Fact]
    public void MathML_MatrixNode_ReturnsTableElements()
    {
        var node = new MatrixNode
        {
            MatrixType = "pmatrix",
            Rows =
            [
                [new NumberNode { Value = "1" }, new NumberNode { Value = "0" }],
                [new NumberNode { Value = "0" }, new NumberNode { Value = "1" }]
            ]
        };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("<mtable>");
        result.Should().Contain("<mtr>");
        result.Should().Contain("<mtd>");
    }

    #endregion

    #region MathML Generator — Full Document Output

    [Fact]
    public void MathML_Generate_WrapsInMathElement()
    {
        var node = new NumberNode { Value = "42" };

        var result = _mathml.Generate(node);

        result.Should().StartWith("<math xmlns=\"http://www.w3.org/1998/Math/MathML\">");
        result.Should().EndWith("</math>");
    }

    [Fact]
    public void MathML_Generate_ProducesValidXml()
    {
        var node = new FractionNode
        {
            Numerator = new SuperscriptNode
            {
                Base = new VariableNode { Name = "x" },
                Exponent = new NumberNode { Value = "2" }
            },
            Denominator = new VariableNode { Name = "y" }
        };

        var result = _mathml.Generate(node);

        // Should parse as valid XML without throwing
        var act = () => XDocument.Parse(result);
        act.Should().NotThrow();
    }

    [Fact]
    public void MathML_Generate_EscapesSpecialCharacters()
    {
        var node = new OperatorNode { Symbol = "<" };

        var result = _mathml.GenerateInner(node);

        result.Should().Contain("&lt;");
        result.Should().NotContain("<mo><</mo>");
    }

    #endregion

    #region MathNarrator — Simple Nodes

    [Fact]
    public void Narrate_NumberNode_ReturnsValue()
    {
        var node = new NumberNode { Value = "42" };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("42");
    }

    [Fact]
    public void Narrate_VariableNode_ReturnsName()
    {
        var node = new VariableNode { Name = "x" };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("x");
    }

    [Fact]
    public void Narrate_GreekSymbol_ReturnsReadableName()
    {
        var node = new SymbolNode { Name = "\\alpha" };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("alpha");
    }

    #endregion

    #region MathNarrator — Fractions

    [Fact]
    public void Narrate_FractionNode_ReturnsOverFormat()
    {
        var node = new FractionNode
        {
            Numerator = new VariableNode { Name = "a" },
            Denominator = new VariableNode { Name = "b" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("a over b");
    }

    #endregion

    #region MathNarrator — Superscript

    [Fact]
    public void Narrate_Superscript_Squared_ReturnsSquared()
    {
        var node = new SuperscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Exponent = new NumberNode { Value = "2" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("x squared");
    }

    [Fact]
    public void Narrate_Superscript_Cubed_ReturnsCubed()
    {
        var node = new SuperscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Exponent = new NumberNode { Value = "3" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("x cubed");
    }

    [Fact]
    public void Narrate_Superscript_ArbitraryPower_ReturnsPowerOf()
    {
        var node = new SuperscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Exponent = new VariableNode { Name = "n" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("x to the power of n");
    }

    #endregion

    #region MathNarrator — Subscript

    [Fact]
    public void Narrate_Subscript_ReturnsSub()
    {
        var node = new SubscriptNode
        {
            Base = new VariableNode { Name = "x" },
            Subscript = new VariableNode { Name = "i" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("x sub i");
    }

    #endregion

    #region MathNarrator — Sqrt

    [Fact]
    public void Narrate_Sqrt_ReturnsSquareRootOf()
    {
        var node = new SqrtNode
        {
            Radicand = new VariableNode { Name = "x" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("square root of x");
    }

    [Fact]
    public void Narrate_CubeRoot_ReturnsCubeRootOf()
    {
        var node = new SqrtNode
        {
            Radicand = new VariableNode { Name = "x" },
            Index = new NumberNode { Value = "3" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("cube root of x");
    }

    #endregion

    #region MathNarrator — Complex Expressions

    [Fact]
    public void Narrate_ComplexExpression_ReturnsReadableDescription()
    {
        // x^2 + y^2 = z^2
        var node = new GroupNode
        {
            Children =
            [
                new SuperscriptNode
                {
                    Base = new VariableNode { Name = "x" },
                    Exponent = new NumberNode { Value = "2" }
                },
                new OperatorNode { Symbol = "+" },
                new SuperscriptNode
                {
                    Base = new VariableNode { Name = "y" },
                    Exponent = new NumberNode { Value = "2" }
                },
                new RelationNode { Symbol = "=" },
                new SuperscriptNode
                {
                    Base = new VariableNode { Name = "z" },
                    Exponent = new NumberNode { Value = "2" }
                }
            ]
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Contain("x squared");
        result.Should().Contain("plus");
        result.Should().Contain("y squared");
        result.Should().Contain("equals");
        result.Should().Contain("z squared");
    }

    [Fact]
    public void Narrate_BigOperator_ReturnsReadableDescription()
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

        var result = MathNarrator.Narrate(node);

        result.Should().Contain("sum");
        result.Should().Contain("from");
        result.Should().Contain("to");
        result.Should().Contain("n");
    }

    [Fact]
    public void Narrate_Matrix_ReturnsDimensions()
    {
        var node = new MatrixNode
        {
            MatrixType = "pmatrix",
            Rows =
            [
                [new NumberNode { Value = "1" }, new NumberNode { Value = "0" }],
                [new NumberNode { Value = "0" }, new NumberNode { Value = "1" }]
            ]
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Be("2 by 2 matrix");
    }

    [Fact]
    public void Narrate_Function_ReturnsFunctionOfArgument()
    {
        var node = new FunctionNode
        {
            Name = "\\sin",
            Argument = new VariableNode { Name = "x" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Contain("sin");
        result.Should().Contain("of");
        result.Should().Contain("x");
    }

    [Fact]
    public void Narrate_Accent_ReturnsBaseWithAccentName()
    {
        var node = new AccentNode
        {
            Accent = "\\hat",
            Base = new VariableNode { Name = "x" }
        };

        var result = MathNarrator.Narrate(node);

        result.Should().Contain("x");
        result.Should().Contain("hat");
    }

    [Fact]
    public void Narrate_EmptyGroup_ReturnsEmpty()
    {
        var node = new GroupNode { Children = [] };

        var result = MathNarrator.Narrate(node);

        result.Should().BeEmpty();
    }

    #endregion
}
