using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Phase 1 LaTeX coverage tests:
/// 1.1 Export preamble sync with LaTeXPreamble.Packages
/// 1.3 Table column alignment
/// 1.4 Table colspan/rowspan
/// </summary>
public class LatexCoverageTests
{
    private readonly RenderService _sut;

    public LatexCoverageTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _sut = new RenderService(null!, logger.Object);
    }

    // ── 1.1 Export preamble ─────────────────────────────────────────

    [Fact]
    public void ExportPreamble_ContainsAllValidationPackages()
    {
        // Extract package names (the {name} part) from both constants
        static HashSet<string> ExtractPackageNames(string preamble)
        {
            var names = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(preamble, @"\\usepackage(\[.*?\])?\{([^}]+)\}"))
            {
                // Some lines have comma-separated packages like amsmath,amssymb,amsfonts,amsthm
                foreach (var pkg in m.Groups[2].Value.Split(','))
                    names.Add(pkg.Trim());
            }
            return names;
        }

        var exportPackages = ExtractPackageNames(LaTeXPreamble.Packages);
        var validationPackages = ExtractPackageNames(LaTeXPreamble.ValidationPackages);

        // Every package used in validation must also be present in the export preamble
        foreach (var pkg in validationPackages)
        {
            exportPackages.Should().Contain(pkg,
                $"validation package '{pkg}' should be present in LaTeXPreamble.Packages");
        }

        // The export preamble should have at least as many packages
        exportPackages.Count.Should().BeGreaterThanOrEqualTo(validationPackages.Count);
    }

    // ── 1.3 Table column alignment ──────────────────────────────────

    [Fact]
    public void Table_WithColumnAlignment_GeneratesCorrectSpec()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A","B","C"],["1","2","3"]],
                "columnAlign": ["l","c","r"]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain("{lcr}");
    }

    [Fact]
    public void Table_WithDefaultAlignment_UsesLeftForAll()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A","B","C"],["1","2","3"]]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain("{lll}");
    }

    [Fact]
    public void Table_AlignmentInHtml_AddsTextAlign()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["Header"],["Data"]],
                "columnAlign": ["c"]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("style=\"text-align: center\"");
    }

    [Fact]
    public void Table_RightAlignmentInHtml_AddsTextAlignRight()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["Header"],["Data"]],
                "columnAlign": ["r"]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("style=\"text-align: right\"");
    }

    [Fact]
    public void Table_LeftAlignmentInHtml_NoStyleAttribute()
    {
        // Left alignment is the default, so no extra style needed
        var block = CreateBlock("table", """
            {
                "rows": [["Header"],["Data"]],
                "columnAlign": ["l"]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().NotContain("text-align");
    }

    // ── 1.4 Table colspan / rowspan ─────────────────────────────────

    [Fact]
    public void Table_WithColspan_GeneratesMulticolumn()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Spanning","colspan":2}, "C"],
                    ["1","2","3"]
                ]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\multicolumn{2}{l}{Spanning}");
    }

    [Fact]
    public void Table_WithRowspan_GeneratesMultirow()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Tall","rowspan":2}, "B"],
                    ["D"]
                ]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\multirow{2}{*}{Tall}");
    }

    [Fact]
    public void Table_WithColspan_HtmlHasColspanAttribute()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Wide","colspan":3}],
                    ["A","B","C"]
                ]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("colspan=\"3\"");
    }

    [Fact]
    public void Table_WithRowspan_HtmlHasRowspanAttribute()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Tall","rowspan":2}, "B"],
                    ["D"]
                ]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("rowspan=\"2\"");
    }

    // ── 2.1 Figure placement ──────────────────────────────────────

    [Theory]
    [InlineData("here", "[H]")]
    [InlineData("top", "[t]")]
    [InlineData("bottom", "[b]")]
    [InlineData("page", "[p]")]
    [InlineData("auto", "[htbp]")]
    public void Figure_PlacementOption_GeneratesCorrectFloatSpec(string placement, string expected)
    {
        var block = CreateBlock("figure", $$"""
            {
                "src": "test.png",
                "caption": "Test",
                "placement": "{{placement}}"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain($"\\begin{{figure}}{expected}");
    }

    [Fact]
    public void Figure_DefaultPlacement_UsesHtbp()
    {
        var block = CreateBlock("figure", """
            {
                "src": "test.png",
                "caption": "Test"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{figure}[htbp]");
    }

    // ── 2.2 Equation numbering ─────────────────────────────────────

    [Fact]
    public void Equation_NumberedFalse_UsesStarredEquation()
    {
        var block = CreateBlock("equation", """
            {
                "latex": "E = mc^2",
                "displayMode": true,
                "numbered": false
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{equation*}");
        latex.Should().Contain(@"\end{equation*}");
    }

    [Fact]
    public void Equation_NumberedTrue_UsesUnstarredEquation()
    {
        var block = CreateBlock("equation", """
            {
                "latex": "E = mc^2",
                "displayMode": true,
                "numbered": true
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{equation}");
        latex.Should().NotContain(@"\begin{equation*}");
    }

    [Fact]
    public void Equation_NumberedFalseWithAlign_UsesStarredAlign()
    {
        var block = CreateBlock("equation", """
            {
                "latex": "\\begin{align}\na &= b \\\\\nc &= d\n\\end{align}",
                "displayMode": true,
                "numbered": false
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{align*}");
        latex.Should().Contain(@"\end{align*}");
    }

    [Fact]
    public void Equation_DefaultNumbered_IsTrue()
    {
        var block = CreateBlock("equation", """
            {
                "latex": "x = 1",
                "displayMode": true
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{equation}");
        latex.Should().NotContain(@"\begin{equation*}");
    }

    // ── 2.3 List label format ──────────────────────────────────────

    [Fact]
    public void List_LabelFormatAlpha_GeneratesAlphLabel()
    {
        var block = CreateBlock("list", """
            {
                "items": ["a", "b", "c"],
                "ordered": true,
                "labelFormat": "alpha"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"[label=(\alph*)]");
    }

    [Fact]
    public void List_LabelFormatRoman_GeneratesRomanLabel()
    {
        var block = CreateBlock("list", """
            {
                "items": ["a", "b", "c"],
                "ordered": true,
                "labelFormat": "roman"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"[label=(\roman*)]");
    }

    [Fact]
    public void List_LabelFormatAlphaHtml_AddsTypeAttribute()
    {
        var block = CreateBlock("list", """
            {
                "items": ["a", "b"],
                "ordered": true,
                "labelFormat": "alpha"
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("type=\"a\"");
    }

    [Fact]
    public void List_LabelFormatRomanHtml_AddsTypeAttribute()
    {
        var block = CreateBlock("list", """
            {
                "items": ["a", "b"],
                "ordered": true,
                "labelFormat": "roman"
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("type=\"i\"");
    }

    // ── 3.1 Subfigure support ──────────────────────────────────────

    [Fact]
    public void Figure_WithSubfigures_GeneratesSubfigureEnvironments()
    {
        var block = CreateBlock("figure", """
            {
                "src": "",
                "caption": "Main caption",
                "subfigures": [
                    {"src": "fig1.png", "caption": "Sub A"},
                    {"src": "fig2.png", "caption": "Sub B"}
                ]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{subfigure}");
        latex.Should().Contain(@"\end{subfigure}");
        latex.Should().Contain("Sub A");
        latex.Should().Contain("Sub B");
    }

    [Fact]
    public void Figure_WithSubfiguresHtml_RendersFlexLayout()
    {
        var block = CreateBlock("figure", """
            {
                "src": "",
                "caption": "Main caption",
                "subfigures": [
                    {"src": "fig1.png", "caption": "Sub A"},
                    {"src": "fig2.png", "caption": "Sub B"}
                ]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("subfigures");
        html.Should().Contain("subfigure");
        html.Should().Contain("Sub A");
        html.Should().Contain("Sub B");
    }

    // ── 3.2 Extended theorem types ─────────────────────────────────

    [Theory]
    [InlineData("claim")]
    [InlineData("assumption")]
    [InlineData("axiom")]
    [InlineData("conjecture")]
    [InlineData("hypothesis")]
    public void Preamble_ContainsExtendedTheoremType(string theoremType)
    {
        LaTeXPreamble.TheoremEnvironments.Should().Contain($"\\newtheorem{{{theoremType}}}");
        LaTeXPreamble.TheoremEnvironments.Should().Contain($"\\newtheorem*{{{theoremType}*}}");
    }

    // ── 3.3 Table caption and label ────────────────────────────────

    [Fact]
    public void Table_WithCaption_GeneratesCaptionCommand()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A"],["1"]],
                "caption": "My Table"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\caption{My Table}");
    }

    [Fact]
    public void Table_WithLabel_GeneratesLabelCommand()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A"],["1"]],
                "caption": "My Table",
                "label": "tab:results"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\label{tab:results}");
    }

    [Fact]
    public void Table_WithCaptionHtml_GeneratesCaptionElement()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A"],["1"]],
                "caption": "My Table"
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("<caption>My Table</caption>");
    }

    // ── 3.4 Empty equation ─────────────────────────────────────────

    [Fact]
    public void Equation_EmptyLatex_RendersAsComment()
    {
        var block = CreateBlock("equation", """
            {
                "latex": "",
                "displayMode": true
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain("% Empty equation");
        latex.Should().NotContain(@"\begin{equation}");
    }

    [Fact]
    public void Equation_WhitespaceOnlyLatex_RendersAsComment()
    {
        var block = CreateBlock("equation", """
            {
                "latex": "   ",
                "displayMode": true
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain("% Empty equation");
    }

    // ── 3.5 Algorithm block ────────────────────────────────────────

    [Fact]
    public void Algorithm_RendersWithFloatH()
    {
        var block = CreateBlock("algorithm", """
            {
                "title": "Binary Search",
                "code": "\\STATE $x \\gets 0$",
                "caption": "Binary Search",
                "label": "alg:bsearch"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\begin{algorithm}[H]");
        latex.Should().Contain(@"\caption{Binary Search}");
        latex.Should().Contain(@"\label{alg:bsearch}");
        latex.Should().Contain(@"\begin{algorithmic}");
        latex.Should().Contain(@"\end{algorithmic}");
    }

    [Fact]
    public void Algorithm_BareLines_WrappedInState()
    {
        var block = CreateBlock("algorithm", """
            {
                "title": "Test",
                "code": "x = x + 1\n\\STATE y = 2",
                "caption": "Test"
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        // Bare line should be wrapped in \STATE
        latex.Should().Contain(@"\STATE x = x + 1");
        // Already-prefixed line should not get double \STATE
        latex.Should().Contain(@"\STATE y = 2");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static Block CreateBlock(string type, string contentJson)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = type,
            Content = JsonDocument.Parse(contentJson),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }
}
