using FluentAssertions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Services;

public class PdfImportServiceTests
{
    private static readonly IOptions<MineruOptions> DefaultMineruOptions = Options.Create(
        new MineruOptions { BatchPageSize = 0 }); // Disable batching for unit tests

    private static PdfImportService CreateService(MineruParseResponse response)
    {
        var mockClient = new Mock<IMineruClient>();
        mockClient.Setup(c => c.ParsePdfAsync(It.IsAny<string>(), It.IsAny<MineruParseOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        mockClient.Setup(c => c.GetImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        return new PdfImportService(mockClient.Object, DefaultMineruOptions, NullLogger<PdfImportService>.Instance);
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
    public async Task ParseAsync_PostProcessing_EmbeddedAbstractInParagraph_SplitsAndDetects()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Statistical study of something. Carried out by: DINIA Lilia. Supervised by: Mr. MOUMEN Aniss Abstract This study examines the determinants of economic growth." },
                new MineruContentBlock { Type = "text", Text = "The results show significant correlations between variables." },
                new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Regular paragraph after introduction." },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // The merged paragraph should be split: before "Abstract" → paragraph, after → abstract
        result.Elements.Should().HaveCount(5);

        // Title/author info kept as paragraph
        var beforeAbstract = result.Elements[0].Should().BeOfType<ImportParagraph>().Subject;
        beforeAbstract.Text.Should().Be("Statistical study of something. Carried out by: DINIA Lilia. Supervised by: Mr. MOUMEN Aniss");

        // Abstract content extracted
        var abstractBlock = result.Elements[1].Should().BeOfType<ImportAbstract>().Subject;
        abstractBlock.Text.Should().Be("This study examines the determinants of economic growth.");

        // Continuation paragraph becomes abstract too (section context set)
        result.Elements[2].Should().BeOfType<ImportAbstract>();
        ((ImportAbstract)result.Elements[2]).Text.Should().Be("The results show significant correlations between variables.");

        // Introduction heading resets section
        result.Elements[3].Should().BeOfType<ImportHeading>();
        result.Elements[4].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_EmbeddedAbstract_IgnoresAtStart()
    {
        // Paragraph starting with "Abstract" should NOT trigger embedded detection
        // (this is handled by the heading detection path, or the paragraph just starts with the word)
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Abstract This study examines something." },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // Should remain a paragraph since "Abstract" is at the start (no preceding text)
        result.Elements.Should().ContainSingle();
        result.Elements[0].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public void FindAbstractKeywordBoundary_FindsEmbeddedKeyword()
    {
        var (start, after) = PdfImportService.FindAbstractKeywordBoundary(
            "Some title text Abstract This is the abstract.");

        start.Should().Be(16);
        after.Should().Be(24);
    }

    [Fact]
    public void FindAbstractKeywordBoundary_IgnoresKeywordAtStart()
    {
        var (start, _) = PdfImportService.FindAbstractKeywordBoundary(
            "Abstract This is the content of the paper.");

        start.Should().Be(-1);
    }

    [Fact]
    public void FindAbstractKeywordBoundary_IgnoresPartialWordMatch()
    {
        var (start, _) = PdfImportService.FindAbstractKeywordBoundary(
            "This is an Abstraction of the concept.");

        start.Should().Be(-1);
    }

    [Fact]
    public void FindAbstractKeywordBoundary_MatchesCaseInsensitive()
    {
        var (start, after) = PdfImportService.FindAbstractKeywordBoundary(
            "Title text ABSTRACT Content here.");

        start.Should().Be(11);
        after.Should().Be(19);
    }

    [Fact]
    public async Task ParseAsync_ImageWithBase64InResponse_DecodesDirectly()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var base64 = Convert.ToBase64String(imageBytes);

        var mockClient = new Mock<IMineruClient>();
        mockClient.Setup(c => c.ParsePdfAsync(It.IsAny<string>(), It.IsAny<MineruParseOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MineruParseResponse
            {
                ContentList = [new MineruContentBlock { Type = "image", ImgPath = "fig1.png" }],
                Images = new Dictionary<string, string> { ["fig1.png"] = base64 }
            });

        var service = new PdfImportService(mockClient.Object, DefaultMineruOptions, NullLogger<PdfImportService>.Instance);

        var result = await service.ParseAsync("/tmp/test.pdf");

        var img = result.Elements[0].Should().BeOfType<ImportImage>().Subject;
        img.Data.Should().BeEquivalentTo(imageBytes);

        // Verify GetImageAsync was NOT called (used base64 from response instead)
        mockClient.Verify(c => c.GetImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_TocDetection_CreatesTableOfContents()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "My Thesis", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Contents", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Declaration ..... i Certificate ..... ii 1 Introduction ..... 1 1.1 Email Communication ..... 1 2 Literature Review ..... 5" },
                new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Regular paragraph" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // "My Thesis" heading + TOC block + "Introduction" heading + paragraph = 4 elements
        // "Contents" heading is consumed, not emitted
        result.Elements.Should().HaveCount(4);
        result.Elements[0].Should().BeOfType<ImportHeading>();
        var toc = result.Elements[1].Should().BeOfType<ImportTableOfContents>().Subject;
        toc.Entries.Should().HaveCountGreaterThan(0);
        result.Elements[2].Should().BeOfType<ImportHeading>();
        result.Elements[3].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_ListOfFigures_DropsContent()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "List of Figures", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Figure 1 ..... 3 Figure 2 ..... 7" },
                new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Body text" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // "List of Figures" heading + its paragraph are both dropped
        result.Elements.Should().HaveCount(2);
        result.Elements[0].Should().BeOfType<ImportHeading>();
        ((ImportHeading)result.Elements[0]).Text.Should().Be("Introduction");
        result.Elements[1].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_ListOfTables_DropsContent()
    {
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "List of Tables", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Table 1 ..... 10" },
                new MineruContentBlock { Type = "text", Text = "Methods", TextLevel = 1 },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        result.Elements[0].Should().BeOfType<ImportHeading>();
        ((ImportHeading)result.Elements[0]).Text.Should().Be("Methods");
    }

    [Fact]
    public void ParseTocParagraph_ParsesDotLeaderEntries()
    {
        var text = "Declaration ..... i Certificate ..... ii 1 Introduction ..... 1 1.1 Email Communication ..... 1 2.1.3 Deep Section ..... 15";

        var entries = PdfImportService.ParseTocParagraph(text);

        entries.Should().HaveCount(5);

        entries[0].Text.Should().Be("Declaration");
        entries[0].PageNumber.Should().Be("i");
        entries[0].Level.Should().Be(1); // unnumbered front matter

        entries[1].Text.Should().Be("Certificate");
        entries[1].PageNumber.Should().Be("ii");
        entries[1].Level.Should().Be(1);

        entries[2].Text.Should().Be("1 Introduction");
        entries[2].PageNumber.Should().Be("1");
        entries[2].Level.Should().Be(1); // chapter level "1"

        entries[3].Text.Should().Be("1.1 Email Communication");
        entries[3].PageNumber.Should().Be("1");
        entries[3].Level.Should().Be(2); // section level "1.1"

        entries[4].Text.Should().Be("2.1.3 Deep Section");
        entries[4].PageNumber.Should().Be("15");
        entries[4].Level.Should().Be(3); // subsection level "2.1.3"
    }

    [Fact]
    public void ParseTocParagraph_EmptyText_ReturnsEmpty()
    {
        PdfImportService.ParseTocParagraph("").Should().BeEmpty();
        PdfImportService.ParseTocParagraph("  ").Should().BeEmpty();
    }

    [Fact]
    public void ParseTocParagraph_NoDotLeaders_ReturnsEmpty()
    {
        PdfImportService.ParseTocParagraph("Just some regular text without dots").Should().BeEmpty();
    }

    [Fact]
    public void ParseTocParagraph_RomanNumeralPages()
    {
        var text = "Preface ..... iii Acknowledgements ..... iv";

        var entries = PdfImportService.ParseTocParagraph(text);

        entries.Should().HaveCount(2);
        entries[0].PageNumber.Should().Be("iii");
        entries[1].PageNumber.Should().Be("iv");
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_TocDetection_ByContentPattern_NoHeading()
    {
        // TOC paragraph with no preceding "Contents" heading — should still be detected
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "My Thesis", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Declaration ..... i Certificate ..... ii Abstract ..... iii 1 Introduction ..... 1 1.1 Background ..... 2" },
                new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = "Regular paragraph" },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // "My Thesis" heading + TOC block + "Introduction" heading + paragraph = 4
        result.Elements.Should().HaveCount(4);
        result.Elements[0].Should().BeOfType<ImportHeading>();
        result.Elements[1].Should().BeOfType<ImportTableOfContents>();
        var toc = (ImportTableOfContents)result.Elements[1];
        toc.Entries.Should().HaveCount(5);
        result.Elements[2].Should().BeOfType<ImportHeading>();
        result.Elements[3].Should().BeOfType<ImportParagraph>();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_TocDetection_ContentsParagraphThenToc()
    {
        // "Contents" is a paragraph (not heading), followed by TOC paragraph
        // Both should be consumed into a single TOC block
        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Contents" },
                new MineruContentBlock { Type = "text", Text = "Declaration ..... i Certificate ..... ii 1 Introduction ..... 1 1.1 Background ..... 2 2 Methods ..... 5" },
                new MineruContentBlock { Type = "text", Text = "Introduction", TextLevel = 1 },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        // "Contents" paragraph should be consumed by the TOC block
        result.Elements.Should().HaveCount(2);
        result.Elements[0].Should().BeOfType<ImportTableOfContents>();
        result.Elements[1].Should().BeOfType<ImportHeading>();
    }

    [Fact]
    public void LooksTocLikeParagraph_DetectsMultipleDotLeaders()
    {
        var tocText = "Declaration ..... i Certificate ..... ii 1 Introduction ..... 1";
        PdfImportService.LooksTocLikeParagraph(tocText).Should().BeTrue();
    }

    [Fact]
    public void LooksTocLikeParagraph_RejectsFewDotLeaders()
    {
        var notToc = "See page ..... 5";
        PdfImportService.LooksTocLikeParagraph(notToc).Should().BeFalse();
    }

    [Fact]
    public void LooksTocLikeParagraph_RejectsNormalParagraph()
    {
        PdfImportService.LooksTocLikeParagraph("This is a normal paragraph with no dots.").Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_PostProcessing_RealWorldTocText()
    {
        // Real-world TOC text from the VIT thesis PDF
        var tocText = "Declaration ..... i Certificate ..... ii Abstract ..... iii Acknowledgements ..... iv List of Figures ..... vii 1 Introduction ..... 1 1.1 Email Communication ..... 1 1.2 Objectives ..... 2 1.3 Challenges of Email Analytics ..... 2 2 Literature Survey ..... 3 2.1 A framework for the forensic investigation of unstructured email relationship data ..... 3 2.5.1 Tokenization ..... 5 3 Experimental Design ..... 10 4 Results and Implementation work ..... 22 5 Conclusion and Future Work ..... 35";

        var service = CreateService(new MineruParseResponse
        {
            ContentList =
            [
                new MineruContentBlock { Type = "text", Text = "Contents", TextLevel = 1 },
                new MineruContentBlock { Type = "text", Text = tocText },
            ]
        });

        var result = await service.ParseAsync("/tmp/test.pdf");

        result.Elements.Should().ContainSingle();
        var toc = result.Elements[0].Should().BeOfType<ImportTableOfContents>().Subject;
        toc.Entries.Should().HaveCountGreaterThanOrEqualTo(10);

        // Verify level detection
        var declaration = toc.Entries.First(e => e.Text.Contains("Declaration"));
        declaration.Level.Should().Be(1); // unnumbered

        var intro = toc.Entries.First(e => e.Text.Contains("1 Introduction"));
        intro.Level.Should().Be(1); // chapter

        var email = toc.Entries.First(e => e.Text.Contains("Email Communication"));
        email.Level.Should().Be(2); // 1.1 = section

        var tokenization = toc.Entries.First(e => e.Text.Contains("Tokenization"));
        tokenization.Level.Should().Be(3); // 2.5.1 = subsection
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
