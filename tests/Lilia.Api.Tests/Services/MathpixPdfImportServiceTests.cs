using FluentAssertions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Services;

public class MathpixPdfImportServiceTests
{
    private static readonly IOptions<MathpixOptions> DefaultOptions = Options.Create(
        new MathpixOptions { PollIntervalMs = 100, TimeoutSeconds = 5 });

    private static MathpixPdfImportService CreateService(string markdown)
    {
        var mockClient = new Mock<IMathpixClient>();
        mockClient.Setup(c => c.SubmitPdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-pdf-id");
        mockClient.Setup(c => c.WaitForCompletionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(markdown);
        mockClient.Setup(c => c.GetImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        return new MathpixPdfImportService(mockClient.Object, DefaultOptions, NullLogger<MathpixPdfImportService>.Instance);
    }

    [Fact]
    public void CanParse_PdfFile_ReturnsTrue()
    {
        var service = CreateService("");
        service.CanParse("document.pdf").Should().BeTrue();
        service.CanParse("DOCUMENT.PDF").Should().BeTrue();
    }

    [Fact]
    public void CanParse_NonPdfFile_ReturnsFalse()
    {
        var service = CreateService("");
        service.CanParse("document.docx").Should().BeFalse();
        service.CanParse("document.tex").Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_Heading_MapsToImportHeading()
    {
        var service = CreateService("# Introduction\n\nSome text");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements[0].Should().BeOfType<ImportHeading>();
        var heading = (ImportHeading)result.Elements[0];
        heading.Text.Should().Be("Introduction");
        heading.Level.Should().Be(1);
    }

    [Fact]
    public async Task ParseAsync_MultiLevelHeadings()
    {
        var service = CreateService("# H1\n\n## H2\n\n### H3");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(3);
        ((ImportHeading)result.Elements[0]).Level.Should().Be(1);
        ((ImportHeading)result.Elements[1]).Level.Should().Be(2);
        ((ImportHeading)result.Elements[2]).Level.Should().Be(3);
    }

    [Fact]
    public async Task ParseAsync_Paragraph_MapsToImportParagraph()
    {
        var service = CreateService("This is a regular paragraph.");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var para = result.Elements[0].Should().BeOfType<ImportParagraph>().Subject;
        para.Text.Should().Be("This is a regular paragraph.");
    }

    [Fact]
    public async Task ParseAsync_DisplayEquation_SingleLine()
    {
        var service = CreateService("$$E = mc^2$$");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var eq = result.Elements[0].Should().BeOfType<ImportEquation>().Subject;
        eq.LatexContent.Should().Be("E = mc^2");
        eq.ConversionSucceeded.Should().BeTrue();
        eq.IsInline.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_DisplayEquation_MultiLine()
    {
        var service = CreateService("$$\n\\int_0^1 f(x) \\, dx\n$$");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var eq = result.Elements[0].Should().BeOfType<ImportEquation>().Subject;
        eq.LatexContent.Should().Be("\\int_0^1 f(x) \\, dx");
    }

    [Fact]
    public async Task ParseAsync_InlineMath_PreservedInParagraph()
    {
        var service = CreateService("The equation $E = mc^2$ is famous.");

        var result = await service.ParseAsync("/tmp/test.pdf");

        var para = result.Elements[0].Should().BeOfType<ImportParagraph>().Subject;
        para.Text.Should().Contain("$E = mc^2$");
    }

    [Fact]
    public async Task ParseAsync_CodeBlock_MapsToImportCodeBlock()
    {
        var service = CreateService("```python\nprint('hello')\nx = 42\n```");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var code = result.Elements[0].Should().BeOfType<ImportCodeBlock>().Subject;
        code.Text.Should().Be("print('hello')\nx = 42");
        code.Language.Should().Be("python");
    }

    [Fact]
    public async Task ParseAsync_CodeBlock_NoLanguage()
    {
        var service = CreateService("```\nsome code\n```");

        var result = await service.ParseAsync("/tmp/test.pdf");

        var code = result.Elements[0].Should().BeOfType<ImportCodeBlock>().Subject;
        code.Text.Should().Be("some code");
        code.Language.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_PipeTable_MapsToImportTable()
    {
        var service = CreateService("| A | B | C |\n|---|---|---|\n| 1 | 2 | 3 |\n| 4 | 5 | 6 |");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var table = result.Elements[0].Should().BeOfType<ImportTable>().Subject;
        table.Rows.Should().HaveCount(3);
        table.HasHeaderRow.Should().BeTrue();
        table.Rows[0][0].Text.Should().Be("A");
        table.Rows[1][0].Text.Should().Be("1");
    }

    [Fact]
    public async Task ParseAsync_Image_MapsToImportImage()
    {
        var service = CreateService("![Figure 1](https://cdn.mathpix.com/images/fig1.png)");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var img = result.Elements[0].Should().BeOfType<ImportImage>().Subject;
        img.AltText.Should().Be("Figure 1");
        img.Data.Should().NotBeEmpty();
        img.MimeType.Should().Be("image/png");
    }

    [Fact]
    public async Task ParseAsync_BulletList_MapsToImportListItems()
    {
        var service = CreateService("- First item\n- Second item\n- Third item");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(3);
        result.Elements.Should().AllBeOfType<ImportListItem>();
        var first = (ImportListItem)result.Elements[0];
        first.Text.Should().Be("First item");
        first.IsNumbered.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_NumberedList_MapsToImportListItems()
    {
        var service = CreateService("1. First\n2. Second\n3. Third");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(3);
        var first = (ImportListItem)result.Elements[0];
        first.Text.Should().Be("First");
        first.IsNumbered.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_AbstractSection()
    {
        var service = CreateService("# Abstract\n\nThis paper presents a novel approach.\n\n# Introduction\n\nRegular content.");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(4);
        result.Elements[0].Should().BeOfType<ImportHeading>();
        result.Elements[1].Should().BeOfType<ImportAbstract>();
        ((ImportAbstract)result.Elements[1]).Text.Should().Be("This paper presents a novel approach.");
        result.Elements[2].Should().BeOfType<ImportHeading>();
        result.Elements[3].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_BibliographySection()
    {
        var service = CreateService("# References\n\n[1] Smith, J. (2020). A paper.\n\n[2] Doe, J. (2021). Another paper.");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(3);
        result.Elements[0].Should().BeOfType<ImportHeading>();
        result.Elements[1].Should().BeOfType<ImportBibliographyEntry>();
        ((ImportBibliographyEntry)result.Elements[1]).ReferenceLabel.Should().Be("1");
        result.Elements[2].Should().BeOfType<ImportBibliographyEntry>();
        ((ImportBibliographyEntry)result.Elements[2]).ReferenceLabel.Should().Be("2");
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_TheoremDetection()
    {
        var service = CreateService("Theorem 1. Every continuous function on [a,b] is bounded.\n\nLemma 2.1: A supporting result.");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(2);
        var thm = result.Elements[0].Should().BeOfType<ImportTheorem>().Subject;
        thm.EnvironmentType.Should().Be(TheoremEnvironmentType.Theorem);
        thm.Number.Should().Be("1");

        var lemma = result.Elements[1].Should().BeOfType<ImportTheorem>().Subject;
        lemma.EnvironmentType.Should().Be(TheoremEnvironmentType.Lemma);
        lemma.Number.Should().Be("2.1");
    }

    [Fact]
    public async Task ParseAsync_EmptyDocument_ReturnsDefaultTitle()
    {
        var service = CreateService("");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().BeEmpty();
        result.Title.Should().Be("Imported PDF Document");
    }

    [Fact]
    public async Task ParseAsync_ExtractsTitle_FromFirstHeading()
    {
        var service = CreateService("# My Great Paper\n\nSome body text.");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Title.Should().Be("My Great Paper");
    }

    [Fact]
    public async Task ParseAsync_ElementsHaveIncrementingOrder()
    {
        var service = CreateService("# Title\n\nParagraph\n\n$$x^2$$");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Select(e => e.Order).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ParseAsync_MixedContent_CorrectTypes()
    {
        var markdown = """
            # Introduction

            This is a paragraph with $inline math$.

            $$\sum_{i=1}^{n} x_i$$

            ```python
            def hello():
                pass
            ```

            - Item 1
            - Item 2

            | Col A | Col B |
            |-------|-------|
            | 1     | 2     |
            """;

        var service = CreateService(markdown);
        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().Contain(e => e is ImportHeading);
        result.Elements.Should().Contain(e => e is ImportParagraph);
        result.Elements.Should().Contain(e => e is ImportEquation);
        result.Elements.Should().Contain(e => e is ImportCodeBlock);
        result.Elements.Should().Contain(e => e is ImportListItem);
        result.Elements.Should().Contain(e => e is ImportTable);
    }

    [Fact]
    public async Task ParseAsync_SubmitsAndPolls()
    {
        var mockClient = new Mock<IMathpixClient>();
        mockClient.Setup(c => c.SubmitPdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("pdf-123");
        mockClient.Setup(c => c.WaitForCompletionAsync("pdf-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("# Test");

        var service = new MathpixPdfImportService(mockClient.Object, DefaultOptions, NullLogger<MathpixPdfImportService>.Instance);
        var result = await service.ParseAsync("/tmp/test.pdf");

        mockClient.Verify(c => c.SubmitPdfAsync("/tmp/test.pdf", It.IsAny<CancellationToken>()), Times.Once);
        mockClient.Verify(c => c.WaitForCompletionAsync("pdf-123", It.IsAny<CancellationToken>()), Times.Once);
        result.Elements.Should().ContainSingle().Which.Should().BeOfType<ImportHeading>();
    }

    [Fact]
    public async Task ParseAsync_LatexTable_FallsBackToText()
    {
        var service = CreateService("\\begin{tabular}{|c|c|}\n\\hline\nA & B \\\\\n\\hline\n\\end{tabular}");

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var table = result.Elements[0].Should().BeOfType<ImportTable>().Subject;
        table.Rows.Should().HaveCount(1);
        table.Rows[0][0].Text.Should().Contain("\\begin{tabular}");
    }

    [Fact]
    public async Task ParseAsync_ImageFetchFailure_StillCreatesElement()
    {
        var mockClient = new Mock<IMathpixClient>();
        mockClient.Setup(c => c.SubmitPdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-id");
        mockClient.Setup(c => c.WaitForCompletionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("![alt](https://example.com/img.png)");
        mockClient.Setup(c => c.GetImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var service = new MathpixPdfImportService(mockClient.Object, DefaultOptions, NullLogger<MathpixPdfImportService>.Instance);
        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var img = result.Elements[0].Should().BeOfType<ImportImage>().Subject;
        img.AltText.Should().Be("alt");
        img.Data.Should().BeEmpty(); // Failed to fetch but element still created
    }
}
