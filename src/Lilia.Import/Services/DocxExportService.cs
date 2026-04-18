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

    public DocxExportService()
    {
        _latexToOmmlConverter = null;
        _equationImageRenderer = null;
    }

    public DocxExportService(ILatexToOmmlConverter latexToOmmlConverter,
        IEquationImageRenderer? equationImageRenderer = null)
    {
        _latexToOmmlConverter = latexToOmmlConverter;
        _equationImageRenderer = equationImageRenderer;
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
            AddStyles(mainPart);

            // Add numbering for lists
            AddNumberingDefinitions(mainPart);

            // Convert blocks to DOCX elements
            foreach (var block in document.Blocks)
            {
                var elements = await ConvertBlock(block, mainPart, options);
                foreach (var element in elements)
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

            // Multi-column layout — honours ExportDocument.Columns.
            if (document.Columns >= 2)
            {
                sectionProps.Append(new Columns
                {
                    ColumnCount = (short)Math.Min(document.Columns, 3),
                    EqualWidth = true,
                    Separator = false,
                });
            }

            body.AppendChild(sectionProps);

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

    private void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

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

        // Add code style
        var codeStyle = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Code",
            CustomStyle = true
        };
        codeStyle.Append(new StyleName { Val = "Code" });
        var codeRPr = new StyleRunProperties();
        codeRPr.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
        codeRPr.Append(new FontSize { Val = "20" }); // 10pt
        codeStyle.Append(codeRPr);
        var codePPr = new StyleParagraphProperties();
        codePPr.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "F0F0F0" });
        codeStyle.Append(codePPr);
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

        // Bullet list abstract numbering
        var bulletAbstract = new AbstractNum { AbstractNumberId = 0 };
        var bulletLevel = new Level { LevelIndex = 0 };
        bulletLevel.Append(new NumberingFormat { Val = NumberFormatValues.Bullet });
        bulletLevel.Append(new LevelText { Val = "•" });
        bulletLevel.Append(new LevelJustification { Val = LevelJustificationValues.Left });
        var bulletPPr = new PreviousParagraphProperties();
        bulletPPr.Append(new Indentation { Left = "720", Hanging = "360" });
        bulletLevel.Append(bulletPPr);
        bulletAbstract.Append(bulletLevel);
        numbering.Append(bulletAbstract);

        // Numbered list abstract numbering
        var numberedAbstract = new AbstractNum { AbstractNumberId = 1 };
        var numberedLevel = new Level { LevelIndex = 0 };
        numberedLevel.Append(new NumberingFormat { Val = NumberFormatValues.Decimal });
        numberedLevel.Append(new LevelText { Val = "%1." });
        numberedLevel.Append(new StartNumberingValue { Val = 1 });
        numberedLevel.Append(new LevelJustification { Val = LevelJustificationValues.Left });
        var numberedPPr = new PreviousParagraphProperties();
        numberedPPr.Append(new Indentation { Left = "720", Hanging = "360" });
        numberedLevel.Append(numberedPPr);
        numberedAbstract.Append(numberedLevel);
        numbering.Append(numberedAbstract);

        // Numbering instances
        numbering.Append(new NumberingInstance { NumberID = 1, AbstractNumId = new AbstractNumId { Val = 0 } });
        numbering.Append(new NumberingInstance { NumberID = 2, AbstractNumId = new AbstractNumId { Val = 1 } });

        numberingPart.Numbering = numbering;
        numberingPart.Numbering.Save();
    }

    private Task<IEnumerable<OpenXmlElement>> ConvertBlock(ExportBlock block, MainDocumentPart mainPart, ExportOptions options)
    {
        return block.Type.ToLowerInvariant() switch
        {
            "equation" => ConvertEquation(block, mainPart),
            _ => Task.FromResult(ConvertBlockSync(block, mainPart, options))
        };
    }

    private IEnumerable<OpenXmlElement> ConvertBlockSync(ExportBlock block, MainDocumentPart mainPart, ExportOptions options)
    {
        return block.Type.ToLowerInvariant() switch
        {
            "paragraph" => ConvertParagraph(block),
            "heading" => ConvertHeading(block),
            "code" => ConvertCode(block),
            "list" => ConvertList(block),
            "table" => ConvertTable(block),
            "figure" => ConvertFigure(block, mainPart),
            "blockquote" => ConvertBlockquote(block),
            "theorem" => ConvertTheorem(block),
            "abstract" => ConvertAbstract(block),
            "pagebreak" => ConvertPageBreak(),
            "tableofcontents" => ConvertTableOfContents(),
            "algorithm" => ConvertAlgorithm(block),
            "callout" => ConvertCallout(block),
            "footnote" => ConvertFootnote(block, mainPart),
            _ => ConvertParagraph(block)
        };
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

    private async Task<IEnumerable<OpenXmlElement>> ConvertEquation(ExportBlock block, MainDocumentPart mainPart)
    {
        var content = block.Content;
        var latex = content.Latex ?? content.Text ?? "";
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
                    if (displayMode)
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

    private IEnumerable<OpenXmlElement> ConvertList(ExportBlock block)
    {
        var elements = new List<OpenXmlElement>();
        var content = block.Content;
        var isOrdered = content.ListType?.ToLowerInvariant() == "ordered";
        var numId = isOrdered ? 2 : 1;

        if (content.Items != null)
        {
            foreach (var item in content.Items)
            {
                var para = new Paragraph();
                var pPr = new ParagraphProperties();
                var numPr = new NumberingProperties();
                numPr.Append(new NumberingLevelReference { Val = item.Level });
                numPr.Append(new NumberingId { Val = numId });
                pPr.Append(numPr);
                para.Append(pPr);

                var run = new Run(new Text(item.Text) { Space = SpaceProcessingModeValues.Preserve });
                para.AppendChild(run);

                elements.Add(para);
            }
        }

        return elements;
    }

    private IEnumerable<OpenXmlElement> ConvertTable(ExportBlock block)
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

        if (content.Rows != null)
        {
            var isFirstRow = true;
            foreach (var rowData in content.Rows)
            {
                var row = new TableRow();

                // Mark first row as header if HasHeader is true
                if (isFirstRow && (content.HasHeader ?? false))
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
                    cell.Append(tcPr);

                    // Cell content
                    var para = new Paragraph();
                    if (isFirstRow && (content.HasHeader ?? false))
                    {
                        var run = new Run();
                        run.Append(new RunProperties(new Bold()));
                        run.Append(new Text(cellData.Text) { Space = SpaceProcessingModeValues.Preserve });
                        para.Append(run);
                    }
                    else
                    {
                        para.Append(new Run(new Text(cellData.Text) { Space = SpaceProcessingModeValues.Preserve }));
                    }
                    cell.Append(para);

                    row.Append(cell);
                }

                table.Append(row);
                isFirstRow = false;
            }
        }

        return [table];
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

        // Add caption if present
        if (!string.IsNullOrEmpty(content.Caption))
        {
            var captionPara = new Paragraph();
            var pPr = new ParagraphProperties();
            pPr.Append(new Justification { Val = JustificationValues.Center });
            captionPara.Append(pPr);

            var run = new Run();
            run.Append(new RunProperties(new Italic()));
            run.Append(new Text(content.Caption) { Space = SpaceProcessingModeValues.Preserve });
            captionPara.Append(run);

            elements.Add(captionPara);
        }

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
        pPr.Append(new Indentation { Left = "720" });
        pPr.Append(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 24, Color = "CCCCCC" }
        ));
        para.Append(pPr);

        var run = new Run();
        run.Append(new RunProperties(new Italic()));
        run.Append(new Text(block.Content.Text ?? "") { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(run);

        return [para];
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

        // Theorem paragraph with left border
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new Indentation { Left = "360" });
        pPr.Append(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 18, Color = "4472C4" }
        ));
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
        headingPPr.Append(new ParagraphStyleId { Val = "Heading1" });
        headingPara.Append(headingPPr);
        headingPara.AppendChild(new Run(new Text("Table of Contents")));
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

            var captionRun = new Run();
            captionRun.Append(new RunProperties(new Bold()));
            captionRun.Append(new Text(content.Caption) { Space = SpaceProcessingModeValues.Preserve });
            captionPara.AppendChild(captionRun);
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
        pPr.Append(new Indentation { Left = "720" });
        pPr.Append(new ParagraphBorders(
            new LeftBorder { Val = BorderValues.Single, Size = 24, Color = "70AD47" }
        ));
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
            if (Enum.TryParse<HighlightColorValues>(span.Highlight, true, out var highlightColor))
            {
                runProps.Append(new Highlight { Val = highlightColor });
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
                    // Inline equation: oMath directly in paragraph (no oMathPara wrapper)
                    var placeholder = new Run();
                    placeholder.InnerXml = omml.Replace(
                        $" xmlns:m=\"{MathNs}\"", ""); // strip namespace decl for inner use
                    // Use a paragraph as a container trick to inject the math element
                    var tempPara = new Paragraph();
                    tempPara.InnerXml = omml;
                    var mathEl = tempPara.FirstChild;
                    if (mathEl != null)
                    {
                        mathEl.Remove();
                        return mathEl;
                    }
                }
                catch { /* fall through */ }
            }
            // Fallback: render as $latex$ text
            return new Run(new Text($"${span.Equation}$") { Space = SpaceProcessingModeValues.Preserve });
        }
        return CreateRun(span);
    }
}
