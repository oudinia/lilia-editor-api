using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Comparison tests verifying that all block types render to both LaTeX and Typst.
/// </summary>
public class LatexTypstComparisonTests
{
    private readonly RenderService _renderService;
    private readonly TypstRenderService _typstService;

    public LatexTypstComparisonTests()
    {
        var renderLogger = new Mock<ILogger<RenderService>>();
        var typstLogger = new Mock<ILogger<TypstRenderService>>();

        _renderService = new RenderService(null!, renderLogger.Object);
        _typstService = new TypstRenderService(null!, typstLogger.Object);
    }

    [Theory]
    [InlineData("paragraph", "{\"text\":\"Hello world.\"}", "Hello world.")]
    [InlineData("heading", "{\"text\":\"Intro\",\"level\":1}", "= Intro")]
    [InlineData("heading", "{\"text\":\"Sub\",\"level\":2}", "== Sub")]
    [InlineData("heading", "{\"text\":\"SubSub\",\"level\":3}", "=== SubSub")]
    [InlineData("equation", "{\"latex\":\"E = mc^2\",\"displayMode\":true}", "$ E = mc^2 $")]
    [InlineData("equation", "{\"latex\":\"x^2\",\"displayMode\":false}", "$x^2$")]
    [InlineData("code", "{\"code\":\"print('hi')\",\"language\":\"python\"}", "```python")]
    [InlineData("blockquote", "{\"text\":\"A famous quote.\"}", "#quote(block: true)")]
    [InlineData("abstract", "{\"text\":\"This paper presents...\"}", "#emph[Abstract]")]
    [InlineData("footnote", "{\"text\":\"See appendix.\"}", "#footnote")]
    [InlineData("pagebreak", "{}", "#pagebreak()")]
    [InlineData("tableofcontents", "{}", "#outline()")]
    public void Block_RendersToValidTypst(string type, string contentJson, string expectedFragment)
    {
        var block = CreateBlock(type, contentJson);
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain(expectedFragment);
    }

    [Theory]
    [InlineData("paragraph", "{\"text\":\"Hello world.\"}")]
    [InlineData("heading", "{\"text\":\"Introduction\",\"level\":1}")]
    [InlineData("equation", "{\"latex\":\"E = mc^2\",\"displayMode\":true}")]
    [InlineData("equation", "{\"latex\":\"x^2\",\"displayMode\":false}")]
    [InlineData("code", "{\"code\":\"console.log('hi')\",\"language\":\"javascript\"}")]
    [InlineData("list", "{\"items\":[\"Alpha\",\"Beta\",\"Gamma\"],\"listType\":\"unordered\"}")]
    [InlineData("list", "{\"items\":[\"First\",\"Second\"],\"listType\":\"ordered\"}")]
    [InlineData("blockquote", "{\"text\":\"To be or not to be.\"}")]
    [InlineData("theorem", "{\"theoremType\":\"theorem\",\"title\":\"Pythagoras\",\"text\":\"a^2 + b^2 = c^2\"}")]
    [InlineData("abstract", "{\"text\":\"We present a novel approach.\"}")]
    [InlineData("figure", "{\"src\":\"image.png\",\"caption\":\"A figure\"}")]
    [InlineData("table", "{\"rows\":[[\"A\",\"B\"],[\"1\",\"2\"]]}")]
    [InlineData("pagebreak", "{}")]
    [InlineData("tableofcontents", "{}")]
    [InlineData("callout", "{\"variant\":\"warning\",\"title\":\"Note\",\"text\":\"Be careful.\"}")]
    [InlineData("footnote", "{\"text\":\"See reference.\"}")]
    public void Block_ProducesBothFormats(string type, string contentJson)
    {
        var block = CreateBlock(type, contentJson);
        var latex = _renderService.RenderBlockToLatex(block);
        var typst = _typstService.RenderBlockToTypst(block);

        latex.Should().NotBeNullOrEmpty($"LaTeX output for {type} should not be empty");
        typst.Should().NotBeNullOrEmpty($"Typst output for {type} should not be empty");
    }

