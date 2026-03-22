using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Core.Models.Epub;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

public class EpubServiceTests
{
    private readonly EpubService _service;

    public EpubServiceTests()
    {
        var logger = new Mock<ILogger<EpubService>>();
        _service = new EpubService(logger.Object);
    }

    // ────────────────────────── XHTML Parsing ──────────────────────────

    [Fact]
    public void ExtractBlocks_ParsesParagraph()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Hello world</p></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;
        var warnings = new List<string>();

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, warnings);

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Paragraph);
        EpubService.GetJsonProperty(blocks[0].Content, "text").Should().Be("Hello world");
    }

    [Theory]
    [InlineData("h1", 1)]
    [InlineData("h2", 2)]
    [InlineData("h3", 3)]
    [InlineData("h4", 4)]
    [InlineData("h5", 5)]
    [InlineData("h6", 6)]
    public void ExtractBlocks_ParsesHeadingsAtAllLevels(string tag, int expectedLevel)
    {
        var body = XElement.Parse($"<body xmlns=\"http://www.w3.org/1999/xhtml\"><{tag}>Title</{tag}></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Heading);
        EpubService.GetJsonPropertyInt(blocks[0].Content, "level").Should().Be(expectedLevel);
        EpubService.GetJsonProperty(blocks[0].Content, "text").Should().Be("Title");
    }

    [Fact]
    public void ExtractBlocks_ParsesBlockquote()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><blockquote>Quoted text</blockquote></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Blockquote);
    }

    [Fact]
    public void ExtractBlocks_ParsesUnorderedList()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><ul><li>A</li><li>B</li></ul></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.List);
        var content = blocks[0].Content.RootElement;
        content.GetProperty("ordered").GetBoolean().Should().BeFalse();
        content.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ExtractBlocks_ParsesOrderedList()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><ol><li>First</li><li>Second</li></ol></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.List);
        blocks[0].Content.RootElement.GetProperty("ordered").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ExtractBlocks_ParsesCodeBlock()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><pre><code>var x = 1;</code></pre></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Code);
        EpubService.GetJsonProperty(blocks[0].Content, "code").Should().Be("var x = 1;");
    }

    [Fact]
    public void ExtractBlocks_ParsesFigureWithCaption()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><figure><img src=\"img.png\" alt=\"Photo\"/><figcaption>My photo</figcaption></figure></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Figure);
        EpubService.GetJsonProperty(blocks[0].Content, "src").Should().Be("img.png");
        EpubService.GetJsonProperty(blocks[0].Content, "caption").Should().Be("My photo");
        EpubService.GetJsonProperty(blocks[0].Content, "alt").Should().Be("Photo");
    }

    [Fact]
    public void ExtractBlocks_ParsesTable()
    {
        var body = XElement.Parse("""
            <body xmlns="http://www.w3.org/1999/xhtml">
            <table>
                <thead><tr><th>Name</th><th>Value</th></tr></thead>
                <tbody><tr><td>A</td><td>1</td></tr></tbody>
            </table>
            </body>
            """);
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Table);
        var content = blocks[0].Content.RootElement;
        content.GetProperty("headers").GetArrayLength().Should().Be(2);
        content.GetProperty("rows").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void ExtractBlocks_ParsesAside()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><aside>Side note</aside></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be(BlockTypes.Aside);
    }

    [Fact]
    public void ExtractBlocks_DetectsInlineStyles()
    {
        var body = XElement.Parse("<body xmlns=\"http://www.w3.org/1999/xhtml\"><p style=\"color:red\">Styled</p></body>");
        var blocks = new List<Block>();
        var sortOrder = 0;
        var warnings = new List<string>();

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, warnings);

        warnings.Should().Contain(w => w.Contains("inline style"));
    }

    [Fact]
    public void ExtractBlocks_ParsesMultipleBlockTypes()
    {
        var body = XElement.Parse("""
            <body xmlns="http://www.w3.org/1999/xhtml">
                <h1>Chapter 1</h1>
                <p>Some text</p>
                <blockquote>A quote</blockquote>
                <pre><code>code</code></pre>
            </body>
            """);
        var blocks = new List<Block>();
        var sortOrder = 0;

        EpubService.ExtractBlocksFromElement(body, blocks, ref sortOrder, new List<string>());

        blocks.Should().HaveCount(4);
        blocks[0].Type.Should().Be(BlockTypes.Heading);
        blocks[1].Type.Should().Be(BlockTypes.Paragraph);
        blocks[2].Type.Should().Be(BlockTypes.Blockquote);
        blocks[3].Type.Should().Be(BlockTypes.Code);
    }

    // ────────────────────────── Metadata Parsing ──────────────────────────

    [Fact]
    public void ExtractMetadata_ParsesAllFields()
    {
        var opf = XDocument.Parse("""
            <package xmlns="http://www.idpf.org/2007/opf">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>Test Book</dc:title>
                <dc:creator>John Doe</dc:creator>
                <dc:language>en</dc:language>
                <dc:publisher>Acme Press</dc:publisher>
                <dc:description>A test book</dc:description>
                <dc:identifier scheme="ISBN">9781234567890</dc:identifier>
              </metadata>
            </package>
            """);

        var metadata = EpubService.ExtractMetadata(opf);

        metadata.Title.Should().Be("Test Book");
        metadata.Author.Should().Be("John Doe");
        metadata.Language.Should().Be("en");
        metadata.Publisher.Should().Be("Acme Press");
        metadata.Description.Should().Be("A test book");
        metadata.Isbn.Should().Be("9781234567890");
    }

    [Fact]
    public void ExtractMetadata_HandlesMinimalMetadata()
    {
        var opf = XDocument.Parse("""
            <package xmlns="http://www.idpf.org/2007/opf">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>Minimal</dc:title>
              </metadata>
            </package>
            """);

        var metadata = EpubService.ExtractMetadata(opf);

        metadata.Title.Should().Be("Minimal");
        metadata.Author.Should().BeNull();
        metadata.Language.Should().BeNull();
    }

    [Fact]
    public void ExtractMetadata_HandlesNoMetadataElement()
    {
        var opf = XDocument.Parse("<package xmlns=\"http://www.idpf.org/2007/opf\"></package>");

        var metadata = EpubService.ExtractMetadata(opf);

        metadata.Title.Should().Be("Unknown");
    }

    // ────────────────────────── Analysis ──────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsMissingMetadata()
    {
        using var stream = CreateMinimalEpub(title: "", author: "");

        var result = await _service.AnalyzeAsync(stream);

        result.Issues.Should().Contain(i => i.Category == "metadata" && i.Severity == "error" && i.Description.Contains("title"));
        result.Issues.Should().Contain(i => i.Category == "metadata" && i.Severity == "warning" && i.Description.Contains("author"));
    }

    [Fact]
    public async Task AnalyzeAsync_DetectsMissingAltText()
    {
        using var stream = CreateEpubWithContent("<body xmlns=\"http://www.w3.org/1999/xhtml\"><img src=\"test.png\"/></body>",
            title: "Test", author: "Author");

        var result = await _service.AnalyzeAsync(stream);

        result.Issues.Should().Contain(i => i.Category == "accessibility" && i.Description.Contains("alt text"));
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsChapterAndBlockCounts()
    {
        using var stream = CreateEpubWithContent(
            "<body xmlns=\"http://www.w3.org/1999/xhtml\"><h1>Ch1</h1><p>Text</p></body>",
            title: "Test", author: "Author");

        var result = await _service.AnalyzeAsync(stream);

        result.BlockCount.Should().BeGreaterThan(0);
        result.ChapterCount.Should().BeGreaterThanOrEqualTo(1);
        result.FileSizeBytes.Should().BeGreaterThan(0);
    }

    // ────────────────────────── Chapter Splitting ──────────────────────────

    [Fact]
    public void SplitIntoChapters_SplitsOnChapterBreak()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Intro" }, 0),
            EpubService.CreateBlock(BlockTypes.ChapterBreak, new { }, 1),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Chapter 2" }, 2)
        };

        var chapters = EpubService.SplitIntoChapters(blocks);

        chapters.Should().HaveCount(2);
        chapters[0].Should().HaveCount(1);
        chapters[1].Should().HaveCount(1);
    }

    [Fact]
    public void SplitIntoChapters_SplitsOnH1()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Chapter 1", level = 1 }, 0),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Text" }, 1),
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Chapter 2", level = 1 }, 2),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "More text" }, 3)
        };

        var chapters = EpubService.SplitIntoChapters(blocks);

        chapters.Should().HaveCount(2);
    }

    [Fact]
    public void SplitIntoChapters_DoesNotSplitOnH2()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Chapter 1", level = 1 }, 0),
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Section 1.1", level = 2 }, 1),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Text" }, 2)
        };

        var chapters = EpubService.SplitIntoChapters(blocks);

        chapters.Should().HaveCount(1);
    }

    // ────────────────────────── Block Rendering (BlockToXhtml) ──────────────────────────

    [Fact]
    public void BlockToXhtml_Paragraph_RendersCorrectly()
    {
        var block = EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Hello world" }, 0);
        EpubService.BlockToXhtml(block).Should().Be("<p>Hello world</p>");
    }

    [Fact]
    public void BlockToXhtml_Heading_RendersWithLevel()
    {
        var block = EpubService.CreateBlock(BlockTypes.Heading, new { text = "Section", level = 3 }, 0);
        EpubService.BlockToXhtml(block).Should().Be("<h3>Section</h3>");
    }

    [Fact]
    public void BlockToXhtml_Code_RendersPreCode()
    {
        var block = EpubService.CreateBlock(BlockTypes.Code, new { code = "x = 1" }, 0);
        EpubService.BlockToXhtml(block).Should().Contain("<pre><code>");
        EpubService.BlockToXhtml(block).Should().Contain("x = 1");
    }

    [Fact]
    public void BlockToXhtml_Equation_RendersWithClass()
    {
        var block = EpubService.CreateBlock(BlockTypes.Equation, new { latex = "E=mc^2" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("equation");
        result.Should().Contain("E=mc^2");
    }

    [Fact]
    public void BlockToXhtml_Figure_RendersFigureElement()
    {
        var block = EpubService.CreateBlock(BlockTypes.Figure, new { src = "img.png", caption = "Cap", alt = "Alt" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("<figure>");
        result.Should().Contain("img.png");
        result.Should().Contain("<figcaption>Cap</figcaption>");
    }

    [Fact]
    public void BlockToXhtml_List_RendersOrderedList()
    {
        var block = EpubService.CreateBlock(BlockTypes.List, new { items = new[] { "A", "B" }, ordered = true }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("<ol>");
        result.Should().Contain("<li>A</li>");
    }

    [Fact]
    public void BlockToXhtml_Table_RendersHeaders()
    {
        var block = EpubService.CreateBlock(BlockTypes.Table, new { headers = new[] { "H1" }, rows = new[] { new[] { "V1" } } }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("<th>H1</th>");
        result.Should().Contain("<td>V1</td>");
    }

    [Fact]
    public void BlockToXhtml_Verse_RendersVerseClass()
    {
        var block = EpubService.CreateBlock(BlockTypes.Verse, new { text = "Roses are red" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("verse");
        result.Should().Contain("Roses are red");
    }

    [Fact]
    public void BlockToXhtml_Aside_RendersAsideElement()
    {
        var block = EpubService.CreateBlock(BlockTypes.Aside, new { text = "Side note" }, 0);
        EpubService.BlockToXhtml(block).Should().Be("<aside>Side note</aside>");
    }

    [Fact]
    public void BlockToXhtml_FrontMatter_RendersWithEpubType()
    {
        var block = EpubService.CreateBlock(BlockTypes.FrontMatter, new { text = "Preface" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("frontmatter");
        result.Should().Contain("Preface");
    }

    [Fact]
    public void BlockToXhtml_BackMatter_RendersWithEpubType()
    {
        var block = EpubService.CreateBlock(BlockTypes.BackMatter, new { text = "Appendix" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("backmatter");
        result.Should().Contain("Appendix");
    }

    [Fact]
    public void BlockToXhtml_Cover_RendersCoverDiv()
    {
        var block = EpubService.CreateBlock(BlockTypes.Cover, new { src = "cover.jpg" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("cover");
        result.Should().Contain("cover.jpg");
    }

    [Fact]
    public void BlockToXhtml_Annotation_RendersAnnotationAside()
    {
        var block = EpubService.CreateBlock(BlockTypes.Annotation, new { text = "Note" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("annotation");
        result.Should().Contain("Note");
    }

    [Fact]
    public void BlockToXhtml_Blockquote_RendersBlockquote()
    {
        var block = EpubService.CreateBlock(BlockTypes.Blockquote, new { text = "Famous quote" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("<blockquote>");
        result.Should().Contain("Famous quote");
    }

    [Fact]
    public void BlockToXhtml_Abstract_RendersAbstractSection()
    {
        var block = EpubService.CreateBlock(BlockTypes.Abstract, new { text = "Summary text", title = "Abstract" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("abstract");
        result.Should().Contain("Summary text");
    }

    [Fact]
    public void BlockToXhtml_EscapesXmlCharacters()
    {
        var block = EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "A < B & C > D" }, 0);
        var result = EpubService.BlockToXhtml(block);
        result.Should().Contain("&lt;");
        result.Should().Contain("&amp;");
        result.Should().Contain("&gt;");
    }

    // ────────────────────────── Export ──────────────────────────

    [Fact]
    public async Task ExportAsync_GeneratesValidOpfStructure()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Title", level = 1 }, 0),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Content" }, 1)
        };
        var options = new EpubExportOptions("My Book", Author: "Author", Language: "en");

        var epub = await _service.ExportAsync(blocks, options);

        using var ms = new MemoryStream(epub);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // Check mimetype
        var mimetypeEntry = zip.GetEntry("mimetype");
        mimetypeEntry.Should().NotBeNull();
        using (var reader = new StreamReader(mimetypeEntry!.Open()))
        {
            var mimetype = await reader.ReadToEndAsync();
            mimetype.Should().Be("application/epub+zip");
        }

        // Check content.opf exists and has correct structure
        var opfEntry = zip.GetEntry("OEBPS/content.opf");
        opfEntry.Should().NotBeNull();
        using (var reader = new StreamReader(opfEntry!.Open()))
        {
            var opfContent = await reader.ReadToEndAsync();
            opfContent.Should().Contain("<dc:title>My Book</dc:title>");
            opfContent.Should().Contain("<dc:creator>Author</dc:creator>");
            opfContent.Should().Contain("<dc:language>en</dc:language>");
            opfContent.Should().Contain("<manifest>");
            opfContent.Should().Contain("<spine");
        }
    }

    [Fact]
    public async Task ExportAsync_GeneratesNavXhtml()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Chapter One", level = 1 }, 0),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Content" }, 1)
        };
        var options = new EpubExportOptions("Nav Test");

        var epub = await _service.ExportAsync(blocks, options);

        using var ms = new MemoryStream(epub);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var navEntry = zip.GetEntry("OEBPS/nav.xhtml");
        navEntry.Should().NotBeNull();
        using var reader = new StreamReader(navEntry!.Open());
        var nav = await reader.ReadToEndAsync();
        nav.Should().Contain("epub:type=\"toc\"");
        nav.Should().Contain("Chapter One");
    }

    [Fact]
    public async Task ExportAsync_GeneratesTocNcx()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "Ch1", level = 1 }, 0),
        };
        var options = new EpubExportOptions("NCX Test");

        var epub = await _service.ExportAsync(blocks, options);

        using var ms = new MemoryStream(epub);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var ncxEntry = zip.GetEntry("OEBPS/toc.ncx");
        ncxEntry.Should().NotBeNull();
        using var reader = new StreamReader(ncxEntry!.Open());
        var ncx = await reader.ReadToEndAsync();
        ncx.Should().Contain("<ncx");
        ncx.Should().Contain("navPoint");
    }

    // ────────────────────────── Clean ──────────────────────────

    [Fact]
    public async Task CleanAndRepackageAsync_ProducesValidEpub()
    {
        using var input = CreateEpubWithContent(
            "<body xmlns=\"http://www.w3.org/1999/xhtml\"><p style=\"color:red;font-size:14px\">Styled text</p></body>",
            title: "Dirty Book", author: "Author");

        var cleaned = await _service.CleanAndRepackageAsync(input);

        cleaned.Should().NotBeEmpty();

        using var ms = new MemoryStream(cleaned);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        zip.GetEntry("mimetype").Should().NotBeNull();
        zip.GetEntry("OEBPS/content.opf").Should().NotBeNull();
        zip.GetEntry("META-INF/container.xml").Should().NotBeNull();
    }

    // ────────────────────────── Import Full ePub ──────────────────────────

    [Fact]
    public async Task ImportAsync_ParsesFullEpub()
    {
        using var stream = CreateEpubWithContent(
            "<body xmlns=\"http://www.w3.org/1999/xhtml\"><h1>Chapter</h1><p>Text</p></body>",
            title: "Import Test", author: "Tester");

        var (metadata, blocks, warnings) = await _service.ImportAsync(stream);

        metadata.Title.Should().Be("Import Test");
        metadata.Author.Should().Be("Tester");
        blocks.Should().HaveCountGreaterThanOrEqualTo(2);
        blocks.Should().Contain(b => b.Type == BlockTypes.Heading);
        blocks.Should().Contain(b => b.Type == BlockTypes.Paragraph);
    }

    // ────────────────────────── Heading Hierarchy ──────────────────────────

    [Fact]
    public async Task AnalyzeAsync_DetectsInconsistentHeadingHierarchy()
    {
        using var stream = CreateEpubWithContent(
            "<body xmlns=\"http://www.w3.org/1999/xhtml\"><h1>Title</h1><h4>Skipped</h4></body>",
            title: "Hierarchy Test", author: "Author");

        var result = await _service.AnalyzeAsync(stream);

        result.Issues.Should().Contain(i =>
            i.Category == "structure" &&
            i.Description.Contains("Heading level jumps"));
    }

    // ────────────────────────── Round-Trip ──────────────────────────

    [Fact]
    public async Task RoundTrip_ExportThenImport_PreservesContent()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Heading, new { text = "My Chapter", level = 1 }, 0),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Body paragraph." }, 1),
            EpubService.CreateBlock(BlockTypes.Code, new { code = "x = 42", language = "python" }, 2),
            EpubService.CreateBlock(BlockTypes.Blockquote, new { text = "A famous quote." }, 3)
        };
        var options = new EpubExportOptions("Round Trip", Author: "Tester");

        var epubBytes = await _service.ExportAsync(blocks, options);

        using var importStream = new MemoryStream(epubBytes);
        var (metadata, imported, _) = await _service.ImportAsync(importStream);

        metadata.Title.Should().Be("Round Trip");
        metadata.Author.Should().Be("Tester");
        imported.Should().Contain(b => b.Type == BlockTypes.Heading);
        imported.Should().Contain(b => b.Type == BlockTypes.Paragraph);
        imported.Should().Contain(b => b.Type == BlockTypes.Code);
        imported.Should().Contain(b => b.Type == BlockTypes.Blockquote);

        var heading = imported.First(b => b.Type == BlockTypes.Heading);
        EpubService.GetJsonProperty(heading.Content, "text").Should().Be("My Chapter");
    }

    [Fact]
    public async Task ExportAsync_ChapterBreak_CreatesMultipleFiles()
    {
        var blocks = new List<Block>
        {
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Part 1" }, 0),
            EpubService.CreateBlock(BlockTypes.ChapterBreak, new { }, 1),
            EpubService.CreateBlock(BlockTypes.Paragraph, new { text = "Part 2" }, 2)
        };
        var options = new EpubExportOptions("Split Test");

        var epub = await _service.ExportAsync(blocks, options);

        using var ms = new MemoryStream(epub);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        zip.GetEntry("OEBPS/chapter1.xhtml").Should().NotBeNull();
        zip.GetEntry("OEBPS/chapter2.xhtml").Should().NotBeNull();

        using var r1 = new StreamReader(zip.GetEntry("OEBPS/chapter1.xhtml")!.Open());
        (await r1.ReadToEndAsync()).Should().Contain("Part 1");

        using var r2 = new StreamReader(zip.GetEntry("OEBPS/chapter2.xhtml")!.Open());
        (await r2.ReadToEndAsync()).Should().Contain("Part 2");
    }

    // ────────────────────────── Helpers ──────────────────────────

    private static MemoryStream CreateMinimalEpub(string title = "Test", string author = "Author")
    {
        return CreateEpubWithContent(
            "<body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Content</p></body>",
            title, author);
    }

    private static MemoryStream CreateEpubWithContent(string bodyXhtml, string title, string author)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // mimetype
            AddEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);

            // container.xml
            AddEntry(zip, "META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);

            // content.opf
            var titleXml = string.IsNullOrEmpty(title) ? "" : $"<dc:title>{title}</dc:title>";
            var authorXml = string.IsNullOrEmpty(author) ? "" : $"<dc:creator>{author}</dc:creator>";
            AddEntry(zip, "OEBPS/content.opf", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    {titleXml}
                    {authorXml}
                    <dc:identifier id="bookid">test-id</dc:identifier>
                  </metadata>
                  <manifest>
                    <item id="ch1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
                  </manifest>
                  <spine>
                    <itemref idref="ch1"/>
                  </spine>
                </package>
                """);

            // chapter1.xhtml
            AddEntry(zip, "OEBPS/chapter1.xhtml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE html>
                <html xmlns="http://www.w3.org/1999/xhtml">
                <head><title>{title}</title></head>
                {bodyXhtml}
                </html>
                """);
        }

        ms.Position = 0;
        return ms;
    }

    private static void AddEntry(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
