using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tests for Phase 2 block model enrichments:
/// - List: start number, nested items, backward compat
/// - Theorem: unnumbered variant, label rendering
/// - Figure: width, position
/// - Code: caption, line numbers, highlight lines
/// </summary>
public class BlockEnrichmentTests
{
    private readonly RenderService _sut;

    public BlockEnrichmentTests()
    {
        var loggerMock = new Mock<ILogger<RenderService>>();
        _sut = new RenderService(null!, loggerMock.Object);
    }

    #region List — Ordered with start number

    [Fact]
    public void RenderListToHtml_OrderedWithStart_RendersStartAttribute()
    {
        var block = CreateBlock("list", """{"listType":"ordered","start":5,"items":["A","B"]}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("<ol class=\"list\" start=\"5\">");
        result.Should().Contain("<li>A</li>");
        result.Should().Contain("<li>B</li>");
    }

    [Fact]
    public void RenderListToLatex_OrderedWithStart_RendersEnumitemOption()
    {
        var block = CreateBlock("list", """{"listType":"ordered","start":3,"items":["X","Y"]}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{enumerate}[start=3]");
        result.Should().Contain(@"\item X");
        result.Should().Contain(@"\item Y");
    }

    [Fact]
    public void RenderListToHtml_OrderedWithStartOne_NoStartAttribute()
    {
        var block = CreateBlock("list", """{"listType":"ordered","start":1,"items":["A"]}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("<ol class=\"list\">");
        result.Should().NotContain("start=");
    }

    [Fact]
    public void RenderListToLatex_OrderedWithStartOne_NoOption()
    {
        var block = CreateBlock("list", """{"listType":"ordered","start":1,"items":["A"]}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{enumerate}");
        result.Should().NotContain("[start=");
    }

    #endregion

    #region List — Nested items

    [Fact]
    public void RenderListToHtml_NestedItems_RendersNestedList()
    {
        var block = CreateBlock("list", """
        {
            "listType":"unordered",
            "items":[
                {"text":"Parent","children":[{"text":"Child 1"},{"text":"Child 2"}]},
                "Simple item"
            ]
        }
        """);
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("<li>Parent<ul><li>Child 1</li><li>Child 2</li></ul></li>");
        result.Should().Contain("<li>Simple item</li>");
    }

    [Fact]
    public void RenderListToLatex_NestedItems_RendersNestedEnvironment()
    {
        var block = CreateBlock("list", """
        {
            "listType":"ordered",
            "items":[
                {"text":"Parent","children":["Child A","Child B"]}
            ]
        }
        """);
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{enumerate}");
        result.Should().Contain(@"\item Parent");
        // Nested enumerate
        var lines = result.Split('\n').Select(l => l.Trim()).ToList();
        // Should have two \begin{enumerate}
        lines.Count(l => l.StartsWith(@"\begin{enumerate}")).Should().Be(2);
        result.Should().Contain(@"\item Child A");
    }

    #endregion

    #region List — Backward compatibility with string[]

    [Fact]
    public void RenderListToHtml_StringArrayItems_StillWorks()
    {
        var block = CreateBlock("list", """{"listType":"unordered","items":["Alpha","Beta","Gamma"]}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("<ul class=\"list\">");
        result.Should().Contain("<li>Alpha</li>");
        result.Should().Contain("<li>Beta</li>");
        result.Should().Contain("<li>Gamma</li>");
    }

    [Fact]
    public void RenderListToLatex_StringArrayItems_StillWorks()
    {
        var block = CreateBlock("list", """{"listType":"unordered","items":["Alpha","Beta"]}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{itemize}");
        result.Should().Contain(@"\item Alpha");
        result.Should().Contain(@"\item Beta");
        result.Should().Contain(@"\end{itemize}");
    }

    [Fact]
    public void RenderListToHtml_ObjectItemsWithText_StillWorks()
    {
        var block = CreateBlock("list", """{"listType":"ordered","items":[{"text":"First"},{"text":"Second"}]}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("<ol class=\"list\">");
        result.Should().Contain("<li>First</li>");
        result.Should().Contain("<li>Second</li>");
    }

    #endregion

    #region Theorem — Unnumbered variant

    [Fact]
    public void RenderTheoremToHtml_UnnumberedFalse_AddsUnnumberedClass()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"Main","text":"Proof text","numbered":false}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("unnumbered");
    }

    [Fact]
    public void RenderTheoremToHtml_NumberedDefault_NoUnnumberedClass()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"Main","text":"Proof text"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().NotContain("unnumbered");
    }

    [Fact]
    public void RenderTheoremToLatex_UnnumberedFalse_UsesStarredEnvironment()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"","text":"Some text","numbered":false}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{theorem*}");
        result.Should().Contain(@"\end{theorem*}");
    }

    [Fact]
    public void RenderTheoremToLatex_NumberedTrue_UsesNormalEnvironment()
    {
        var block = CreateBlock("theorem", """{"theoremType":"lemma","title":"","text":"Content","numbered":true}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{lemma}");
        result.Should().Contain(@"\end{lemma}");
        result.Should().NotContain("lemma*");
    }

    [Fact]
    public void RenderTheoremToLatex_NumberedDefault_UsesNormalEnvironment()
    {
        var block = CreateBlock("theorem", """{"theoremType":"definition","title":"","text":"Content"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\begin{definition}");
        result.Should().NotContain("definition*");
    }

    #endregion

    #region Theorem — Label rendering

    [Fact]
    public void RenderTheoremToLatex_WithLabel_EmitsLabel()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"Main","text":"Body","label":"thm:main"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\label{thm:main}");
    }

    [Fact]
    public void RenderTheoremToLatex_WithoutLabel_NoLabelCommand()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"","text":"Body"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().NotContain(@"\label");
    }

    [Fact]
    public void RenderTheoremToHtml_WithLabel_EmitsIdAttribute()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"","text":"Body","label":"thm:x"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("id=\"thm:x\"");
    }

    [Fact]
    public void RenderTheoremToHtml_WithoutLabel_NoIdAttribute()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"","text":"Body"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().NotContain(" id=");
    }

    #endregion

    #region Figure — Width

    [Fact]
    public void RenderFigureToHtml_WithWidth_AppliesWidthStyle()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":"","width":0.5}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("width: 50%");
    }

    [Fact]
    public void RenderFigureToHtml_DefaultWidth_Uses80Percent()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":""}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("width: 80%");
    }

    [Fact]
    public void RenderFigureToLatex_WithWidth_UsesCustomWidth()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":"","width":0.6}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"width=0.6\textwidth");
    }

    [Fact]
    public void RenderFigureToLatex_DefaultWidth_Uses08()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":""}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"width=0.8\textwidth");
    }

    #endregion

    #region Figure — Position

    [Fact]
    public void RenderFigureToHtml_PositionLeft_AppliesTextAlignLeft()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":"","position":"left"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("text-align: left");
    }

    [Fact]
    public void RenderFigureToHtml_PositionRight_AppliesTextAlignRight()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":"","position":"right"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("text-align: right");
    }

    [Fact]
    public void RenderFigureToHtml_DefaultPosition_AppliesCenter()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":""}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("text-align: center");
    }

    [Fact]
    public void RenderFigureToLatex_PositionLeft_UsesRaggedright()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":"","position":"left"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\raggedright");
        result.Should().NotContain(@"\centering");
    }

    [Fact]
    public void RenderFigureToLatex_PositionRight_UsesRaggedleft()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":"","position":"right"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\raggedleft");
    }

    [Fact]
    public void RenderFigureToLatex_DefaultPosition_UsesCentering()
    {
        var block = CreateBlock("figure", """{"src":"img.png","alt":"","caption":""}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"\centering");
    }

    #endregion

    #region Code — Caption

    [Fact]
    public void RenderCodeToHtml_WithCaption_RendersFigcaption()
    {
        var block = CreateBlock("code", """{"code":"x = 1","language":"python","caption":"Example code"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("<figcaption>Example code</figcaption>");
    }

    [Fact]
    public void RenderCodeToHtml_WithoutCaption_NoFigcaption()
    {
        var block = CreateBlock("code", """{"code":"x = 1","language":"python"}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().NotContain("<figcaption>");
    }

    [Fact]
    public void RenderCodeToLatex_WithCaption_AddsLstlistingCaption()
    {
        var block = CreateBlock("code", """{"code":"x = 1","language":"python","caption":"My listing"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain("caption={My listing}");
    }

    [Fact]
    public void RenderCodeToLatex_WithoutCaption_NoCaptionOption()
    {
        var block = CreateBlock("code", """{"code":"x = 1","language":"python"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().NotContain("caption=");
    }

    #endregion

    #region Code — Line numbers

    [Fact]
    public void RenderCodeToHtml_WithLineNumbers_AddsLineNumbersClass()
    {
        var block = CreateBlock("code", """{"code":"a\nb","language":"","lineNumbers":true}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().Contain("line-numbers");
    }

    [Fact]
    public void RenderCodeToHtml_WithoutLineNumbers_NoLineNumbersClass()
    {
        var block = CreateBlock("code", """{"code":"a","language":""}""");
        var result = _sut.RenderBlockToHtml(block);

        result.Should().NotContain("line-numbers");
    }

    [Fact]
    public void RenderCodeToLatex_WithLineNumbers_AddsNumbersLeft()
    {
        var block = CreateBlock("code", """{"code":"a","language":"python","lineNumbers":true}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain("numbers=left");
    }

    [Fact]
    public void RenderCodeToLatex_WithoutLineNumbers_NoNumbersOption()
    {
        var block = CreateBlock("code", """{"code":"a","language":"python"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().NotContain("numbers=");
    }

    #endregion

    #region Code — Highlight lines (LaTeX only)

    [Fact]
    public void RenderCodeToLatex_WithHighlightLines_AddsEmphstyle()
    {
        var block = CreateBlock("code", """{"code":"a\nb\nc","language":"python","highlightLines":[1,3]}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain(@"emphstyle=\color{yellow}");
        result.Should().Contain("emph={1,3}");
    }

    [Fact]
    public void RenderCodeToLatex_WithoutHighlightLines_NoEmphstyle()
    {
        var block = CreateBlock("code", """{"code":"a","language":"python"}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().NotContain("emphstyle");
    }

    #endregion

    #region Code — Multiple options combined

    [Fact]
    public void RenderCodeToLatex_AllOptions_CombinedCorrectly()
    {
        var block = CreateBlock("code", """{"code":"x","language":"java","caption":"Test","lineNumbers":true,"highlightLines":[2]}""");
        var result = _sut.RenderBlockToLatex(block);

        result.Should().Contain("language=java");
        result.Should().Contain("caption={Test}");
        result.Should().Contain("numbers=left");
        result.Should().Contain("emphstyle=");
    }

    #endregion

    #region LaTeX Preamble — Unnumbered theorem variants

    [Fact]
    public void LaTeXPreamble_TheoremEnvironments_ContainsStarredVariants()
    {
        var env = LaTeXPreamble.TheoremEnvironments;

        env.Should().Contain(@"\newtheorem*{theorem*}{Theorem}");
        env.Should().Contain(@"\newtheorem*{lemma*}{Lemma}");
        env.Should().Contain(@"\newtheorem*{proposition*}{Proposition}");
        env.Should().Contain(@"\newtheorem*{corollary*}{Corollary}");
        env.Should().Contain(@"\newtheorem*{definition*}{Definition}");
        env.Should().Contain(@"\newtheorem*{example*}{Example}");
        env.Should().Contain(@"\newtheorem*{remark*}{Remark}");
    }

    [Fact]
    public void LaTeXPreamble_TheoremEnvironments_StillContainsNumberedVariants()
    {
        var env = LaTeXPreamble.TheoremEnvironments;

        env.Should().Contain(@"\newtheorem{theorem}{Theorem}");
        env.Should().Contain(@"\newtheorem{lemma}{Lemma}");
        env.Should().Contain(@"\newtheorem{definition}{Definition}");
    }

    #endregion

    #region Helpers

    private static Block CreateBlock(string type, string contentJson)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = type,
            Content = JsonDocument.Parse(contentJson),
            SortOrder = 0
        };
    }

    #endregion
}
