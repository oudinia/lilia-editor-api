using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Lilia.Import.Services;

/// <summary>
/// Service for exporting documents to DOCX format.
/// </summary>
public class DocxExportService : IDocxExportService
{
    private readonly ILatexToOmmlConverter? _latexToOmmlConverter;
    private readonly IEquationImageRenderer? _equationImageRenderer;
    private readonly IImportTelemetrySink _telemetry;

    public DocxExportService()
    {
        _latexToOmmlConverter = null;
        _equationImageRenderer = null;
        _telemetry = new NoopImportTelemetrySink();
    }

    public DocxExportService(ILatexToOmmlConverter latexToOmmlConverter,
        IEquationImageRenderer? equationImageRenderer = null,
        IImportTelemetrySink? telemetry = null)
    {
        _latexToOmmlConverter = latexToOmmlConverter;
        _equationImageRenderer = equationImageRenderer;
        _telemetry = telemetry ?? new NoopImportTelemetrySink();
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportAsync(ExportDocument document, ExportOptions? options = null)
    {
        options ??= ExportOptions.Default;

        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            // Add main document part
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Add styles
            AddStyles(mainPart, document);

            // Add numbering for lists
            AddNumberingDefinitions(mainPart);

            NumberCaptions(document.Blocks);

            // Convert blocks to DOCX elements
            var sawBibliography = false;
            foreach (var block in document.Blocks)
            {
                if (string.Equals(block.Type, "bibliography", StringComparison.OrdinalIgnoreCase))
                    sawBibliography = true;

                // A footnote is a Word footnote, its mark on the paragraph before it (MCP-102: it
                // printed as a free-standing "*text" paragraph).
                if (string.Equals(block.Type, "footnote", StringComparison.OrdinalIgnoreCase))
                {
                    AppendFootnote(block, mainPart, body);
                    continue;
                }

                var elements = await ConvertBlock(block, mainPart, options, document);
                foreach (var element in elements)
                {
                    body.AppendChild(element);
                }
            }

            // A document can carry references without carrying a block that says
            // where to put them. The LaTeX export emits \bibliography whenever
            // there is at least one entry, block or no block, and Word should not
            // be the format that quietly loses them.
            if (!sawBibliography
                && options.IncludeBibliography
                && document.Bibliography is { Count: > 0 })
            {
                foreach (var element in ConvertBibliography(options, document))
                {
                    body.AppendChild(element);
                }
            }

            // Add section properties (page setup)
            var sectionProps = new SectionProperties();
            var pageSize = new PageSize
            {
                Width = options.PageWidth,
                Height = options.PageHeight
            };
            var pageMargin = new PageMargin
            {
                Top = options.MarginTop,
                Right = options.MarginRight,
                Bottom = options.MarginBottom,
                Left = options.MarginLeft
            };
            sectionProps.Append(pageSize);
            sectionProps.Append(pageMargin);

            // Multi-column layout — honours ExportDocument.Columns and
            // BalancedColumns (EqualWidth → whether Word auto-distributes width).
            if (document.Columns >= 2)
            {
                // Equal columns with a gap. EqualWidth followed BalancedColumns, so an unbalanced
                // two-column paper asked for unequal columns and defined none (MCP-105).
                sectionProps.Append(new Columns
                {
                    ColumnCount = (short)Math.Min(document.Columns, 3),
                    EqualWidth = true,
                    Space = "567",
                    Separator = false,
                });
            }

            AddHeaderFooter(mainPart, sectionProps, document);
            body.AppendChild(sectionProps);
            SchemaOrder(mainPart);

            // Set document properties
            if (!string.IsNullOrEmpty(document.Title))
            {
                wordDoc.PackageProperties.Title = document.Title;
            }
            if (!string.IsNullOrEmpty(document.Author))
            {
                wordDoc.PackageProperties.Creator = document.Author;
            }

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private void AddStyles(MainDocumentPart mainPart, ExportDocument document)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        // Normal, Title, Quote and TOC Heading (MCP-105): every style here is BasedOn Normal, which
        // wasn't defined, so Word fell back to its own defaults; the document's font size goes here.
        var normal = new Style { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true };
        normal.Append(new StyleName { Val = "Normal" });
        normal.Append(new PrimaryStyle());
        normal.Append(new StyleParagraphProperties(new SpacingBetweenLines { After = "120", Line = "264", LineRule = LineSpacingRuleValues.Auto }));
        normal.Append(new StyleRunProperties(new FontSize { Val = (Math.Clamp(document.FontSize, 8, 20) * 2).ToString() }));
        styles.Append(normal);

        var title = new Style { Type = StyleValues.Paragraph, StyleId = "Title" };
        title.Append(new StyleName { Val = "Title" });
        title.Append(new BasedOn { Val = "Normal" });
        title.Append(new NextParagraphStyle { Val = "Normal" });
        title.Append(new PrimaryStyle());
        title.Append(new StyleParagraphProperties(new Justification { Val = JustificationValues.Center }, new SpacingBetweenLines { After = "120" }));
        title.Append(new StyleRunProperties(new Bold(), new FontSize { Val = "40" }));
        styles.Append(title);

        var quote = new Style { Type = StyleValues.Paragraph, StyleId = "Quote" };
        quote.Append(new StyleName { Val = "Quote" });
        quote.Append(new BasedOn { Val = "Normal" });
        quote.Append(new NextParagraphStyle { Val = "Normal" });
        quote.Append(new PrimaryStyle());
        quote.Append(new StyleRunProperties(new Italic()));
        styles.Append(quote);

        // The contents' own heading: looks like a heading, but outside the outline, so the table of
        // contents doesn't list itself (it was Heading1).
        var tocHeading = new Style { Type = StyleValues.Paragraph, StyleId = "TOCHeading" };
        tocHeading.Append(new StyleName { Val = "TOC Heading" });
        tocHeading.Append(new BasedOn { Val = "Normal" });
        tocHeading.Append(new NextParagraphStyle { Val = "Normal" });
        tocHeading.Append(new StyleParagraphProperties(new SpacingBetweenLines { Before = "240", After = "120" }));
        tocHeading.Append(new StyleRunProperties(new Bold(), new FontSize { Val = GetHeadingFontSize(1) }));
        styles.Append(tocHeading);

        // Add heading styles
        for (int level = 1; level <= 6; level++)
        {
            var headingStyle = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = $"Heading{level}",
                CustomStyle = false
            };
            headingStyle.Append(new StyleName { Val = $"heading {level}" });
            headingStyle.Append(new BasedOn { Val = "Normal" });
            headingStyle.Append(new NextParagraphStyle { Val = "Normal" });

            var pPr = new StyleParagraphProperties();
            pPr.Append(new OutlineLevel { Val = level - 1 });
            pPr.Append(new SpacingBetweenLines { Before = "240", After = "120" });
            headingStyle.Append(pPr);

            var rPr = new StyleRunProperties();
            rPr.Append(new Bold());
            rPr.Append(new FontSize { Val = GetHeadingFontSize(level) });
            headingStyle.Append(rPr);

            styles.Append(headingStyle);
        }

        // Word's built-in Caption style, so captions look like captions and
        // Word's "Insert Caption" / cross-reference tools recognise them.
        var captionStyle = new Style { Type = StyleValues.Paragraph, StyleId = "Caption" };
        captionStyle.Append(new StyleName { Val = "caption" });
        captionStyle.Append(new BasedOn { Val = "Normal" });
        captionStyle.Append(new StyleParagraphProperties(new SpacingBetweenLines { Before = "120", After = "120" }));
        captionStyle.Append(new StyleRunProperties(new Italic(), new FontSize { Val = "20" }));
        styles.Append(captionStyle);

        // Add code style
        var codeStyle = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Code",
            CustomStyle = true
        };
        codeStyle.Append(new StyleName { Val = "Code" });
        // w:pPr before w:rPr (schema order).
        var codePPr = new StyleParagraphProperties();
        codePPr.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "F0F0F0" });
        codeStyle.Append(codePPr);
        var codeRPr = new StyleRunProperties();
        codeRPr.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
        codeRPr.Append(new FontSize { Val = "20" }); // 10pt
        codeStyle.Append(codeRPr);
        styles.Append(codeStyle);

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private string GetHeadingFontSize(int level)
    {
        return level switch
        {
            1 => "48", // 24pt
            2 => "36", // 18pt
            3 => "28", // 14pt
            4 => "24", // 12pt
            5 => "22", // 11pt
            _ => "22"  // 11pt
        };
    }

    private void AddNumberingDefinitions(MainDocumentPart mainPart)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();

        // Nine levels each, as Word addresses them: a nested list used to fall back to level 0's
        // bullet or number (MCP-99). Numbered levels follow LaTeX's enumerate: 1. (a) i. A.
        var bulletAbstract = new AbstractNum { AbstractNumberId = 0 };
        string[] bullets = ["•", "◦", "▪"];
        for (var lvl = 0; lvl < 9; lvl++)
        {
            var level = new Level { LevelIndex = lvl };
            level.Append(new NumberingFormat { Val = NumberFormatValues.Bullet });
            level.Append(new LevelText { Val = bullets[lvl % bullets.Length] });
            level.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            level.Append(new PreviousParagraphProperties(new Indentation { Left = (720 * (lvl + 1)).ToString(), Hanging = "360" }));
            bulletAbstract.Append(level);
        }
        numbering.Append(bulletAbstract);

        var numberedAbstract = new AbstractNum { AbstractNumberId = 1 };
        (NumberFormatValues Format, string Text)[] numbers =
        [
            (NumberFormatValues.Decimal, "%1."), (NumberFormatValues.LowerLetter, "(%2)"),
            (NumberFormatValues.LowerRoman, "%3."), (NumberFormatValues.UpperLetter, "%4."),
        ];
        for (var lvl = 0; lvl < 9; lvl++)
        {
            var (format, pattern) = numbers[lvl % numbers.Length];
            var level = new Level { LevelIndex = lvl };
            level.Append(new StartNumberingValue { Val = 1 });
            level.Append(new NumberingFormat { Val = format });
            level.Append(new LevelText { Val = pattern.Replace("%" + (lvl % numbers.Length + 1), "%" + (lvl + 1)) });
            level.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            level.Append(new PreviousParagraphProperties(new Indentation { Left = (720 * (lvl + 1)).ToString(), Hanging = "360" }));
            numberedAbstract.Append(level);
        }
        numbering.Append(numberedAbstract);

        // Numbering instances
        numbering.Append(new NumberingInstance { NumberID = 1, AbstractNumId = new AbstractNumId { Val = 0 } });
        numbering.Append(new NumberingInstance { NumberID = 2, AbstractNumId = new AbstractNumId { Val = 1 } });

        numberingPart.Numbering = numbering;
        numberingPart.Numbering.Save();
    }

    private Task<IEnumerable<OpenXmlElement>> ConvertBlock(
        ExportBlock block, MainDocumentPart mainPart, ExportOptions options, ExportDocument document)
    {
        return block.Type.ToLowerInvariant() switch
        {
            "equation" => ConvertEquation(block, mainPart),
            _ => Task.FromResult(ConvertBlockSync(block, mainPart, options, document))
        };
    }

    private IEnumerable<OpenXmlElement> ConvertBlockSync(
        ExportBlock block, MainDocumentPart mainPart, ExportOptions options, ExportDocument document)
    {
        return block.Type.ToLowerInvariant() switch
        {
            "paragraph" => ConvertParagraph(block),
            "heading" => ConvertHeading(block),
            "code" => ConvertCode(block),
            "list" => ConvertList(block, mainPart),
            "table" => ConvertTable(block),
            "figure" => ConvertFigure(block, mainPart),
            "blockquote" => ConvertBlockquote(block),
            "theorem" => ConvertTheorem(block),
            "title" => ConvertTitle(block),
            "matter" => ConvertMatter(block),
            "abstract" => ConvertAbstract(block),
            "bibliography" => ConvertBibliography(options, document),
            "pagebreak" => ConvertPageBreak(),
            "tableofcontents" => ConvertTableOfContents(),
            "algorithm" => ConvertAlgorithm(block),
            "callout" => ConvertCallout(block),
            "footnote" => ConvertFootnote(block, mainPart),
            _ => ConvertUnknownBlock(block),
        };
    }

    /// <summary>
    /// Default-case fallback in the export switch — emit telemetry so
    /// the dev team sees the silent fallback, then emit the content as
    /// a paragraph (graceful degradation, doesn't lose user content).
    /// Tier 3 of the export defence-in-depth strategy.
    /// </summary>
    private IEnumerable<OpenXmlElement> ConvertUnknownBlock(ExportBlock block)
    {
        _telemetry.Record(new ImportTelemetryRecord
        {
            EventKind = "silent_fallback",
            Severity = "warn",
            SourceFormat = "docx",
            TokenOrEnv = block.Type,
            BlockKindEmitted = "paragraph",
            SampleText = $"block.type={block.Type}",
        });
        return ConvertParagraph(block);
    }

    private IEnumerable<OpenXmlElement> ConvertParagraph(ExportBlock block)
    {
        var para = new Paragraph();
        var content = block.Content;

        if (content.RichText != null && content.RichText.Count > 0)
        {
            foreach (var span in content.RichText)
            {
                para.AppendChild(CreateSpanElement(span));
            }
        }
        else if (!string.IsNullOrEmpty(content.Text))
        {
            var run = new Run(new Text(content.Text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);
        }

        return [para];
    }

    private IEnumerable<OpenXmlElement> ConvertHeading(ExportBlock block)
    {
        var content = block.Content;
        var level = content.Level ?? 1;
        level = Math.Max(1, Math.Min(6, level));

        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphStyleId { Val = $"Heading{level}" });
        para.Append(pPr);

        if (!string.IsNullOrEmpty(content.Text))
        {
            var run = new Run(new Text(content.Text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);
        }

        return [para];
    }

    private const string MathNs = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// First value that is neither null nor empty, or "". Plain `??` would stop
    /// at an empty string, so a half-migrated block carrying `source: ""`
    /// alongside a real `latex` would export as a blank equation.
    /// </summary>
    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

    private async Task<IEnumerable<OpenXmlElement>> ConvertEquation(ExportBlock block, MainDocumentPart mainPart)
    {
        var content = block.Content;
        // `source` is the current name, `latex` the legacy one; both are read so
        // documents stored either side of the rename export identically. Mirrors
        // Lilia.Core.Blocks.EquationContent, which does the same for the JSON
        // renderers — this path deserialises into a typed DTO instead.
        var latex = Coalesce(content.Source, content.Latex, content.Text);
        var displayMode = content.DisplayMode ?? true;

        // Path 1: OMML (native Word math — editable, accessible)
        if (_latexToOmmlConverter != null && !string.IsNullOrWhiteSpace(latex))
        {
            var (omml, success, _) = _latexToOmmlConverter.Convert(latex);
            if (success && !string.IsNullOrEmpty(omml))
            {
                try
                {
                    var para = new Paragraph();
                    if (displayMode && !string.IsNullOrEmpty(content.EquationNumber))
                    {
                        // Centred, its number at the right margin: a centre and a right tab stop.
                        para.InnerXml = $"<w:pPr xmlns:w=\"{WordNs}\"><w:tabs><w:tab w:val=\"center\" w:pos=\"4513\"/><w:tab w:val=\"right\" w:pos=\"9026\"/></w:tabs></w:pPr>"
                            + $"<w:r xmlns:w=\"{WordNs}\"><w:tab/></w:r>" + omml
                            + $"<w:r xmlns:w=\"{WordNs}\"><w:tab/><w:t>({System.Security.SecurityElement.Escape(content.EquationNumber)})</w:t></w:r>";
                    }
                    else if (displayMode)
                    {
                        var pPrXml = $"<w:pPr xmlns:w=\"{WordNs}\"><w:jc w:val=\"center\"/></w:pPr>";
                        para.InnerXml = pPrXml + $"<m:oMathPara xmlns:m=\"{MathNs}\">{omml}</m:oMathPara>";
                    }
                    else
                    {
                        para.InnerXml = omml;
                    }
                    return [para];
                }
                catch { /* fall through */ }
            }
        }

        // Path 2: PNG image via pdflatex (for complex expressions OMML can't handle)
        if (_equationImageRenderer != null && !string.IsNullOrWhiteSpace(latex))
        {
            try
            {
                var png = await _equationImageRenderer.RenderToPngAsync(latex, displayMode);
                if (png != null && png.Length > 0)
                {
                    var imgPara = EmbedEquationPng(png, mainPart, displayMode, latex);
                    if (imgPara != null) return [imgPara];
                }
            }
            catch { /* fall through */ }
        }

        // Path 3: plain italic text (last resort — at least the LaTeX is readable)
        var fallback = new Paragraph();
        if (displayMode)
        {
            var pPr = new ParagraphProperties();
            pPr.Append(new Justification { Val = JustificationValues.Center });
            fallback.Append(pPr);
        }
        var run = new Run();
        var rPr = new RunProperties();
        rPr.Append(new Italic());
        run.Append(rPr);
        run.Append(new Text(displayMode ? $"[{latex}]" : $"${latex}$") { Space = SpaceProcessingModeValues.Preserve });
        fallback.AppendChild(run);
        return [fallback];
    }

    private Paragraph? EmbedEquationPng(byte[] png, MainDocumentPart mainPart, bool displayMode, string altText)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(png))
            imagePart.FeedData(stream);

        var relationshipId = mainPart.GetIdOfPart(imagePart);

        // Estimate size: 150 DPI, scale to fit within 4 inches wide
        var widthEmu  = Math.Min((long)(4 * 914400), (long)(png.Length / 10 * 914400 / 150));
        var heightEmu = widthEmu / 3; // rough 3:1 aspect for inline math

        var drawing = CreateImageDrawing(relationshipId, Math.Max(widthEmu, 457200), Math.Max(heightEmu, 152400), altText);
        var para = new Paragraph(new Run(drawing));
        if (displayMode)
        {
            var pPr = new ParagraphProperties();
            pPr.Append(new Justification { Val = JustificationValues.Center });
            para.PrependChild(pPr);
        }
        return para;
    }

    private IEnumerable<OpenXmlElement> ConvertCode(ExportBlock block)
    {
        var content = block.Content;
        var code = content.Code ?? content.Text ?? "";
        var lines = code.Split('\n');
        var elements = new List<OpenXmlElement>();
        // "Listing 2: …" above the code, as the LaTeX export prints it; the .docx had no caption.
        if (string.Equals(block.Type, "code", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(content.Caption))
            elements.Add(CaptionParagraph("Listing", content.CaptionNumber, content.Caption!, center: false, content.CaptionRichText));

        foreach (var line in lines)
        {
            var para = new Paragraph();
            var pPr = new ParagraphProperties();
            pPr.Append(new ParagraphStyleId { Val = "Code" });
            para.Append(pPr);

            var run = new Run();
            var runProps = new RunProperties();
            runProps.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            run.Append(runProps);
            run.Append(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);

            elements.Add(para);
        }

        return elements;
    }

    private IEnumerable<OpenXmlElement> ConvertList(ExportBlock block, MainDocumentPart mainPart)
    {
        var elements = new List<OpenXmlElement>();
        var content = block.Content;
        var isOrdered = content.ListType?.ToLowerInvariant() == "ordered";
        var numId = isOrdered ? NewNumberedList(mainPart) : 1;

        // A description list: each term in bold, then its description, the following lines indented
        // (as LaTeX's description environment prints it). Its descriptions were dropped (MCP-95).
        if (content.ListType?.ToLowerInvariant() == "description" && content.Items != null)
        {
            AppendDescriptionItems(content.Items, 0, elements);
            return elements;
        }

        // Walks Children too. It used to iterate Items only, so a nested list
        // exported its top level and silently dropped everything under it --
        // the sub-points of an argument, gone, with no error anywhere.
        if (content.Items != null)
        {
            AppendListItems(content.Items, numId, 0, elements);
        }

        return elements;
    }

    /// <summary>
    /// One list item and everything nested beneath it.
    ///
    /// <para><paramref name="depth"/> is the fallback indent level, used when an
    /// item does not carry its own: a child of a level-0 item sits at level 1
    /// whether or not anyone said so. Word's numbering definition addresses
    /// nine levels, so deeper nesting is clamped rather than dropped.</para>
    /// </summary>
    private void AppendListItems(
        List<ExportListItem> items, int numId, int depth, List<OpenXmlElement> into)
    {
        foreach (var item in items)
        {
            var level = Math.Clamp(item.Level > 0 ? item.Level : depth, 0, 8);

            var para = new Paragraph();
            var pPr = new ParagraphProperties();
            var numPr = new NumberingProperties();
            numPr.Append(new NumberingLevelReference { Val = level });
            numPr.Append(new NumberingId { Val = numId });
            pPr.Append(numPr);
            para.Append(pPr);

            if (item.RichText is { Count: > 0 })
            {
                foreach (var span in item.RichText) para.AppendChild(CreateSpanElement(span));
            }
            else
            {
                para.AppendChild(new Run(
                    new Text(item.Text) { Space = SpaceProcessingModeValues.Preserve }));
            }

            into.Add(para);

            if (item.Children is { Count: > 0 })
            {
                AppendListItems(item.Children, numId, level + 1, into);
            }
        }
    }

    /// <summary>
    /// Each numbered list counts from 1, as each LaTeX enumerate does: they all shared one Word
    /// numbering, so a second list went on from the first.
    /// </summary>
    private static int NewNumberedList(MainDocumentPart mainPart)
    {
        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
        if (numbering is null) return 2;
        var id = numbering.Elements<NumberingInstance>().Max(n => (int)(n.NumberID?.Value ?? 0)) + 1;
        var instance = new NumberingInstance { NumberID = id, AbstractNumId = new AbstractNumId { Val = 1 } };
        for (var lvl = 0; lvl < 9; lvl++)
            instance.Append(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = lvl });
        numbering.Append(instance);
        return id;
    }

    private void AppendDescriptionItems(List<ExportListItem> items, int depth, List<OpenXmlElement> into)
    {
        foreach (var item in items)
        {
            var indent = 360 * (depth + 1);
            var para = new Paragraph(new ParagraphProperties(
                new Indentation { Left = indent.ToString(), Hanging = "360" }));
            var term = item.RichText is { Count: > 0 } ? item.RichText : [new ExportRichTextSpan { Text = item.Text }];
            foreach (var span in term)
            {
                if (string.IsNullOrEmpty(span.Equation)) span.Bold = true;
                para.AppendChild(CreateSpanElement(span));
            }
            if (item.Description is { Count: > 0 } description)
            {
                para.AppendChild(new Run(new Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
                foreach (var span in description) para.AppendChild(CreateSpanElement(span));
            }
            into.Add(para);
            if (item.Children is { Count: > 0 }) AppendDescriptionItems(item.Children, depth + 1, into);
        }
    }

    /// <summary>
    /// Word's fixed highlight palette. An unrecognised colour returns null and
    /// the run is emitted unhighlighted -- losing a highlight is a cosmetic
    /// loss, and it beats losing the document.
    /// </summary>
    private static HighlightColorValues? ParseHighlight(string name) =>
        name.Trim().ToLowerInvariant() switch
        {
            "yellow" => HighlightColorValues.Yellow,
            "green" => HighlightColorValues.Green,
            "cyan" => HighlightColorValues.Cyan,
            "magenta" => HighlightColorValues.Magenta,
            "blue" => HighlightColorValues.Blue,
            "red" => HighlightColorValues.Red,
            "darkblue" => HighlightColorValues.DarkBlue,
            "darkcyan" => HighlightColorValues.DarkCyan,
            "darkgreen" => HighlightColorValues.DarkGreen,
            "darkmagenta" => HighlightColorValues.DarkMagenta,
            "darkred" => HighlightColorValues.DarkRed,
            "darkyellow" => HighlightColorValues.DarkYellow,
            "darkgray" or "darkgrey" => HighlightColorValues.DarkGray,
            "lightgray" or "lightgrey" => HighlightColorValues.LightGray,
            "black" => HighlightColorValues.Black,
            "white" => HighlightColorValues.White,
            "none" => HighlightColorValues.None,
            _ => null,
        };

    /// <summary>
    /// Number captioned tables and figures in document order, as LaTeX's
    /// counters do — only a block with a caption takes a number.
    /// </summary>
    private static void NumberCaptions(IEnumerable<ExportBlock> blocks)
    {
        int tables = 0, figures = 0, listings = 0, algorithms = 0;
        foreach (var b in blocks)
        {
            if (b.Content is null || string.IsNullOrWhiteSpace(b.Content.Caption)) continue;
            if (string.Equals(b.Type, "table", StringComparison.OrdinalIgnoreCase)) b.Content.CaptionNumber = ++tables;
            else if (string.Equals(b.Type, "figure", StringComparison.OrdinalIgnoreCase)) b.Content.CaptionNumber = ++figures;
            // Listing and algorithm captions, numbered as LaTeX numbers them (MCP-105: they were gone).
            else if (string.Equals(b.Type, "code", StringComparison.OrdinalIgnoreCase)) b.Content.CaptionNumber = ++listings;
            else if (string.Equals(b.Type, "algorithm", StringComparison.OrdinalIgnoreCase)) b.Content.CaptionNumber = ++algorithms;
        }
    }

    /// <summary>
    /// A Word caption: the built-in Caption style, "Table " + a SEQ field + ": "
    /// + the text. The field is what Word numbers (and cross-references point
    /// at — Olivia, 23 Sep: use Word's own fields, so Word numbers them); the
    /// run inside it is the number shown before Word updates fields.
    /// </summary>
    private Paragraph CaptionParagraph(string label, int? number, string text, bool center, List<ExportRichTextSpan>? runs = null)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties(new ParagraphStyleId { Val = "Caption" });
        if (center) pPr.Append(new Justification { Val = JustificationValues.Center });
        para.Append(pPr);
        if (number is int n)
        {
            para.Append(new Run(new Text($"{label} ") { Space = SpaceProcessingModeValues.Preserve }));
            para.Append(new SimpleField(new Run(new Text(n.ToString()))) { Instruction = $" SEQ {label} \\* ARABIC " });
            para.Append(new Run(new Text(": ") { Space = SpaceProcessingModeValues.Preserve }));
        }
        if (runs is { Count: > 0 })
            foreach (var span in runs) para.AppendChild(CreateSpanElement(span));
        else
            para.Append(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    private IEnumerable<OpenXmlElement> ConvertTable(ExportBlock block)
    {
        // The caption goes above the table, as the LaTeX export places it.
        if (!string.IsNullOrWhiteSpace(block.Content?.Caption))
            yield return CaptionParagraph("Table", block.Content.CaptionNumber, block.Content.Caption!, center: true, block.Content.CaptionRichText);
        foreach (var element in ConvertTableBody(block)) yield return element;
    }

    private IEnumerable<OpenXmlElement> ConvertTableBody(ExportBlock block)
    {
        var table = new Table();
        var content = block.Content;

        // Table properties
        var tblPr = new TableProperties();
        tblPr.Append(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 },
            new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 },
            new RightBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
        ));
        tblPr.Append(new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct });
        table.Append(tblPr);
        // The grid the spans count against (w:tblGrid is required, and gridSpan needs it).
        var gridColumns = content.Rows is { Count: > 0 } ? content.Rows.Max(r => r.Sum(c => Math.Max(1, c.ColSpan))) : 0;
        if (gridColumns > 0)
            table.Append(new TableGrid(Enumerable.Range(0, gridColumns).Select(_ => new GridColumn { Width = (9000 / gridColumns).ToString() })));

        if (content.Rows != null)
        {
            // Header rows: the column headings, and the group headers above them (MCP-93).
            var headerRows = content.HeaderRows ?? ((content.HasHeader ?? false) ? 1 : 0);
            var rowIndex = 0;
            foreach (var rowData in content.Rows)
            {
                var row = new TableRow();
                var isFirstRow = rowIndex++ < headerRows;

                // Header rows repeat on every page.
                if (isFirstRow)
                {
                    var trPr = new TableRowProperties();
                    trPr.Append(new TableHeader());
                    row.Append(trPr);
                }

                foreach (var cellData in rowData)
                {
                    var cell = new TableCell();

                    // Cell properties
                    var tcPr = new TableCellProperties();
                    if (cellData.ColSpan > 1)
                    {
                        tcPr.Append(new GridSpan { Val = cellData.ColSpan });
                    }
                    // A cell spanning rows starts a vertical merge; the places under it continue it.
                    if (cellData.RowSpan > 1)
                        tcPr.Append(new VerticalMerge { Val = MergedCellValues.Restart });
                    else if (cellData.MergedAbove)
                        tcPr.Append(new VerticalMerge());
                    cell.Append(tcPr);
                    if (cellData.MergedAbove)
                    {
                        cell.Append(new Paragraph());
                        row.Append(cell);
                        continue;
                    }

                    // Cell content
                    var para = new Paragraph();
                    if (isFirstRow && cellData.RichText is { Count: > 0 })
                    {
                        // Bold, with its maths as equations.
                        foreach (var span in cellData.RichText)
                        {
                            if (string.IsNullOrEmpty(span.Equation)) span.Bold = true;
                            para.AppendChild(CreateSpanElement(span));
                        }
                    }
                    else if (isFirstRow)
                    {
                        var run = new Run();
                        run.Append(new RunProperties(new Bold()));
                        run.Append(new Text(cellData.Text) { Space = SpaceProcessingModeValues.Preserve });
                        para.Append(run);
                    }
                    else if (cellData.RichText is { Count: > 0 })
                    {
                        // The cell's runs: bold, italic, its maths as Word equations. Only the plain
                        // text was written, so \textbf{…} and $…$ showed as typed (13 Oct sweep).
                        foreach (var span in cellData.RichText) para.AppendChild(CreateSpanElement(span));
                    }
                    else
                    {
                        para.Append(new Run(new Text(cellData.Text) { Space = SpaceProcessingModeValues.Preserve }));
                    }
                    cell.Append(para);

                    row.Append(cell);
                }

                table.Append(row);
            }
        }

        if (content.TableNotes is not { Count: > 0 } notes) return [table];

        // The table's notes under it, each after its mark, as threeparttable prints them.
        var elements = new List<OpenXmlElement> { table };
        foreach (var (mark, text) in notes)
        {
            var note = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }));
            note.Append(new Run(new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }, new FontSize { Val = "18" }),
                new Text(mark) { Space = SpaceProcessingModeValues.Preserve }));
            note.Append(new Run(new RunProperties(new FontSize { Val = "18" }), new Text(" " + text) { Space = SpaceProcessingModeValues.Preserve }));
            elements.Add(note);
        }
        return elements;
    }

    private IEnumerable<OpenXmlElement> ConvertFigure(ExportBlock block, MainDocumentPart mainPart)
    {
        var elements = new List<OpenXmlElement>();
        var content = block.Content;

        // If there's image data, add the image
        if (content.Image != null && !string.IsNullOrEmpty(content.Image.Data))
        {
            try
            {
                var imageBytes = Convert.FromBase64String(content.Image.Data);
                var imagePartType = content.Image.MimeType?.ToLowerInvariant() switch
                {
                    "image/png" => ImagePartType.Png,
                    "image/gif" => ImagePartType.Gif,
                    "image/bmp" => ImagePartType.Bmp,
                    "image/tiff" => ImagePartType.Tiff,
                    _ => ImagePartType.Jpeg
                };
                var imagePart = mainPart.AddImagePart(imagePartType);
                using (var stream = new MemoryStream(imageBytes))
                {
                    imagePart.FeedData(stream);
                }

                var relationshipId = mainPart.GetIdOfPart(imagePart);
                var widthEmu = (long)((content.Image.Width ?? 400) * 914400 / 96);
                var heightEmu = (long)((content.Image.Height ?? 300) * 914400 / 96);

                var drawing = CreateImageDrawing(relationshipId, widthEmu, heightEmu, content.Image.AltText ?? "Image");
                var para = new Paragraph(new Run(drawing));

                // Center the image
                var pPr = new ParagraphProperties();
                pPr.Append(new Justification { Val = JustificationValues.Center });
                para.PrependChild(pPr);

                elements.Add(para);
            }
            catch
            {
                // If image fails, add placeholder text
                var para = new Paragraph(new Run(new Text("[Image]")));
                elements.Add(para);
            }
        }
        else
        {
            // No image to place (a drawing that did not render, a link Lilia cannot read): a framed
            // placeholder, so the figure isn't a caption floating alone (MCP-104).
            var alt = content.Image?.AltText;
            elements.Add(new Paragraph(
                new ParagraphProperties(
                    new ParagraphBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4, Color = "999999" },
                        new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "999999" },
                        new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "999999" },
                        new RightBorder { Val = BorderValues.Single, Size = 4, Color = "999999" }),
                    new SpacingBetweenLines { Before = "240", After = "240" },
                    new Justification { Val = JustificationValues.Center }),
                new Run(new RunProperties(new Italic(), new Color { Val = "666666" }),
                    new Text(string.IsNullOrWhiteSpace(alt) ? "[Figure: see the PDF export]" : $"[Figure: {alt}]") { Space = SpaceProcessingModeValues.Preserve })));
        }

        // Add caption if present — "Figure N: …", as the PDF prints it.
        if (!string.IsNullOrEmpty(content.Caption))
            elements.Add(CaptionParagraph("Figure", content.CaptionNumber, content.Caption, center: true, content.CaptionRichText));

        if (elements.Count == 0)
        {
            elements.Add(new Paragraph(new Run(new Text("[Figure]"))));
        }

        return elements;
    }

    private Drawing CreateImageDrawing(string relationshipId, long widthEmu, long heightEmu, string altText)
    {
        var element = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = 1U, Name = altText, Description = altText },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = altText },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        return element;
    }

    private IEnumerable<OpenXmlElement> ConvertBlockquote(ExportBlock block)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphStyleId { Val = "Quote" });
        pPr.Append(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 24, Color = "CCCCCC" }
        ));
        pPr.Append(new Indentation { Left = "720" });
        para.Append(pPr);

        // Its runs, italic, each line break a Word line break: a verse is its lines, and a newline
        // inside w:t showed as one run-on line (MCP-97).
        var spans = block.Content.RichText is { Count: > 0 } rich
            ? rich
            : [new ExportRichTextSpan { Text = block.Content.Text ?? "" }];
        foreach (var span in spans)
        {
            if (!string.IsNullOrEmpty(span.Equation) || !span.Text.Contains('\n'))
            {
                if (string.IsNullOrEmpty(span.Equation)) span.Italic = true;
                para.AppendChild(CreateSpanElement(span));
                continue;
            }
            var lines = span.Text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) para.AppendChild(new Run(new Break()));
                if (lines[i].Length == 0) continue;
                para.AppendChild(new Run(new RunProperties(new Italic()), new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve }));
            }
        }

        if (string.IsNullOrWhiteSpace(block.Content.Attribution)) return [para];
        var attribution = new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
            new Run(new Text("— " + block.Content.Attribution) { Space = SpaceProcessingModeValues.Preserve }));
        return [para, attribution];
    }

    /// <summary>
    /// A real Word footnote: its text in the footnotes part, its mark at the end of the paragraph
    /// (or table cell) before it. A footnote with nothing before it gets a paragraph for its mark.
    /// </summary>
    private void AppendFootnote(ExportBlock block, MainDocumentPart mainPart, Body body)
    {
        var part = mainPart.FootnotesPart ?? mainPart.AddNewPart<FootnotesPart>();
        if (part.Footnotes is null)
        {
            part.Footnotes = new Footnotes(
                new Footnote(new Paragraph(new Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
                new Footnote(new Paragraph(new Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        }
        var id = part.Footnotes.Elements<Footnote>().Select(f => (int)(f.Id?.Value ?? 0)).DefaultIfEmpty(0).Max() + 1;

        var text = new Paragraph(new Run(
            new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
            new FootnoteReferenceMark()));
        text.AppendChild(new Run(new Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
        if (block.Content.RichText is { Count: > 0 } spans)
            foreach (var span in spans) text.AppendChild(CreateSpanElement(span));
        else
            text.AppendChild(new Run(new Text(block.Content.Text ?? "") { Space = SpaceProcessingModeValues.Preserve }));
        part.Footnotes.AppendChild(new Footnote(text) { Id = id });

        var reference = new Run(
            new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
            new FootnoteReference { Id = id });
        var host = body.LastChild switch
        {
            Paragraph p => p,
            Table t => t.Descendants<Paragraph>().LastOrDefault(),
            _ => null,
        };
        if (host is null)
        {
            host = new Paragraph();
            body.AppendChild(host);
        }
        host.AppendChild(reference);
    }

    private IEnumerable<OpenXmlElement> ConvertTheorem(ExportBlock block)
    {
        var elements = new List<OpenXmlElement>();
        var content = block.Content;

        var theoremType = content.TheoremType ?? "theorem";
        var displayType = char.ToUpper(theoremType[0]) + theoremType[1..];
        var number = content.TheoremNumber;
        var title = content.Text; // title stored in Text
        var bodySpans = content.RichText;

        // Build header text: "Theorem 1 (Title)."
        var headerText = displayType;
        if (number.HasValue)
            headerText += $" {number.Value}";
        if (!string.IsNullOrEmpty(title))
            headerText += $" ({title})";
        headerText += ".";

        // Theorem paragraph with left border (w:pBdr before w:ind: Word's schema order)
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 18, Color = "4472C4" }
        ));
        pPr.Append(new Indentation { Left = "360" });
        pPr.Append(new SpacingBetweenLines { Before = "120", After = "120" });
        para.Append(pPr);

        // Bold header run
        var headerRun = new Run();
        var headerProps = new RunProperties();
        headerProps.Append(new Bold());
        headerRun.Append(headerProps);
        headerRun.Append(new Text(headerText + " ") { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(headerRun);

        // Body text in italic
        if (bodySpans != null && bodySpans.Count > 0)
        {
            foreach (var span in bodySpans)
            {
                // Maths is a Word equation, not italic LaTeX (13 Oct sweep: "g = \\SI{…}" in an abstract).
                if (!string.IsNullOrEmpty(span.Equation)) { para.AppendChild(CreateSpanElement(span)); continue; }
                var run = CreateRun(span);
                // Add italic to theorem body
                var existingProps = run.RunProperties;
                if (existingProps == null)
                {
                    existingProps = new RunProperties();
                    run.PrependChild(existingProps);
                }
                if (!existingProps.Elements<Italic>().Any())
                    existingProps.Append(new Italic());
                para.AppendChild(run);
            }
        }

        elements.Add(para);
        return elements;
    }

    /// <summary>
    /// Bibliography block — emit a "References" heading + one paragraph
    /// per resolved entry. Falls back to a placeholder line when entries
    /// aren't pre-resolved into the block content (the typical export
    /// path resolves these through BibliographyService before calling).
    /// Pre-fix this block hit the `_ =>` default and exported as a
    /// generic paragraph — caught by ExportHandlerCoverageTests.
    /// </summary>
    /// <summary>
    /// The References section.
    ///
    /// <para>This used to emit the heading and then the literal text
    /// "[Bibliography entries appended below]", on the stated assumption that
    /// "entries are appended at the document-conversion layer". No such layer
    /// existed. <see cref="ExportDocument.Bibliography"/> was populated from
    /// the database and then never read, so <b>every Word export silently
    /// dropped its references</b> — a 24-block paper with two cited works
    /// exported with no References section at all (2026-09-09).</para>
    ///
    /// <para>Entries are yielded one at a time rather than gathered into a
    /// list: a bibliography can run to hundreds of entries and there is no
    /// reason to hold them all while the caller is appending them anyway.</para>
    /// </summary>
    private IEnumerable<OpenXmlElement> ConvertBibliography(
        ExportOptions options, ExportDocument document)
    {
        if (!options.IncludeBibliography) yield break;

        yield return BibliographyHeading();

        var entries = document.Bibliography;
        if (entries == null || entries.Count == 0)
        {
            // An empty References section is a real state — the author added
            // the block but has not cited anything yet — and saying so beats
            // a bare heading that looks like a rendering failure.
            yield return ItalicParagraph("No references yet.");
            yield break;
        }

        // Numbered entries keep the order their numbers give (citations print them); others sort by text.
        var ordered = entries.All(e => e.Number is not null)
            ? entries.OrderBy(e => e.Number)
            : entries.OrderBy(e => FormatBibliographyEntry(e), StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in ordered)
        {
            yield return BibliographyEntryParagraph(entry);
        }
    }

    private static Paragraph BibliographyHeading()
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new SpacingBetweenLines { Before = "360", After = "180" });
        para.Append(pPr);
        var run = new Run();
        var runProps = new RunProperties();
        runProps.Append(new Bold());
        runProps.Append(new FontSize { Val = "32" }); // 16pt
        run.Append(runProps);
        run.Append(new Text("References") { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(run);
        return para;
    }

    private static Paragraph ItalicParagraph(string text)
    {
        var para = new Paragraph();
        var run = new Run();
        var runProps = new RunProperties();
        runProps.Append(new Italic());
        run.Append(runProps);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(run);
        return para;
    }

    /// <summary>A hanging-indent entry, the shape a reference list expects.</summary>
    private static Paragraph BibliographyEntryParagraph(ExportBibliographyEntry entry)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new SpacingBetweenLines { After = "120" });
        pPr.Append(new Indentation { Left = "720", Hanging = "720" });
        para.Append(pPr);
        var label = entry.Number is int n ? $"[{n}] " : "";
        para.AppendChild(new Run(
            new Text(label + FormatBibliographyEntry(entry)) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    /// <summary>
    /// One reference, rendered plainly: author, year, title, then whatever
    /// publication detail the entry carries.
    ///
    /// <para>A missing field is skipped rather than filled with a placeholder —
    /// an entry with only an author and a title should read like a short
    /// reference, not like a form with gaps. A mononym author ("Euclid") is
    /// left exactly as written; splitting it to initialise a given name that
    /// does not exist is what produced "Euclid, (c. 300 BCE)." on the web
    /// side.</para>
    /// </summary>
    internal static string FormatBibliographyEntry(ExportBibliographyEntry entry)
    {
        string Field(string name) =>
            entry.Fields.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v.Trim()
                : "";

        var parts = new List<string>(5);

        var author = Field("author");
        if (author.Length > 0) parts.Add(author);

        var year = Field("year");
        if (year.Length > 0) parts.Add($"({year})");

        var title = Field("title");
        if (title.Length > 0) parts.Add(title);

        // Whichever venue the entry type actually has.
        var venue = Field("journal");
        if (venue.Length == 0) venue = Field("booktitle");
        if (venue.Length == 0) venue = Field("publisher");
        if (venue.Length > 0) parts.Add(venue);

        var pages = Field("pages");
        if (pages.Length > 0) parts.Add($"pp. {pages}");

        // Nothing usable at all: the cite key is the only honest label left,
        // and it at least lets the author find the entry they need to fix.
        if (parts.Count == 0) return entry.CiteKey;

        // A part that ends with its own full stop (an initial: "Young, L. C.") doesn't get a second.
        return string.Join(". ", parts.Select(p => p.TrimEnd('.'))) + ".";
    }

    // Word's schema order for paragraph and run properties (CT_PPrBase, CT_RPr). The writers append
    // them as they go, which Word tolerates and the schema doesn't (MCP-105: "unexpected child").
    private static readonly string[] ParagraphOrder =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers",
        "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE",
        "autoSpaceDN", "bidi", "adjustRightInd", "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents",
        "suppressOverlap", "jc", "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId", "cnfStyle",
        "rPr", "sectPr", "pPrChange",
    ];
    private static readonly string[] RunOrder =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss",
        "imprint", "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs",
        "highlight", "u", "effect", "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout",
        "specVanish", "oMath",
    ];

    private static void SchemaOrder(MainDocumentPart mainPart)
    {
        static void Sort(OpenXmlElement props, string[] order)
        {
            var children = props.ChildElements.ToList();
            var sorted = children.OrderBy(c => { var i = Array.IndexOf(order, c.LocalName); return i < 0 ? order.Length : i; }).ToList();
            if (children.SequenceEqual(sorted)) return;
            props.RemoveAllChildren();
            foreach (var c in sorted) props.AppendChild(c);
        }
        IEnumerable<OpenXmlElement> Roots()
        {
            if (mainPart.Document is { } d) yield return d;
            foreach (var h in mainPart.HeaderParts) if (h.Header is { } x) yield return x;
            foreach (var f in mainPart.FooterParts) if (f.Footer is { } x) yield return x;
            if (mainPart.FootnotesPart?.Footnotes is { } n) yield return n;
        }
        foreach (var root in Roots())
        {
            foreach (var p in root.Descendants<ParagraphProperties>().ToList()) Sort(p, ParagraphOrder);
            foreach (var r in root.Descendants<RunProperties>().ToList()) Sort(r, RunOrder);
        }
        if (mainPart.StyleDefinitionsPart?.Styles is { } styles)
        {
            foreach (var p in styles.Descendants<StyleParagraphProperties>().ToList()) Sort(p, ParagraphOrder);
            foreach (var r in styles.Descendants<StyleRunProperties>().ToList()) Sort(r, RunOrder);
        }
    }

    /// <summary>
    /// A front or back matter block: its title as an unnumbered heading, then its text; a dedication
    /// centred in italics without a heading, as the LaTeX export prints them.
    /// </summary>
    private IEnumerable<OpenXmlElement> ConvertMatter(ExportBlock block)
    {
        var content = block.Content;
        var dedication = content.Variant == "dedication";
        if (!dedication && !string.IsNullOrWhiteSpace(content.Text))
            yield return new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text(content.Text) { Space = SpaceProcessingModeValues.Preserve }));
        if (content.RichText is not { Count: > 0 } spans) yield break;
        var para = new Paragraph();
        if (dedication)
            para.Append(new ParagraphProperties(new Justification { Val = JustificationValues.Center }, new SpacingBetweenLines { Before = "480", After = "480" }));
        foreach (var span in spans)
        {
            if (dedication && string.IsNullOrEmpty(span.Equation)) span.Italic = true;
            para.AppendChild(CreateSpanElement(span));
        }
        yield return para;
    }

    /// <summary>
    /// The running header and footer: left, centre and right slots on one line (centre and right tab
    /// stops), as fancyhdr prints them. With no footer slot, the page number centred, unless page
    /// numbering is off. The .docx had neither (MCP-105).
    /// </summary>
    private static void AddHeaderFooter(MainDocumentPart mainPart, SectionProperties section, ExportDocument document)
    {
        static Paragraph Line(string? left, string? center, string? right, bool pageNumber, bool roman)
        {
            var para = new Paragraph(new ParagraphProperties(new Tabs(
                new TabStop { Val = TabStopValues.Center, Position = 4513 },
                new TabStop { Val = TabStopValues.Right, Position = 9026 })));
            void Add(string? text) { if (!string.IsNullOrEmpty(text)) para.Append(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })); }
            Add(left);
            para.Append(new Run(new TabChar()));
            if (pageNumber)
                para.Append(new SimpleField(new Run(new Text("1"))) { Instruction = roman ? " PAGE \\* roman " : " PAGE " });
            else
                Add(center);
            para.Append(new Run(new TabChar()));
            Add(right);
            return para;
        }

        var hasHeader = !string.IsNullOrWhiteSpace(document.HeaderLeft) || !string.IsNullOrWhiteSpace(document.HeaderCenter) || !string.IsNullOrWhiteSpace(document.HeaderRight);
        if (hasHeader)
        {
            var header = mainPart.AddNewPart<HeaderPart>();
            header.Header = new Header(Line(document.HeaderLeft, document.HeaderCenter, document.HeaderRight, false, false));
            // References come first in w:sectPr (schema order), before the page size.
            section.InsertAt(new HeaderReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(header) }, 0);
        }

        var hasFooter = !string.IsNullOrWhiteSpace(document.FooterLeft) || !string.IsNullOrWhiteSpace(document.FooterCenter) || !string.IsNullOrWhiteSpace(document.FooterRight);
        var numbering = (document.PageNumbering ?? "arabic").Trim().ToLowerInvariant();
        if (!hasFooter && numbering == "none") return;
        var footer = mainPart.AddNewPart<FooterPart>();
        footer.Footer = new Footer(hasFooter
            ? Line(document.FooterLeft, document.FooterCenter, document.FooterRight, false, false)
            : Line(null, null, null, true, numbering == "roman"));
        section.InsertAt(new FooterReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(footer) }, hasHeader ? 1 : 0);
    }

    /// <summary>
    /// The title block: title, then author, then date, centred.
    ///
    /// <para>There was no case for this at all — neither here nor in the
    /// mapper — so a paper exported to Word beginning at its abstract, with no
    /// title and its author absent from the file entirely.</para>
    ///
    /// <para><c>\today</c> is resolved to the current date, matching what
    /// LaTeX would typeset. Any other date is emitted exactly as written; an
    /// author writing "Spring 2026" means it.</para>
    /// </summary>
    private IEnumerable<OpenXmlElement> ConvertTitle(ExportBlock block)
    {
        var content = block.Content;

        var title = content.Text;
        if (!string.IsNullOrWhiteSpace(title))
        {
            var para = CentredParagraph(title, sizeHalfPoints: "40", bold: true, spaceAfter: "120");
            para.ParagraphProperties!.PrependChild(new ParagraphStyleId { Val = "Title" });
            yield return para;
        }

        if (!string.IsNullOrWhiteSpace(content.Author))
            yield return CentredParagraph(content.Author, sizeHalfPoints: "24", bold: false, spaceAfter: "60");

        var date = ResolveDate(content.DateText, content.Date);
        if (!string.IsNullOrWhiteSpace(date))
            yield return CentredParagraph(date, sizeHalfPoints: "22", bold: false, spaceAfter: "240");
    }

    /// <summary>\today means "when this was typeset"; anything else is verbatim.</summary>
    internal static string? ResolveDate(string? dateText, DateTime? date)
    {
        var written = dateText?.Trim();
        if (string.Equals(written, @"\today", StringComparison.OrdinalIgnoreCase))
            return DateTime.Now.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(written)) return written;
        return date?.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Paragraph CentredParagraph(
        string text, string sizeHalfPoints, bool bold, string spaceAfter)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new Justification { Val = JustificationValues.Center });
        pPr.Append(new SpacingBetweenLines { After = spaceAfter });
        para.Append(pPr);

        var run = new Run();
        var runProps = new RunProperties();
        if (bold) runProps.Append(new Bold());
        runProps.Append(new FontSize { Val = sizeHalfPoints });
        run.Append(runProps);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(run);
        return para;
    }

    private IEnumerable<OpenXmlElement> ConvertAbstract(ExportBlock block)
    {
        var elements = new List<OpenXmlElement>();
        var content = block.Content;

        // "Abstract" heading
        var headingPara = new Paragraph();
        var headingPPr = new ParagraphProperties();
        headingPPr.Append(new Justification { Val = JustificationValues.Center });
        headingPPr.Append(new SpacingBetweenLines { Before = "240", After = "120" });
        headingPara.Append(headingPPr);

        var headingRun = new Run();
        var headingRunProps = new RunProperties();
        headingRunProps.Append(new Bold());
        headingRunProps.Append(new FontSize { Val = "28" }); // 14pt
        headingRun.Append(headingRunProps);
        headingRun.Append(new Text("Abstract") { Space = SpaceProcessingModeValues.Preserve });
        headingPara.AppendChild(headingRun);
        elements.Add(headingPara);

        // Body paragraph in italic
        var bodyPara = new Paragraph();
        var bodyPPr = new ParagraphProperties();
        bodyPPr.Append(new Indentation { Left = "720", Right = "720" });
        bodyPara.Append(bodyPPr);

        if (content.RichText != null && content.RichText.Count > 0)
        {
            foreach (var span in content.RichText)
            {
                // Maths is a Word equation, not italic LaTeX (13 Oct sweep: "g = \\SI{…}" in an abstract).
                if (!string.IsNullOrEmpty(span.Equation)) { bodyPara.AppendChild(CreateSpanElement(span)); continue; }
                var run = CreateRun(span);
                var existingProps = run.RunProperties;
                if (existingProps == null)
                {
                    existingProps = new RunProperties();
                    run.PrependChild(existingProps);
                }
                if (!existingProps.Elements<Italic>().Any())
                    existingProps.Append(new Italic());
                bodyPara.AppendChild(run);
            }
        }
        else if (!string.IsNullOrEmpty(content.Text))
        {
            var run = new Run();
            run.Append(new RunProperties(new Italic()));
            run.Append(new Text(content.Text) { Space = SpaceProcessingModeValues.Preserve });
            bodyPara.AppendChild(run);
        }

        elements.Add(bodyPara);
        return elements;
    }

    private IEnumerable<OpenXmlElement> ConvertPageBreak()
    {
        var para = new Paragraph();
        var run = new Run();
        run.Append(new Break { Type = BreakValues.Page });
        para.AppendChild(run);
        return [para];
    }

    private IEnumerable<OpenXmlElement> ConvertTableOfContents()
    {
        var elements = new List<OpenXmlElement>();

        // TOC heading
        var headingPara = new Paragraph();
        var headingPPr = new ParagraphProperties();
        headingPPr.Append(new ParagraphStyleId { Val = "TOCHeading" });
        headingPara.Append(headingPPr);
        headingPara.AppendChild(new Run(new Text("Contents")));
        elements.Add(headingPara);

        // TOC field
        var tocPara = new Paragraph();
        var fldBegin = new Run(new FieldChar { FieldCharType = FieldCharValues.Begin });
        var fldCode = new Run(new FieldCode(" TOC \\o \"1-3\" \\h \\z ") { Space = SpaceProcessingModeValues.Preserve });
        var fldSeparate = new Run(new FieldChar { FieldCharType = FieldCharValues.Separate });
        var fldText = new Run(new Text("Right-click to update field.") { Space = SpaceProcessingModeValues.Preserve });
        var fldEnd = new Run(new FieldChar { FieldCharType = FieldCharValues.End });

        tocPara.Append(fldBegin);
        tocPara.Append(fldCode);
        tocPara.Append(fldSeparate);
        tocPara.Append(fldText);
        tocPara.Append(fldEnd);
        elements.Add(tocPara);

        return elements;
    }

    private IEnumerable<OpenXmlElement> ConvertAlgorithm(ExportBlock block)
    {
        var content = block.Content;

        // Add caption before the code block
        var elements = new List<OpenXmlElement>();
        if (!string.IsNullOrEmpty(content.Caption))
        {
            var captionPara = new Paragraph();
            var captionPPr = new ParagraphProperties();
            captionPPr.Append(new SpacingBetweenLines { Before = "120", After = "60" });
            captionPara.Append(captionPPr);
            if (content.CaptionNumber is int n)
                captionPara.AppendChild(new Run(new RunProperties(new Bold()), new Text($"Algorithm {n} ") { Space = SpaceProcessingModeValues.Preserve }));

            if (content.CaptionRichText is { Count: > 0 } runs)
            {
                foreach (var span in runs)
                {
                    if (string.IsNullOrEmpty(span.Equation)) span.Bold = true;
                    captionPara.AppendChild(CreateSpanElement(span));
                }
            }
            else
            {
                var captionRun = new Run();
                captionRun.Append(new RunProperties(new Bold()));
                captionRun.Append(new Text(content.Caption) { Space = SpaceProcessingModeValues.Preserve });
                captionPara.AppendChild(captionRun);
            }
            elements.Add(captionPara);
        }

        // Reuse code block rendering
        var codeBlock = new ExportBlock
        {
            Type = "code",
            Content = new ExportBlockContent
            {
                Code = content.Code ?? content.Text,
                Language = content.Language ?? "text"
            }
        };
        elements.AddRange(ConvertCode(codeBlock));
        return elements;
    }

    private IEnumerable<OpenXmlElement> ConvertCallout(ExportBlock block)
    {
        var content = block.Content;
        var title = content.Text; // title in Text
        var bodySpans = content.RichText;

        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 24, Color = "70AD47" }
        ));
        pPr.Append(new Indentation { Left = "720" });
        pPr.Append(new SpacingBetweenLines { Before = "120", After = "120" });
        pPr.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "F0FFF0" });
        para.Append(pPr);

        // Bold title
        if (!string.IsNullOrEmpty(title))
        {
            var titleRun = new Run();
            titleRun.Append(new RunProperties(new Bold()));
            titleRun.Append(new Text(title + " ") { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(titleRun);
        }

        // Body
        if (bodySpans != null && bodySpans.Count > 0)
        {
            foreach (var span in bodySpans)
            {
                para.AppendChild(CreateSpanElement(span));
            }
        }

        return [para];
    }

    private IEnumerable<OpenXmlElement> ConvertFootnote(ExportBlock block, MainDocumentPart mainPart)
    {
        // Footnotes in OpenXML require a FootnotesPart. For simplicity,
        // render as a superscript reference with the footnote text inline.
        var content = block.Content;
        var text = content.Text ?? "";

        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new Indentation { Left = "360" });
        para.Append(pPr);

        // Footnote marker
        var markerRun = new Run();
        markerRun.Append(new RunProperties(
            new VerticalTextAlignment { Val = VerticalPositionValues.Superscript },
            new FontSize { Val = "18" }
        ));
        markerRun.Append(new Text("*") { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(markerRun);

        // Footnote text
        if (content.RichText != null && content.RichText.Count > 0)
        {
            foreach (var span in content.RichText)
            {
                para.AppendChild(CreateSpanElement(span));
            }
        }
        else
        {
            var textRun = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(textRun);
        }

        return [para];
    }

    private Run CreateRun(ExportRichTextSpan span)
    {
        var run = new Run();
        var runProps = new RunProperties();

        if (span.Bold)
            runProps.Append(new Bold());
        if (span.Italic)
            runProps.Append(new Italic());
        if (span.Underline)
            runProps.Append(new Underline { Val = UnderlineValues.Single });
        if (span.Strikethrough)
            runProps.Append(new Strike());
        if (span.Superscript)
            runProps.Append(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript });
        if (span.Subscript)
            runProps.Append(new VerticalTextAlignment { Val = VerticalPositionValues.Subscript });

        if (!string.IsNullOrEmpty(span.Color))
        {
            var color = span.Color.TrimStart('#');
            runProps.Append(new Color { Val = color });
        }

        if (!string.IsNullOrEmpty(span.FontSize))
        {
            // Convert pt to half-points
            if (double.TryParse(span.FontSize.Replace("pt", ""), out var pts))
            {
                runProps.Append(new FontSize { Val = ((int)(pts * 2)).ToString() });
            }
        }

        if (!string.IsNullOrEmpty(span.FontFamily))
        {
            runProps.Append(new RunFonts { Ascii = span.FontFamily, HighAnsi = span.FontFamily });
        }

        if (!string.IsNullOrEmpty(span.Highlight))
        {
            // Enum.TryParse<HighlightColorValues> used to sit here. In
            // DocumentFormat.OpenXml 3.x HighlightColorValues is a readonly
            // struct, not an enum, so that call threw
            //   System.ArgumentException: Type provided must be an Enum
            // at runtime -- and because it threw rather than returned false,
            // ONE highlighted word aborted the export of the whole document.
            var highlight = ParseHighlight(span.Highlight);
            if (highlight is not null)
            {
                runProps.Append(new Highlight { Val = highlight });
            }
        }

        if (runProps.HasChildren)
        {
            run.Append(runProps);
        }

        run.Append(new Text(span.Text) { Space = SpaceProcessingModeValues.Preserve });

        return run;
    }

    /// <summary>
    /// Returns either a Run (text span) or an inline m:oMath element (equation span).
    /// Use this instead of CreateRun when spans may contain inline equations.
    /// </summary>
    private OpenXmlElement CreateSpanElement(ExportRichTextSpan span)
    {
        if (!string.IsNullOrEmpty(span.Equation) && _latexToOmmlConverter != null)
        {
            var (omml, success, _) = _latexToOmmlConverter.Convert(span.Equation);
            if (success && !string.IsNullOrEmpty(omml))
            {
                try
                {
                    // Build the element from its XML directly. The previous
                    // approach set InnerXml on a throwaway Paragraph and lifted
                    // FirstChild out of it — but the strongly-typed Paragraph
                    // does not materialise m:oMath as a child, so FirstChild was
                    // null every time and every inline equation fell through to
                    // the "$latex$" fallback below. A 24-block paper exported
                    // with its inline maths as literal "$a^2 + b^2 = c^2$"
                    // (2026-09-09), while display equations — which take the
                    // ConvertEquation path and never extract a child — came out
                    // as proper Word equations.
                    return new DocumentFormat.OpenXml.Math.OfficeMath(omml);
                }
                catch { /* fall through */ }
            }
            // Fallback: render as $latex$ text
            return new Run(new Text($"${span.Equation}$") { Space = SpaceProcessingModeValues.Preserve });
        }
        return CreateRun(span);
    }
}
