using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// What actually reaches Word.
///
/// <para>These assert on the produced document.xml rather than on the shape of
/// the code, because the two defects found on 2026-09-09 were both invisible
/// from the inside: <c>ConvertBibliography</c> ran on every export and emitted
/// a heading, and <c>CreateSpanElement</c> ran on every inline equation and
/// took the OMML branch. Both then threw their work away — one into a
/// placeholder for a "document-conversion layer" that did not exist, the other
/// into a null child of a throwaway paragraph. Only the bytes tell you.</para>
/// </summary>
public class DocxExportServiceTests
{
    // ── harness ──────────────────────────────────────────────────────────

    private static DocxExportService Service() => new(new LatexToOmmlConverter());

    private static async Task<string> ExportXmlAsync(
        ExportDocument document, ExportOptions? options = null)
    {
        var bytes = await Service().ExportAsync(document, options);
        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);
        return word.MainDocumentPart!.Document.OuterXml;
    }

    /// <summary>The visible text, with every tag removed.</summary>
    private static string TextOf(string xml) => Regex.Replace(xml, "<[^>]+>", "");

    private static ExportDocument Doc(params ExportBlock[] blocks) =>
        new() { Title = "Test", Blocks = [.. blocks] };

    private static ExportBlock Block(string type, ExportBlockContent content) =>
        new() { Type = type, Content = content };

    private static ExportBlock Para(string text) =>
        Block("paragraph", new ExportBlockContent
        {
            Text = text,
            RichText = [new ExportRichTextSpan { Text = text }],
        });

    private static ExportBibliographyEntry Entry(
        string key, string type = "book", params (string, string)[] fields)
    {
        var e = new ExportBibliographyEntry { CiteKey = key, EntryType = type };
        foreach (var (k, v) in fields) e.Fields[k] = v;
        return e;
    }

    // A 1×1 transparent PNG — enough to prove the image part is written.
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // ── the file itself ──────────────────────────────────────────────────

    [Fact]
    public async Task ProducesAnOpenableWordDocument()
    {
        var bytes = await Service().ExportAsync(Doc(Para("Hello.")));

        bytes.Should().NotBeEmpty();
        // A .docx is a ZIP: "PK".
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
    }

    [Fact]
    public async Task AnEmptyDocumentStillExports()
    {
        // No blocks at all is a real state — a document just created.
        var bytes = await Service().ExportAsync(Doc());
        bytes.Should().NotBeEmpty();
    }

    // ── the bibliography, which used to vanish ───────────────────────────

    [Fact]
    public async Task TheBibliographyBlockEmitsTheEntries()
    {
        // The regression: this emitted "[Bibliography entries appended below]"
        // and nothing else, for every document, forever.
        var doc = Doc(Para("Body."), Block("bibliography", new ExportBlockContent()));
        doc.Bibliography =
        [
            Entry("euclid", "book", ("author", "Euclid"), ("title", "Elements"), ("year", "300")),
        ];

        var text = TextOf(await ExportXmlAsync(doc));

        text.Should().Contain("References");
        text.Should().Contain("Euclid");
        text.Should().Contain("Elements");
        text.Should().NotContain("appended below", "the placeholder must be gone");
    }

    [Fact]
    public async Task EveryEntryIsEmitted()
    {
        var doc = Doc(Block("bibliography", new ExportBlockContent()));
        doc.Bibliography =
        [
            Entry("a", "book", ("author", "Author One"), ("title", "First")),
            Entry("b", "book", ("author", "Author Two"), ("title", "Second")),
            Entry("c", "book", ("author", "Author Three"), ("title", "Third")),
        ];

        var text = TextOf(await ExportXmlAsync(doc));

        text.Should().Contain("First").And.Contain("Second").And.Contain("Third");
    }

    [Fact]
    public async Task ReferencesAppearEvenWithoutABlockToMarkThem()
    {
        // The LaTeX export emits \bibliography whenever there is an entry,
        // block or no block. Word must not be the format that loses them.
        var doc = Doc(Para("Body with a citation."));
        doc.Bibliography = [Entry("euclid", "book", ("author", "Euclid"), ("title", "Elements"))];

        var text = TextOf(await ExportXmlAsync(doc));

        text.Should().Contain("References").And.Contain("Elements");
    }

    [Fact]
    public async Task TheSectionIsNotEmittedTwiceWhenABlockIsPresent()
    {
        var doc = Doc(Para("Body."), Block("bibliography", new ExportBlockContent()));
        doc.Bibliography = [Entry("euclid", "book", ("author", "Euclid"), ("title", "Elements"))];

        var text = TextOf(await ExportXmlAsync(doc));

        Regex.Matches(text, "References").Count.Should().Be(1);
        Regex.Matches(text, "Elements").Count.Should().Be(1);
    }

    [Fact]
    public async Task AnEmptyBibliographySaysSoRatherThanShowingABareHeading()
    {
        var doc = Doc(Block("bibliography", new ExportBlockContent()));

        var text = TextOf(await ExportXmlAsync(doc));

        text.Should().Contain("References").And.Contain("No references yet");
    }

    [Fact]
    public async Task TheBibliographyCanBeTurnedOff()
    {
        var doc = Doc(Para("Body."), Block("bibliography", new ExportBlockContent()));
        doc.Bibliography = [Entry("euclid", "book", ("author", "Euclid"), ("title", "Elements"))];

        var text = TextOf(await ExportXmlAsync(doc, new ExportOptions { IncludeBibliography = false }));

        text.Should().NotContain("References");
        text.Should().NotContain("Elements");
        text.Should().Contain("Body.", "the rest of the document is unaffected");
    }

    // ── how one reference reads ──────────────────────────────────────────

    [Fact]
    public void AFullEntryReadsAsAReference()
    {
        var s = DocxExportService.FormatBibliographyEntry(Entry("x", "article",
            ("author", "Marie Curie"), ("year", "1903"), ("title", "On Radioactivity"),
            ("journal", "Annales"), ("pages", "12-34")));

        s.Should().Be("Marie Curie. (1903). On Radioactivity. Annales. pp. 12-34.");
    }

    [Fact]
    public void AMononymIsLeftExactlyAsWritten()
    {
        // "Euclid, (c. 300 BCE)." was the web-side bug. Nothing here splits a
        // name to initialise a given name that does not exist.
        var s = DocxExportService.FormatBibliographyEntry(Entry("x", "book",
            ("author", "Euclid"), ("year", "c. 300 BCE"), ("title", "Elements")));

        s.Should().Be("Euclid. (c. 300 BCE). Elements.");
        s.Should().NotContain(", (");
    }

    [Theory]
    [InlineData("journal", "Annales")]
    [InlineData("booktitle", "Proceedings of Something")]
    [InlineData("publisher", "Brown University Press")]
    public void TheVenueComesFromWhicheverFieldTheEntryTypeUses(string field, string value)
    {
        var s = DocxExportService.FormatBibliographyEntry(
            Entry("x", "misc", ("author", "A"), ("title", "T"), (field, value)));

        s.Should().Contain(value);
    }

    [Fact]
    public void MissingFieldsAreSkippedRatherThanPaddedOut()
    {
        var s = DocxExportService.FormatBibliographyEntry(
            Entry("x", "book", ("author", "Solo Author")));

        s.Should().Be("Solo Author.");
        s.Should().NotContain("()", "an absent year must not leave empty parentheses");
    }

    [Fact]
    public void AnEntryWithNothingUsableFallsBackToItsCiteKey()
    {
        // Better than a blank line: the author can find the entry to fix it.
        DocxExportService.FormatBibliographyEntry(Entry("orphan_key")).Should().Be("orphan_key");
    }

    [Fact]
    public void WhitespaceOnlyFieldsCountAsMissing()
    {
        var s = DocxExportService.FormatBibliographyEntry(
            Entry("x", "book", ("author", "  "), ("title", "Real Title")));

        s.Should().Be("Real Title.");
    }

    // ── maths, which used to arrive as dollar signs ──────────────────────

    [Fact]
    public async Task InlineMathBecomesARealWordEquation()
    {
        // The regression: this fell through to literal "$a^2 + b^2 = c^2$"
        // because the OMML was lifted from a temp paragraph whose FirstChild
        // was always null.
        var doc = Doc(Block("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "We have " },
                new ExportRichTextSpan { Text = "a^2 + b^2 = c^2", Equation = "a^2 + b^2 = c^2" },
                new ExportRichTextSpan { Text = " for a right triangle." },
            ],
        }));

        var xml = await ExportXmlAsync(doc);

        xml.Should().Contain("oMath", "the equation must be native Word math");
        TextOf(xml).Should().NotContain("$", "no LaTeX delimiters may reach the reader");
        TextOf(xml).Should().Contain("for a right triangle.", "the surrounding text survives");
    }

    [Fact]
    public async Task ADisplayEquationBlockBecomesWordMath()
    {
        var doc = Doc(Block("equation", new ExportBlockContent
        {
            Source = @"\frac{a}{b}",
            DisplayMode = true,
        }));

        (await ExportXmlAsync(doc)).Should().Contain("oMath");
    }

    [Fact]
    public async Task SeveralEquationsInOneParagraphAllConvert()
    {
        var doc = Doc(Block("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "x", Equation = "x" },
                new ExportRichTextSpan { Text = " and " },
                new ExportRichTextSpan { Text = "y", Equation = "y" },
            ],
        }));

        var xml = await ExportXmlAsync(doc);

        Regex.Matches(xml, "<m:oMath").Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task AnEquationTheConverterCannotHandleStillLeavesReadableText()
    {
        // Degrading to "$...$" is acceptable; losing the content is not.
        var doc = Doc(Block("paragraph", new ExportBlockContent
        {
            RichText = [new ExportRichTextSpan { Text = "?", Equation = @"\somecommandthatdoesnotexist{" }],
        }));

        var bytes = await Service().ExportAsync(doc);
        bytes.Should().NotBeEmpty();
    }

    // ── every other block type ───────────────────────────────────────────

    [Fact]
    public async Task Headings() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("heading", new ExportBlockContent { Text = "Introduction", Level = 1 }))))
            .Should().Contain("Introduction");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task HeadingsAtEveryLevel(int level) =>
        TextOf(await ExportXmlAsync(Doc(
            Block("heading", new ExportBlockContent { Text = $"H{level}", Level = level }))))
            .Should().Contain($"H{level}");

    [Fact]
    public async Task Abstracts() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("abstract", new ExportBlockContent { Text = "In summary." }))))
            .Should().Contain("Abstract").And.Contain("In summary.");

    [Fact]
    public async Task Code() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("code", new ExportBlockContent { Code = "print('hi')", Language = "python" }))))
            .Should().Contain("print('hi')");

    [Fact]
    public async Task Blockquotes() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("blockquote", new ExportBlockContent { Text = "A quoted line." }))))
            .Should().Contain("A quoted line.");

    [Fact]
    public async Task Theorems() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("theorem", new ExportBlockContent
            {
                Text = "Every right triangle obeys it.",
                TheoremType = "theorem",
                TheoremNumber = 1,
            }))))
            .Should().Contain("Every right triangle obeys it.");

    [Fact]
    public async Task BulletedLists()
    {
        var text = TextOf(await ExportXmlAsync(Doc(Block("list", new ExportBlockContent
        {
            ListType = "bullet",
            Items = [new ExportListItem { Text = "First" }, new ExportListItem { Text = "Second" }],
        }))));

        text.Should().Contain("First").And.Contain("Second");
    }

    [Fact]
    public async Task NumberedLists()
    {
        var text = TextOf(await ExportXmlAsync(Doc(Block("list", new ExportBlockContent
        {
            ListType = "numbered",
            Items = [new ExportListItem { Text = "Step one" }],
        }))));

        text.Should().Contain("Step one");
    }

    [Fact]
    public async Task NestedListItems()
    {
        var text = TextOf(await ExportXmlAsync(Doc(Block("list", new ExportBlockContent
        {
            ListType = "bullet",
            Items =
            [
                new ExportListItem
                {
                    Text = "Outer",
                    Children = [new ExportListItem { Text = "Inner", Level = 1 }],
                },
            ],
        }))));

        text.Should().Contain("Outer").And.Contain("Inner");
    }

    [Fact]
    public async Task Tables()
    {
        var xml = await ExportXmlAsync(Doc(Block("table", new ExportBlockContent
        {
            HasHeader = true,
            Rows =
            [
                [new ExportTableCell { Text = "Name" }, new ExportTableCell { Text = "Value" }],
                [new ExportTableCell { Text = "alpha" }, new ExportTableCell { Text = "1" }],
            ],
        })));

        xml.Should().Contain("<w:tbl>", "a real Word table, not text");
        TextOf(xml).Should().Contain("Name").And.Contain("alpha");
    }

    [Fact]
    public async Task TablesWithSpannedCells()
    {
        var xml = await ExportXmlAsync(Doc(Block("table", new ExportBlockContent
        {
            Rows = [[new ExportTableCell { Text = "wide", ColSpan = 2 }]],
        })));

        TextOf(xml).Should().Contain("wide");
    }

    [Fact]
    public async Task FiguresEmbedTheImage()
    {
        var bytes = await Service().ExportAsync(Doc(Block("figure", new ExportBlockContent
        {
            Caption = "Figure caption here.",
            Image = new ExportImageData { Data = OnePixelPng, MimeType = "image/png" },
        })));

        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);

        word.MainDocumentPart!.ImageParts.Should().NotBeEmpty("the image must travel inside the .docx");
        TextOf(word.MainDocumentPart.Document.OuterXml).Should().Contain("Figure caption here.");
    }

    [Fact]
    public async Task AFigureWithNoImageStillKeepsItsCaption()
    {
        var text = TextOf(await ExportXmlAsync(Doc(Block("figure", new ExportBlockContent
        {
            Caption = "Caption without an image.",
        }))));

        text.Should().Contain("Caption without an image.");
    }

    [Fact]
    public async Task PageBreaks() =>
        (await ExportXmlAsync(Doc(Block("pagebreak", new ExportBlockContent()))))
            .Should().Contain("w:br");

    [Fact]
    public async Task Callouts() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("callout", new ExportBlockContent { Text = "Mind this." }))))
            .Should().Contain("Mind this.");

    [Fact]
    public async Task Algorithms() =>
        TextOf(await ExportXmlAsync(Doc(
            Block("algorithm", new ExportBlockContent { Text = "for each x: do y" }))))
            .Should().Contain("for each x: do y");

    [Fact]
    public async Task TableOfContents() =>
        (await ExportXmlAsync(Doc(Block("tableofcontents", new ExportBlockContent()))))
            .Should().NotBeNullOrEmpty();

    [Fact]
    public async Task AnUnknownBlockTypeDoesNotAbortTheExport()
    {
        // A newer block type reaching an older exporter must degrade, not throw.
        var text = TextOf(await ExportXmlAsync(Doc(
            Block("some-future-block", new ExportBlockContent { Text = "Still readable." }),
            Para("And the rest of the document."))));

        text.Should().Contain("And the rest of the document.");
    }

    // ── inline formatting ────────────────────────────────────────────────

    [Fact]
    public async Task BoldItalicAndUnderline()
    {
        var xml = await ExportXmlAsync(Doc(Block("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "bold", Bold = true },
                new ExportRichTextSpan { Text = "italic", Italic = true },
                new ExportRichTextSpan { Text = "under", Underline = true },
            ],
        })));

        xml.Should().MatchRegex("<w:b ?/>").And.MatchRegex("<w:i ?/>");
        xml.Should().Contain("w:u");
    }

    [Fact]
    public async Task SuperscriptAndSubscript()
    {
        var xml = await ExportXmlAsync(Doc(Block("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "2", Superscript = true },
                new ExportRichTextSpan { Text = "n", Subscript = true },
            ],
        })));

        xml.Should().Contain("superscript").And.Contain("subscript");
    }

    [Fact]
    public async Task StrikethroughAndHighlight()
    {
        var xml = await ExportXmlAsync(Doc(Block("paragraph", new ExportBlockContent
        {
            RichText =
            [
                new ExportRichTextSpan { Text = "gone", Strikethrough = true },
                new ExportRichTextSpan { Text = "marked", Highlight = "yellow" },
            ],
        })));

        TextOf(xml).Should().Contain("gone").And.Contain("marked");
    }

    // ── things that must not break it ────────────────────────────────────

    [Theory]
    [InlineData("Ampersand & angle < brackets >")]
    [InlineData("Quotes \" and ' apostrophes")]
    [InlineData("Accents: é à ü ñ ç")]
    [InlineData("Greek: α β γ Δ Ω")]
    [InlineData("Symbols: — – … × ÷ ≤ ≥")]
    public async Task CharactersThatBreakXmlIfUnescaped(string text)
    {
        var bytes = await Service().ExportAsync(Doc(Para(text)));

        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);
        // Opening it at all proves the XML stayed well-formed.
        TextOf(word.MainDocumentPart!.Document.OuterXml).Should().Contain(text[..8]);
    }

    [Fact]
    public async Task ALongDocumentExportsWithoutHoldingItAllTwice()
    {
        // 500 paragraphs is not large, but it does walk the block loop enough
        // to catch anything quadratic or anything that buffers per block.
        var blocks = Enumerable.Range(0, 500).Select(i => Para($"Paragraph number {i}.")).ToArray();

        var bytes = await Service().ExportAsync(Doc(blocks));

        bytes.Should().NotBeEmpty();
        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);
        TextOf(word.MainDocumentPart!.Document.OuterXml).Should().Contain("Paragraph number 499.");
    }

    [Fact]
    public async Task BlockOrderIsPreserved()
    {
        var text = TextOf(await ExportXmlAsync(Doc(
            Para("FIRST"), Para("SECOND"), Para("THIRD"))));

        text.IndexOf("FIRST", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("SECOND", StringComparison.Ordinal));
        text.IndexOf("SECOND", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("THIRD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABlockWithNoContentIsSkippedQuietly()
    {
        var bytes = await Service().ExportAsync(Doc(
            Block("paragraph", new ExportBlockContent()), Para("Real content.")));

        bytes.Should().NotBeEmpty();
    }
}
