using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Export.Docx;

/// <summary>
/// The launch bot's languages run (#151, 17 Nov 2026), on the Word side: an Arabic document's paragraphs were
/// left to right (launch-bot #155); "Abstract", "Table" and "References" stayed English in French, German,
/// Chinese and Arabic documents, and the file set no language (launch-bot #156); a \footnote in a paragraph
/// printed in brackets inside it (launch-bot #157). The PDF did all three right.
/// </summary>
public class WordLanguageTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static XDocument Part(byte[] docx, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var entry = zip.GetEntry(name);
        if (entry is null) return new XDocument();
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static string Text(XElement e) => string.Concat(e.Descendants(W + "t").Select(t => t.Value));

    private static ExportDocument Doc(string language, params ExportBlock[] blocks)
    {
        var export = new ExportDocument { Title = "T", Blocks = [.. blocks] };
        // As the Word export builds it from the stored document (BuildExportDocumentAsync).
        DocumentExportService.ApplyLanguage(export, new Document { Title = "T", Language = language });
        return export;
    }

    private static ExportBlock Paragraph(string text) => new()
    {
        Type = "paragraph",
        Content = new ExportBlockContent { Text = text, RichText = DocumentExportService.ParseInlineFormatting(text, null) },
    };

    private static ExportBlock Abstract(string text) => new()
    {
        Type = "abstract",
        Content = new ExportBlockContent { Text = text, RichText = DocumentExportService.ParseInlineFormatting(text, null) },
    };

    private static ExportBlock Table(string caption) => new()
    {
        Type = "table",
        Content = new ExportBlockContent { Caption = caption, Rows = [[new ExportTableCell { Text = "a" }, new ExportTableCell { Text = "1" }]] },
    };

    private static List<string> SchemaErrors(byte[] docx)
    {
        using var word = WordprocessingDocument.Open(new MemoryStream(docx), false);
        // A table's own properties have older schema-order slips (w:tblBorders before w:tblW, w:left before
        // w:bottom), outside these issues; everything this change writes is checked.
        return new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(word)
            .Where(e => e.Path?.XPath?.Contains("/w:tblPr[") != true)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}").ToList();
    }

    // ── launch-bot #155: right to left ─────────────────────────────────────

    [Fact]
    public async Task Arabic_paragraphs_run_right_to_left_and_equations_and_code_stay_left_to_right()
    {
        var equation = new ExportBlock { Type = "equation", Content = new ExportBlockContent { Latex = "E = mc^2", DisplayMode = true } };
        var code = new ExportBlock { Type = "code", Content = new ExportBlockContent { Code = "x = 1" } };
        var doc = Doc("ar", Paragraph("هذه فقرة عربية مع $x$ في النص."), equation, code);

        var bytes = await new DocxExportService(new LatexToOmmlConverter()).ExportAsync(doc);
        var body = Part(bytes, "word/document.xml").Descendants(W + "body").Single();

        var arabic = body.Elements(W + "p").Single(p => Text(p).Contains("فقرة"));
        arabic.Element(W + "pPr")!.Element(W + "bidi").Should().NotBeNull("an Arabic paragraph reads right to left");
        arabic.Descendants(W + "r").Where(r => Text(r).Contains("فقرة"))
            .Should().OnlyContain(r => r.Element(W + "rPr") != null && r.Element(W + "rPr")!.Element(W + "rtl") != null);

        var display = body.Elements(W + "p").Single(p => p.Descendants(M + "oMathPara").Any());
        display.Descendants(W + "bidi").Should().BeEmpty("an equation stays left to right, as in the PDF");
        var codeLine = body.Elements(W + "p").Single(p => p.Descendants(W + "pStyle").Any(s => s.Attribute(W + "val")!.Value == "Code"));
        codeLine.Descendants(W + "bidi").Should().BeEmpty("code reads left to right, as in the PDF");

        var lang = Part(bytes, "word/styles.xml").Descendants(W + "docDefaults").Descendants(W + "lang").Single();
        lang.Attribute(W + "bidi")!.Value.Should().Be("ar-SA");
        SchemaErrors(bytes).Should().BeEmpty();
    }

    // launch-bot #154 (18 Nov 2026): Arabic bold and italic runs had w:b and w:i only, which Word ignores for Arabic,
    // so the emphasis printed plain. Arabic emphasis is bold, as in the PDF; Latin italics stay italic.
    [Fact]
    public async Task Arabic_emphasis_is_bold_in_word_and_latin_italics_stay()
    {
        var doc = Doc("ar", Paragraph("هذه **فقرة** مع *تأكيد* و *italic* في النص."));

        var bytes = await new DocxExportService().ExportAsync(doc);
        var body = Part(bytes, "word/document.xml").Descendants(W + "body").Single();
        XElement Run(string text) => body.Descendants(W + "r").Single(r => Text(r) == text);

        Run("فقرة").Element(W + "rPr")!.Element(W + "bCs").Should().NotBeNull("Word shows Arabic bold only with w:bCs");
        Run("تأكيد").Element(W + "rPr")!.Element(W + "bCs").Should().NotBeNull("Arabic emphasis is bold, as in the PDF");
        Run("italic").Element(W + "rPr")!.Element(W + "bCs").Should().BeNull("Latin italics stay italic");
        Run("italic").Element(W + "rPr")!.Element(W + "i").Should().NotBeNull();
        SchemaErrors(bytes).Should().BeEmpty();
    }

    // launch-bot #154/#155 (18 Nov 2026): English references in an Arabic document were right-to-left paragraphs, so
    // Word printed "… Codes. [1]" and the full stop at the wrong end.
    [Fact]
    public async Task An_arabic_documents_english_reference_stays_left_to_right()
    {
        var doc = Doc("ar", Paragraph("هذه فقرة عربية."));
        doc.Bibliography =
        [
            new ExportBibliographyEntry { CiteKey = "h", Number = 1, Fields = new() { ["author"] = "D. Huffman", ["title"] = "A Method for the Construction of Minimum-Redundancy Codes", ["year"] = "1952" } },
            new ExportBibliographyEntry { CiteKey = "a", Number = 2, Fields = new() { ["author"] = "مؤلف", ["title"] = "عنوان", ["year"] = "2020" } },
        ];

        var bytes = await new DocxExportService().ExportAsync(doc);
        var body = Part(bytes, "word/document.xml").Descendants(W + "body").Single();

        var english = body.Elements(W + "p").Single(p => Text(p).Contains("Huffman"));
        english.Descendants(W + "bidi").Should().BeEmpty();
        english.Descendants(W + "rtl").Should().BeEmpty();
        var arabic = body.Elements(W + "p").Single(p => Text(p).Contains("عنوان"));
        arabic.Element(W + "pPr")!.Element(W + "bidi").Should().NotBeNull("an Arabic reference still reads right to left");
        body.Elements(W + "p").Single(p => Text(p).Contains("فقرة")).Element(W + "pPr")!.Element(W + "bidi").Should().NotBeNull();
        SchemaErrors(bytes).Should().BeEmpty();
    }

    [Fact]
    public async Task An_english_document_has_no_right_to_left_marks()
    {
        var bytes = await new DocxExportService().ExportAsync(Doc("en", Paragraph("A plain paragraph.")));
        var xml = Part(bytes, "word/document.xml");
        xml.Descendants(W + "bidi").Should().BeEmpty();
        xml.Descendants(W + "rtl").Should().BeEmpty();
        Part(bytes, "word/styles.xml").Descendants(W + "docDefaults").Descendants(W + "lang").Single()
            .Attribute(W + "val")!.Value.Should().Be("en-US");
    }

    // ── launch-bot #156: the document's words and language ─────────────────

    [Theory]
    [InlineData("fr", "Résumé", "Résultats", "Tableau 1 – Résultats", "Références", "fr-FR", "val")]
    [InlineData("de", "Zusammenfassung", "Ergebnisse", "Tabelle 1: Ergebnisse", "Literatur", "de-DE", "val")]
    [InlineData("zh", "摘要", "结果", "表 1: 结果", "参考文献", "zh-CN", "eastAsia")]
    [InlineData("ar", "ملخص", "نتائج", "جدول 1: نتائج", "المراجع", "ar-SA", "bidi")]
    public async Task Words_are_the_pdfs_in_the_documents_language_and_the_language_is_set(
        string language, string abstractName, string tableCaption, string caption, string references, string tag, string attribute)
    {
        var doc = Doc(language, Abstract("Texte."), Table(tableCaption));
        doc.Bibliography = [new ExportBibliographyEntry { CiteKey = "k", Number = 1, Fields = new() { ["author"] = "A. Author", ["title"] = "T", ["year"] = "2020" } }];

        var bytes = await new DocxExportService().ExportAsync(doc);
        var body = Part(bytes, "word/document.xml").Descendants(W + "body").Single();
        var paragraphs = body.Elements(W + "p").Select(Text).ToList();

        paragraphs.Should().Contain(abstractName).And.Contain(references).And.NotContain("Abstract").And.NotContain("References");
        paragraphs.Should().Contain(caption);
        // Word's own field stays "SEQ Table", so its numbering and cross-references keep working.
        body.Descendants(W + "fldSimple").Single().Attribute(W + "instr")!.Value.Should().Be(@" SEQ Table \* ARABIC ");

        var lang = Part(bytes, "word/styles.xml").Descendants(W + "docDefaults").Descendants(W + "lang").Single();
        lang.Attribute(W + attribute)!.Value.Should().Be(tag);
        SchemaErrors(bytes).Should().BeEmpty();
    }

    [Fact]
    public async Task A_german_table_of_contents_and_figure_take_babels_names()
    {
        var toc = new ExportBlock { Type = "tableOfContents", Content = new ExportBlockContent() };
        var figure = new ExportBlock { Type = "figure", Content = new ExportBlockContent { Caption = "Schéma" } };
        var bytes = await new DocxExportService().ExportAsync(Doc("de", toc, figure));
        var paragraphs = Part(bytes, "word/document.xml").Descendants(W + "p").Select(Text).ToList();
        paragraphs.Should().Contain("Inhaltsverzeichnis").And.Contain("Abbildung 1: Schéma");
    }

    [Fact]
    public void An_auto_document_takes_the_language_its_text_reads_as()
    {
        var export = new ExportDocument();
        var block = new Block { Type = "paragraph", Content = JsonDocument.Parse("""{"text":"هذه وثيقة مكتوبة باللغة العربية، وفيها جمل كثيرة تكفي لمعرفة لغتها."}""") };
        DocumentExportService.ApplyLanguage(export, new Document { Title = "T", Language = "auto", Blocks = [block] });
        export.Language.Should().Be("ar");
        export.Rtl.Should().BeTrue();
        export.Names.Abstract.Should().Be("ملخص");
    }

    // ── launch-bot #157: footnotes in prose ───────────────────────────────

    [Fact]
    public void A_footnote_in_prose_is_a_footnote_span_not_text_in_brackets()
    {
        var spans = DocumentExportService.ParseInlineFormatting(@"une file de priorité\footnote{Une file à tas \textbf{binaire} suffit.}. Sa complexité", null);

        string.Concat(spans.Select(s => s.Text)).Should().Be("une file de priorité. Sa complexité");
        var note = spans.Single(s => s.Footnote is not null).Footnote!;
        string.Concat(note.Select(s => s.Text)).Should().Be("Une file à tas binaire suffit.");
        note.Should().Contain(s => s.Bold && s.Text == "binaire");
    }

    [Fact]
    public void A_plain_field_keeps_the_footnote_in_brackets() =>
        ProseLatex.PlainForWord(@"Results\footnote{Preliminary.}", null).Should().Be("Results (Preliminary.)");

    [Fact]
    public async Task A_footnote_in_prose_is_a_real_word_footnote()
    {
        var note = new ExportBlock { Type = "footnote", Content = new ExportBlockContent { Text = "A block footnote." } };
        var doc = Doc("fr", Paragraph(@"une file de priorité\footnote{Une file à tas binaire suffit.}. Sa complexité"), note,
            Paragraph(@"Second\footnote{Deuxième note.} paragraphe."));

        var bytes = await new DocxExportService().ExportAsync(doc);
        var body = Part(bytes, "word/document.xml").Descendants(W + "body").Single();
        var footnotes = Part(bytes, "word/footnotes.xml");

        Text(body).Should().NotContain("Une file à tas").And.NotContain("(").And.Contain("une file de priorité. Sa complexité");
        var ids = body.Descendants(W + "footnoteReference").Select(r => r.Attribute(W + "id")!.Value).ToList();
        ids.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        foreach (var (id, text) in ids.Zip(new[] { "Une file à tas binaire suffit.", "A block footnote.", "Deuxième note." }))
            Text(footnotes.Descendants(W + "footnote").Single(f => f.Attribute(W + "id")!.Value == id)).Should().Contain(text);
        SchemaErrors(bytes).Should().BeEmpty();
    }
}
