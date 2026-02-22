using FluentAssertions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Services;

public class PdfImportServiceTests
{
    private static PdfImportService CreateService(MineruParseResponse response)
    {
        var mockClient = new Mock<IMineruClient>();
        mockClient.Setup(c => c.ParsePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        mockClient.Setup(c => c.GetImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        return new PdfImportService(mockClient.Object, NullLogger<PdfImportService>.Instance);
    }

    [Fact]
    public void CanParse_PdfFile_ReturnsTrue()
    {
        var service = CreateService(new MineruParseResponse());
        service.CanParse("document.pdf").Should().BeTrue();
        service.CanParse("DOCUMENT.PDF").Should().BeTrue();
    }

    [Fact]
    public void CanParse_NonPdfFile_ReturnsFalse()
    {
        var service = CreateService(new MineruParseResponse());
        service.CanParse("document.docx").Should().BeFalse();
        service.CanParse("document.tex").Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_HeadingBlock_MapsToImportHeading()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var heading = result.Elements[0].Should().BeOfType<ImportHeading>().Subject;
        heading.Text.Should().Be("Introduction");
        heading.Level.Should().Be(1);
    }

    [Fact]
    public async Task ParseAsync_TextBlockWithoutLevel_MapsToImportParagraph()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "text", Text = "Some paragraph text." }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var para = result.Elements[0].Should().BeOfType<ImportParagraph>().Subject;
        para.Text.Should().Be("Some paragraph text.");
    }

    [Fact]
    public async Task ParseAsync_TextBlockLevel0_MapsToImportParagraph()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "text", Text = "Body text", TextLevel = 0 }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements[0].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_EquationBlock_MapsToImportEquation()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "equation", Text = "E = mc^2" }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        var eq = result.Elements[0].Should().BeOfType<ImportEquation>().Subject;
        eq.LatexContent.Should().Be("E = mc^2");
        eq.ConversionSucceeded.Should().BeTrue();
        eq.IsInline.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_TableBlock_MapsToImportTable()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock
            {
                Type = "table",
                TableBody = "<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>"
            }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        var table = result.Elements[0].Should().BeOfType<ImportTable>().Subject;
        table.Rows.Should().HaveCount(2);
        table.HasHeaderRow.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_ImageBlock_MapsToImportImage()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock
            {
                Type = "image",
                ImgPath = "figures/fig1.png",
                ImageCaption = "Figure 1: A diagram"
            }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        var img = result.Elements[0].Should().BeOfType<ImportImage>().Subject;
        img.Data.Should().NotBeEmpty();
        img.AltText.Should().Be("Figure 1: A diagram");
        img.MimeType.Should().Be("image/png");
    }

    [Fact]
    public async Task ParseAsync_CodeBlock_MapsToImportCodeBlock()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "code", Text = "print('hello')" }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        var code = result.Elements[0].Should().BeOfType<ImportCodeBlock>().Subject;
        code.Text.Should().Be("print('hello')");
    }

    [Fact]
    public async Task ParseAsync_ListBlock_MapsToImportListItems()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "list", Text = "- Item one\n- Item two\n- Item three" }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(3);
        result.Elements.Should().AllBeOfType<ImportListItem>();
        var first = (ImportListItem)result.Elements[0];
        first.Text.Should().Be("Item one");
        first.IsNumbered.Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_NumberedList_DetectsOrdering()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "list", Text = "1. First\n2. Second" }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(2);
        var first = (ImportListItem)result.Elements[0];
        first.IsNumbered.Should().BeTrue();
        first.Text.Should().Be("First");
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_AbstractSection()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Abstract", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "This paper presents..." },
                new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Regular paragraph" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(4);
        result.Elements[0].Should().BeOfType<ImportHeading>();
        result.Elements[1].Should().BeOfType<ImportAbstract>();
        ((ImportAbstract)result.Elements[1]).Text.Should().Be("This paper presents...");
        result.Elements[2].Should().BeOfType<ImportHeading>();
        result.Elements[3].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_BibliographySection()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "References", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "[1] Smith, J. (2020). A paper. Journal, 1-10." },
                new MineruContentBlock { Type = "text", Text = "[2] Doe, J. (2021). Another paper." },
            ]
        });

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
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Theorem 1. Every continuous function on [a,b] is bounded." },
                new MineruContentBlock { Type = "text", Text = "Lemma 2.1: A supporting result." },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().HaveCount(2);
        var thm = result.Elements[0].Should().BeOfType<ImportTheorem>().Subject;
        thm.EnvironmentType.Should().Be(TheoremEnvironmentType.Theorem);
        thm.Number.Should().Be("1");
        thm.Text.Should().Be("Every continuous function on [a,b] is bounded.");

        var lemma = result.Elements[1].Should().BeOfType<ImportTheorem>().Subject;
        lemma.EnvironmentType.Should().Be(TheoremEnvironmentType.Lemma);
        lemma.Number.Should().Be("2.1");
    }

    [Fact]
    public async Task ParseAsync_EmptyDocument_ReturnsDefaultTitle()
    {
        var service = CreateService(new MineruParseResponse { ContentList = [] });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().BeEmpty();
        result.Title.Should().Be("Imported PDF Document");
    }

    [Fact]
    public async Task ParseAsync_ExtractsTitle_FromFirstHeading()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "My Great Paper", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Some body text." },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Title.Should().Be("My Great Paper");
    }

    [Fact]
    public async Task ParseAsync_EmptyBlocks_AreSkipped()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "  " },
                new MineruContentBlock { Type = "text", Text = "Valid paragraph" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // Empty/whitespace-only paragraphs are still included (whitespace-trimmed)
        result.Elements.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsync_UnknownBlockType_IsSkipped()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "unknown_type", Text = "Something" },
                new MineruContentBlock { Type = "text", Text = "Valid" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        result.Elements[0].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_ImageWithBase64InResponse_DecodesDirectly()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var base64 = Convert.ToBase64String(imageBytes);

        var mockClient = new Mock<IMineruClient>();
        mockClient.Setup(c => c.ParsePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MineruParseResponse
            {
                ContentList = [new MineruContentBlock { Type = "image", ImgPath = "fig1.png" }],
                Images = new Dictionary<string, string> { ["fig1.png"] = base64 }
            });

        var service = new PdfImportService(mockClient.Object, NullLogger<PdfImportService>.Instance);

        var result = await service.ParseAsync("/tmp/test.pdf");

        var img = result.Elements[0].Should().BeOfType<ImportImage>().Subject;
        img.Data.Should().BeEquivalentTo(imageBytes);

        // Verify GetImageAsync was NOT called (used base64 from response instead)
        mockClient.Verify(c => c.GetImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_ElementsHaveIncrementingOrder()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Title", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Paragraph" },
                new MineruContentBlock { Type = "equation", Text = "x^2" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Select(e => e.Order).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ParseAsync_TableWithoutHtml_FallsBackToText()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList = [new MineruContentBlock { Type = "table", Text = "Some table data" }]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        var table = result.Elements[0].Should().BeOfType<ImportTable>().Subject;
        table.Rows.Should().HaveCount(1);
        table.Rows[0][0].Text.Should().Be("Some table data");
    }
}
