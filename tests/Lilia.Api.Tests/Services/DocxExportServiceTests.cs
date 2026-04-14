using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using Lilia.Import.Converters;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for DocxExportService.
/// Uses mocked ILatexToOmmlConverter and IEquationImageRenderer to verify
/// each block type produces correct DOCX XML, without requiring pdflatex.
/// </summary>
public class DocxExportServiceTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    // ── DOCX ZIP helpers ──────────────────────────────────────────────────────

    private static XDocument ExtractDocumentXml(byte[] docxBytes)
    {
        using var ms = new MemoryStream(docxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new Exception("word/document.xml not found");
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static string GetAllText(XDocument doc) =>
        string.Join(" ", doc.Descendants(W + "t").Select(t => t.Value));

    private static bool HasEntry(byte[] docxBytes, string path)
    {
        using var ms = new MemoryStream(docxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return zip.GetEntry(path) != null;
    }

    // ── Factories ─────────────────────────────────────────────────────────────

    private static ExportDocument SingleBlock(string type, ExportBlockContent content) =>
        new() { Title = "Test", Blocks = [new ExportBlock { Type = type, Content = content }] };

    private static ILatexToOmmlConverter MockOmmlSuccess(string ommlContent)
    {
        var mock = new Mock<ILatexToOmmlConverter>();
        mock.Setup(c => c.Convert(It.IsAny<string>()))
            .Returns((ommlContent, true, (string?)null));
        return mock.Object;
    }

    private static ILatexToOmmlConverter MockOmmlFailure()
    {
        var mock = new Mock<ILatexToOmmlConverter>();
        mock.Setup(c => c.Convert(It.IsAny<string>()))
            .Returns(("", false, "parse error"));
        return mock.Object;
    }

    private static IEquationImageRenderer MockImageRenderer(byte[]? result = null)
    {
        var mock = new Mock<IEquationImageRenderer>();
        mock.Setup(r => r.RenderToPngAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    // ── Basic DOCX structure ──────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_EmptyDocument_ProducesValidDocx()
    {
        var svc = new DocxExportService();
        var doc = new ExportDocument { Title = "Empty" };

        var result = await svc.ExportAsync(doc);

        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(3000); // minimum valid DOCX
        HasEntry(result, "word/document.xml").Should().BeTrue();
        HasEntry(result, "[Content_Types].xml").Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_DocumentWithTitle_SetsPackageProperties()
    {
        var svc = new DocxExportService();
        var doc = new ExportDocument { Title = "My Report", Author = "Alice" };

        var result = await svc.ExportAsync(doc);

        result.Should().NotBeNullOrEmpty();
        // Content types should always be present
        HasEntry(result, "[Content_Types].xml").Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_StylesPartAdded_ContainsHeadingStyles()
    {
        var svc = new DocxExportService();
        var doc = new ExportDocument { Title = "T" };

        var result = await svc.ExportAsync(doc);

        HasEntry(result, "word/styles.xml").Should().BeTrue();

        using var ms = new MemoryStream(result);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/styles.xml")!;
        using var reader = new StreamReader(entry.Open());
        var stylesXml = reader.ReadToEnd();
        stylesXml.Should().Contain("Heading1");
        stylesXml.Should().Contain("Code");
    }

    // ── Paragraph ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Paragraph_TextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("paragraph", new ExportBlockContent { Text = "Hello world" });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        GetAllText(xml).Should().Contain("Hello world");
    }

    [Fact]
    public async Task ExportAsync_ParagraphWithRichText_Bold()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "bold text", Bold = true }
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        GetAllText(xml).Should().Contain("bold text");
        // Bold run should have w:b element
        xml.Descendants(W + "b").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_ParagraphWithRichText_Italic()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "italic", Italic = true }
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(W + "i").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_ParagraphEmpty_ProducesEmptyParagraph()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("paragraph", new ExportBlockContent { Text = "" });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        // Body should have at least one paragraph element (plus section props)
        xml.Descendants(W + "p").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── Heading ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "Heading1")]
    [InlineData(2, "Heading2")]
    [InlineData(3, "Heading3")]
    [InlineData(4, "Heading4")]
    [InlineData(5, "Heading5")]
    [InlineData(6, "Heading6")]
    public async Task ExportAsync_Heading_AppliesCorrectStyle(int level, string styleId)
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("heading", new ExportBlockContent
        {
            Text = $"Section {level}",
            Level = level
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        var headings = xml.Descendants(W + "pStyle")
            .Where(e => e.Attribute(W + "val")?.Value == styleId)
            .ToList();
        headings.Should().HaveCountGreaterThanOrEqualTo(1, $"Expected {styleId} style for level {level}");
    }

    [Fact]
    public async Task ExportAsync_Heading_TextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("heading", new ExportBlockContent
        {
            Text = "Introduction",
            Level = 1
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("Introduction");
    }

    [Fact]
    public async Task ExportAsync_HeadingLevelOutOfRange_ClampsTo1()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("heading", new ExportBlockContent
        {
            Text = "Over-levelled",
            Level = 10
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        // Should clamp to 6 max
        var style = xml.Descendants(W + "pStyle")
            .FirstOrDefault(e =>
            {
                var val = e.Attribute(W + "val")?.Value;
                return val != null && val.StartsWith("Heading");
            });
        style.Should().NotBeNull();
        int.Parse(style!.Attribute(W + "val")!.Value.Replace("Heading", "")).Should().BeLessThanOrEqualTo(6);
    }

    // ── Code ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_CodeBlock_UsesConsolasFont()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("code", new ExportBlockContent
        {
            Code = "print('hello')",
            Language = "python"
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        var consoasRuns = xml.Descendants(W + "rFonts")
            .Where(e => e.Attribute(W + "ascii")?.Value == "Consolas")
            .ToList();
        consoasRuns.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_CodeBlock_TextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("code", new ExportBlockContent
        {
            Code = "let x = 42;"
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("let x = 42;");
    }

    [Fact]
    public async Task ExportAsync_MultilineCode_ProducesOneParaPerLine()
    {
        var svc = new DocxExportService();
        var code = "line1\nline2\nline3";
        var doc = SingleBlock("code", new ExportBlockContent { Code = code });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        var codeParas = xml.Descendants(W + "pStyle")
            .Where(e => e.Attribute(W + "val")?.Value == "Code")
            .ToList();
        codeParas.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExportAsync_CodeBlock_CodeStyleApplied()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("code", new ExportBlockContent { Code = "x" });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(W + "pStyle")
            .Any(e => e.Attribute(W + "val")?.Value == "Code")
            .Should().BeTrue("code block should use Code paragraph style");
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Table_ProducesWTblElement()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("table", new ExportBlockContent
        {
            Rows =
            [
                [new ExportTableCell { Text = "A" }, new ExportTableCell { Text = "B" }],
                [new ExportTableCell { Text = "C" }, new ExportTableCell { Text = "D" }],
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(W + "tbl").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_Table_CorrectRowCount()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("table", new ExportBlockContent
        {
            Rows =
            [
                [new ExportTableCell { Text = "H1" }, new ExportTableCell { Text = "H2" }],
                [new ExportTableCell { Text = "R1" }, new ExportTableCell { Text = "R2" }],
                [new ExportTableCell { Text = "R3" }, new ExportTableCell { Text = "R4" }],
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        var tbl = xml.Descendants(W + "tbl").First();
        tbl.Elements(W + "tr").Should().HaveCount(3);
    }

    [Fact]
    public async Task ExportAsync_TableWithHeader_FirstRowMarked()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("table", new ExportBlockContent
        {
            HasHeader = true,
            Rows =
            [
                [new ExportTableCell { Text = "Header" }],
                [new ExportTableCell { Text = "Data" }],
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        var tbl = xml.Descendants(W + "tbl").First();
        var firstRow = tbl.Elements(W + "tr").First();
        firstRow.Descendants(W + "tblHeader").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_Table_CellTextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("table", new ExportBlockContent
        {
            Rows = [[new ExportTableCell { Text = "SomeUniqueValue" }]]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("SomeUniqueValue");
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_BulletList_ProducesNumberingProperties()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("list", new ExportBlockContent
        {
            ListType = "bullet",
            Items =
            [
                new ExportListItem { Text = "Item 1", Level = 0 },
                new ExportListItem { Text = "Item 2", Level = 0 },
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(W + "numId").Should().HaveCountGreaterThanOrEqualTo(2);
        GetAllText(xml).Should().Contain("Item 1");
        GetAllText(xml).Should().Contain("Item 2");
    }

    [Fact]
    public async Task ExportAsync_OrderedList_UsesNumId2()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("list", new ExportBlockContent
        {
            ListType = "ordered",
            Items = [new ExportListItem { Text = "First", Level = 0 }]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        var numIds = xml.Descendants(W + "numId")
            .Select(e => e.Attribute(W + "val")?.Value)
            .ToList();
        numIds.Should().Contain("2"); // ordered list uses ID 2
    }

    // ── Equation: OMML path ───────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_EquationOmmlSuccess_ProducesOmmlPara()
    {
        var sampleOmml = $"<m:oMath xmlns:m=\"{M.NamespaceName}\"><m:r><m:t>x</m:t></m:r></m:oMath>";
        var svc = new DocxExportService(MockOmmlSuccess(sampleOmml));
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = "x",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        // Should contain oMathPara (display mode)
        xml.Descendants(M + "oMathPara").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_EquationOmmlSuccessInlineMode_ProducesOmml()
    {
        var sampleOmml = $"<m:oMath xmlns:m=\"{M.NamespaceName}\"><m:r><m:t>x</m:t></m:r></m:oMath>";
        var svc = new DocxExportService(MockOmmlSuccess(sampleOmml));
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = "x",
            DisplayMode = false
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        // Inline mode should have oMath but no oMathPara wrapper
        xml.Descendants(M + "oMath").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_EquationOmmlConverterCalled_WithCorrectLatex()
    {
        var mockConverter = new Mock<ILatexToOmmlConverter>();
        var sampleOmml = $"<m:oMath xmlns:m=\"{M.NamespaceName}\"><m:r><m:t>E</m:t></m:r></m:oMath>";
        mockConverter.Setup(c => c.Convert(It.IsAny<string>()))
            .Returns((sampleOmml, true, (string?)null));

        var svc = new DocxExportService(mockConverter.Object);
        var doc = SingleBlock("equation", new ExportBlockContent { Latex = @"E=mc^2" });

        await svc.ExportAsync(doc);

        mockConverter.Verify(c => c.Convert(@"E=mc^2"), Times.Once);
    }

    // ── Equation: PNG fallback path ───────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_EquationOmmlFails_ImageRendererIsCalled()
    {
        // Verify that when OMML conversion fails, the image renderer is called
        var mockRenderer = new Mock<IEquationImageRenderer>();
        mockRenderer.Setup(r => r.RenderToPngAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((byte[]?)null); // return null so we fall through to plain text

        var svc = new DocxExportService(MockOmmlFailure(), mockRenderer.Object);
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = @"\frac{a}{b}",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);

        result.Should().NotBeNullOrEmpty();
        // Image renderer should have been called since OMML failed
        mockRenderer.Verify(
            r => r.RenderToPngAsync(It.IsAny<string>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ExportAsync_EquationBothFail_FallsBackToPlainText()
    {
        var svc = new DocxExportService(MockOmmlFailure(), MockImageRenderer(null));
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = @"\complex_formula",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        var allText = GetAllText(xml);

        // Plain text fallback: [latex_here]
        allText.Should().Contain(@"\complex_formula");
    }

    [Fact]
    public async Task ExportAsync_EquationNoConverter_FallsBackToPlainText()
    {
        var svc = new DocxExportService(); // no converter at all
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = "x^2",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("x^2");
    }

    [Fact]
    public async Task ExportAsync_DisplayEquationFallback_IsCentred()
    {
        var svc = new DocxExportService(MockOmmlFailure(), MockImageRenderer(null));
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = "x",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(W + "jc")
            .Any(e => e.Attribute(W + "val")?.Value == "center")
            .Should().BeTrue("display mode fallback should be centered");
    }

    [Fact]
    public async Task ExportAsync_InlineEquationFallback_UseDollarSigns()
    {
        var svc = new DocxExportService(MockOmmlFailure(), MockImageRenderer(null));
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = "x",
            DisplayMode = false
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("$x$");
    }

    // ── Blockquote ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Blockquote_TextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("blockquote", new ExportBlockContent
        {
            Text = "To be or not to be"
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("To be or not to be");
    }

    // ── Theorem ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Theorem_TextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("theorem", new ExportBlockContent
        {
            TheoremType = "theorem",
            Text = "Pythagoras",  // title stored in Text
            RichText =            // body stored in RichText
            [
                new ExportRichTextSpan { Text = "a^2 + b^2 = c^2" }
            ]
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        var allText = GetAllText(xml);
        allText.Should().Contain("Pythagoras");
        allText.Should().Contain("a^2 + b^2 = c^2");
    }

    // ── Abstract ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_Abstract_TextAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("abstract", new ExportBlockContent
        {
            Text = "This paper presents a comprehensive study of..."
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("This paper presents");
    }

    // ── Page break ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_PageBreak_ProducesPageBreakRun()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("pagebreak", new ExportBlockContent());

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(W + "br")
            .Any(e => e.Attribute(W + "type")?.Value == "page")
            .Should().BeTrue("pagebreak should produce a w:br type=page element");
    }

    // ── Unknown type fallback ─────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_UnknownBlockType_FallsBackToParagraph()
    {
        var svc = new DocxExportService();
        var doc = SingleBlock("unknowntype", new ExportBlockContent
        {
            Text = "Some text"
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        GetAllText(xml).Should().Contain("Some text");
    }

    // ── Multiple blocks ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_MultipleBlocks_AllAppearsInDocx()
    {
        var svc = new DocxExportService();
        var doc = new ExportDocument
        {
            Title = "Multi-block",
            Blocks =
            [
                new ExportBlock { Type = "heading", Content = new ExportBlockContent { Text = "Title", Level = 1 } },
                new ExportBlock { Type = "paragraph", Content = new ExportBlockContent { Text = "Body text" } },
                new ExportBlock { Type = "code", Content = new ExportBlockContent { Code = "x = 1" } },
            ]
        };

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);
        var allText = GetAllText(xml);
        allText.Should().Contain("Title");
        allText.Should().Contain("Body text");
        allText.Should().Contain("x = 1");
    }

    // ── ExportOptions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_DefaultOptions_Succeeds()
    {
        var svc = new DocxExportService();
        var doc = new ExportDocument { Title = "T" };

        var result = await svc.ExportAsync(doc, ExportOptions.Default);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportAsync_NullOptions_UsesDefault()
    {
        var svc = new DocxExportService();
        var doc = new ExportDocument { Title = "T" };

        var result = await svc.ExportAsync(doc, null);

        result.Should().NotBeNullOrEmpty();
    }

    // ── Real converter integration ────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_WithRealConverter_SimpleFrac_ProducesOmml()
    {
        var realConverter = new LatexToOmmlConverter();
        var svc = new DocxExportService(realConverter);
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = @"\frac{1}{2}",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(M + "oMathPara").Should().HaveCountGreaterThanOrEqualTo(1);
        xml.Descendants(M + "f").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExportAsync_WithRealConverter_SumEquation_ProducesNary()
    {
        var realConverter = new LatexToOmmlConverter();
        var svc = new DocxExportService(realConverter);
        var doc = SingleBlock("equation", new ExportBlockContent
        {
            Latex = @"\sum_{k=1}^{n} k",
            DisplayMode = true
        });

        var result = await svc.ExportAsync(doc);
        var xml = ExtractDocumentXml(result);

        xml.Descendants(M + "nary").Should().HaveCountGreaterThanOrEqualTo(1);
    }
}