    [Fact]
    public void Heading_RendersCorrectLevels_InTypst()
    {
        for (var level = 1; level <= 5; level++)
        {
            var block = CreateBlock("heading", $"{{\"text\":\"Level {level}\",\"level\":{level}}}");
            var typst = _typstService.RenderBlockToTypst(block);
            var expectedPrefix = new string('=', level);
            typst.Should().StartWith($"{expectedPrefix} ");
        }
    }

    [Fact]
    public void Equation_DisplayMode_RendersWithSpaces_InTypst()
    {
        var block = CreateBlock("equation", "{\"latex\":\"\\\\int_0^1 f(x) dx\",\"displayMode\":true}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().StartWith("$ ");
        typst.Should().EndWith(" $");
    }

    [Fact]
    public void Equation_InlineMode_RendersWithoutSpaces_InTypst()
    {
        var block = CreateBlock("equation", "{\"latex\":\"x^2\",\"displayMode\":false}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Be("$x^2$");
    }

    [Fact]
    public void List_Unordered_UsesDashMarker_InTypst()
    {
        var block = CreateBlock("list", "{\"items\":[\"Alpha\",\"Beta\"],\"listType\":\"unordered\"}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("- Alpha");
        typst.Should().Contain("- Beta");
    }

    [Fact]
    public void List_Ordered_UsesPlusMarker_InTypst()
    {
        var block = CreateBlock("list", "{\"items\":[\"First\",\"Second\"],\"listType\":\"ordered\"}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("+ First");
        typst.Should().Contain("+ Second");
    }

    [Fact]
    public void Table_RendersColumnsAndCells_InTypst()
    {
        var block = CreateBlock("table", "{\"rows\":[[\"A\",\"B\"],[\"1\",\"2\"]]}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("#table(");
        typst.Should().Contain("columns: 2");
        typst.Should().Contain("[A]");
        typst.Should().Contain("[B]");
    }

    [Fact]
    public void Table_WithHeaders_RendersBoldHeaders_InTypst()
    {
        var block = CreateBlock("table", "{\"headers\":[\"Name\",\"Value\"],\"rows\":[[\"x\",\"1\"]]}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("[*Name*]");
        typst.Should().Contain("[*Value*]");
    }

    [Fact]
    public void Theorem_RendersBlockWithStyling_InTypst()
    {
        var block = CreateBlock("theorem", "{\"theoremType\":\"lemma\",\"title\":\"Key Lemma\",\"text\":\"If x then y.\"}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("*Lemma*");
        typst.Should().Contain("Key Lemma");
        typst.Should().Contain("If x then y.");
    }

    [Fact]
    public void Figure_RendersImageWithCaption_InTypst()
    {
        var block = CreateBlock("figure", "{\"src\":\"photo.jpg\",\"caption\":\"A photo\"}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("#figure(");
        typst.Should().Contain("image(");
        typst.Should().Contain("photo.jpg");
        typst.Should().Contain("A photo");
    }

    [Fact]
    public void Callout_RendersBlockWithTitle_InTypst()
    {
        var block = CreateBlock("callout", "{\"variant\":\"info\",\"title\":\"Important\",\"text\":\"Read this.\"}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("*Important*");
        typst.Should().Contain("Read this.");
    }

    [Fact]
    public void EscapeTypst_HandlesSpecialCharacters()
    {
        var result = TypstRenderService.EscapeTypst("Use #set and @ref with <angle>");
        result.Should().Contain("\\#set");
        result.Should().Contain("\\@ref");
        result.Should().Contain("\\<angle\\>");
    }

    [Fact]
    public void EmptyEquation_ReturnsComment_InTypst()
    {
        var block = CreateBlock("equation", "{\"latex\":\"\",\"displayMode\":true}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("// Empty equation");
    }

    [Fact]
    public void UnknownBlockType_ReturnsComment_InTypst()
    {
        var block = CreateBlock("nonexistent", "{}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("// Unknown block type: nonexistent");
    }

    [Fact]
    public void Algorithm_RendersAsFigure_InTypst()
    {
        var block = CreateBlock("algorithm", "{\"title\":\"Sort\",\"code\":\"for i in range(n):\\n  swap(a[i], a[j])\",\"caption\":\"Sorting algorithm\"}");
        var typst = _typstService.RenderBlockToTypst(block);
        typst.Should().Contain("#figure(");
        typst.Should().Contain("algorithm");
        typst.Should().Contain("Sorting algorithm");
    }

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
}
