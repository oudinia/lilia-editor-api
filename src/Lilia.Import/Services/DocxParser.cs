using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Lilia.Import.Services;

/// <summary>
/// Parses DOCX files using DocumentFormat.OpenXml and extracts content
/// into the intermediate ImportDocument model.
/// </summary>
public class DocxParser : IDocxParser
{
    private readonly IOmmlConverter? _ommlConverter;
    private ImportOptions _options = ImportOptions.Default;
    private readonly List<ImportWarning> _warnings = [];
    private int _elementOrder;

    /// <summary>
    /// Create a DocxParser without OMML conversion (OMML XML only).
    /// </summary>
    public DocxParser()
    {
        _ommlConverter = null;
    }

    /// <summary>
    /// Create a DocxParser with OMML to LaTeX conversion.
    /// </summary>
    /// <param name="ommlConverter">The OMML to LaTeX converter.</param>
    public DocxParser(IOmmlConverter ommlConverter)
    {
        _ommlConverter = ommlConverter ?? throw new ArgumentNullException(nameof(ommlConverter));
    }

    /// <inheritdoc />
    public bool CanParse(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null)
    {
        _options = options ?? ImportOptions.Default;
        _warnings.Clear();
        _elementOrder = 0;

        var importDoc = new ImportDocument
        {
            SourcePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath)
        };

        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);

            // Extract metadata
            importDoc.Metadata = ExtractMetadata(doc);

            // Extract title from core properties if available
            if (!string.IsNullOrWhiteSpace(doc.PackageProperties.Title))
            {
                importDoc.Title = doc.PackageProperties.Title;
            }

            // Parse headers and footers first
            var headerFooterElements = ExtractHeadersAndFooters(doc.MainDocumentPart!);

            // Parse footnotes and endnotes
            var noteElements = ExtractFootnotesAndEndnotes(doc.MainDocumentPart!);

            // Parse comments
            var commentElements = ExtractComments(doc.MainDocumentPart!);

            // Parse track changes (revisions)
            var trackChangeElements = ExtractTrackChanges(doc.MainDocumentPart!);

            // Parse document body
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                var elements = ParseBody(body, doc.MainDocumentPart!);

                // Combine all elements: headers/footers at start, body, notes, comments, and track changes at end
                var allElements = new List<ImportElement>();
                allElements.AddRange(headerFooterElements);
                allElements.AddRange(elements);
                allElements.AddRange(noteElements);
                allElements.AddRange(commentElements);
                allElements.AddRange(trackChangeElements);

                importDoc.Elements = allElements;

                // If no title from properties, use first heading
                if (importDoc.Title == Path.GetFileNameWithoutExtension(filePath))
                {
                    var firstHeading = elements.OfType<ImportHeading>().FirstOrDefault();
                    if (firstHeading != null)
                    {
                        importDoc.Title = firstHeading.Text;
                    }
                }
            }

            importDoc.Warnings = [.. _warnings];
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to parse document: {ex.Message}"));
            importDoc.Warnings = [.. _warnings];
        }

        return Task.FromResult(importDoc);
    }

    private List<ImportElement> ExtractHeadersAndFooters(MainDocumentPart mainPart)
    {
        var elements = new List<ImportElement>();

        try
        {
            // Extract headers
            foreach (var headerPart in mainPart.HeaderParts)
            {
                var headerType = GetHeaderType(mainPart, headerPart);
                var header = headerPart.Header;
                if (header != null)
                {
                    var text = ExtractTextFromParts(header);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        elements.Add(new ImportHeader
                        {
                            Order = _elementOrder++,
                            HeaderType = headerType,
                            Text = text
                        });
                    }
                }
            }

            // Extract footers
            foreach (var footerPart in mainPart.FooterParts)
            {
                var footerType = GetFooterType(mainPart, footerPart);
                var footer = footerPart.Footer;
                if (footer != null)
                {
                    var text = ExtractTextFromParts(footer);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        elements.Add(new ImportFooter
                        {
                            Order = _elementOrder++,
                            FooterType = footerType,
                            Text = text
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to extract headers/footers: {ex.Message}"));
        }

        return elements;
    }

    private Models.HeaderFooterType GetHeaderType(MainDocumentPart mainPart, HeaderPart headerPart)
    {
        var headerRef = mainPart.Document?.Body?
            .Descendants<SectionProperties>()
            .SelectMany(sp => sp.Descendants<HeaderReference>())
            .FirstOrDefault(hr => hr.Id?.Value == mainPart.GetIdOfPart(headerPart));

        if (headerRef?.Type?.Value == HeaderFooterValues.First)
            return Models.HeaderFooterType.First;
        if (headerRef?.Type?.Value == HeaderFooterValues.Even)
            return Models.HeaderFooterType.Even;

        return Models.HeaderFooterType.Default;
    }

    private FooterType GetFooterType(MainDocumentPart mainPart, FooterPart footerPart)
    {
        var footerRef = mainPart.Document?.Body?
            .Descendants<SectionProperties>()
            .SelectMany(sp => sp.Descendants<FooterReference>())
            .FirstOrDefault(fr => fr.Id?.Value == mainPart.GetIdOfPart(footerPart));

        if (footerRef?.Type?.Value == HeaderFooterValues.First)
            return FooterType.First;
        if (footerRef?.Type?.Value == HeaderFooterValues.Even)
            return FooterType.Even;

        return FooterType.Default;
    }

    private string ExtractTextFromParts(OpenXmlElement container)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var para in container.Descendants<Paragraph>())
        {
            var text = string.Concat(para.Descendants<Text>().Select(t => t.Text));
            if (!string.IsNullOrEmpty(text))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(text);
            }
        }
        return sb.ToString().Trim();
    }

    private List<ImportElement> ExtractFootnotesAndEndnotes(MainDocumentPart mainPart)
    {
        var elements = new List<ImportElement>();

        try
        {
            // Extract footnotes
            var footnotesPart = mainPart.FootnotesPart;
            if (footnotesPart?.Footnotes != null)
            {
                foreach (var footnote in footnotesPart.Footnotes.Elements<Footnote>())
                {
                    var noteId = (int)(footnote.Id?.Value ?? 0);
                    // Skip special footnotes (separator, continuation)
                    if (noteId < 1) continue;

                    var text = ExtractTextFromParts(footnote);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        elements.Add(new ImportFootnote
                        {
                            Order = _elementOrder++,
                            NoteId = noteId,
                            Text = text
                        });
                    }
                }
            }

            // Extract endnotes
            var endnotesPart = mainPart.EndnotesPart;
            if (endnotesPart?.Endnotes != null)
            {
                foreach (var endnote in endnotesPart.Endnotes.Elements<Endnote>())
                {
                    var noteId = (int)(endnote.Id?.Value ?? 0);
                    // Skip special endnotes (separator, continuation)
                    if (noteId < 1) continue;

                    var text = ExtractTextFromParts(endnote);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        elements.Add(new ImportEndnote
                        {
                            Order = _elementOrder++,
                            NoteId = noteId,
                            Text = text
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to extract footnotes/endnotes: {ex.Message}"));
        }

        return elements;
    }

    private List<ImportElement> ExtractTrackChanges(MainDocumentPart mainPart)
    {
        var elements = new List<ImportElement>();

        try
        {
            var body = mainPart.Document?.Body;
            if (body == null)
                return elements;

            // Find all insertions
            foreach (var ins in body.Descendants<InsertedRun>())
            {
                var author = ins.Author?.Value;
                var dateVal = ins.Date?.Value;
                int.TryParse(ins.Id?.Value, out var revId);

                DateTime? date = null;
                if (dateVal.HasValue)
                {
                    date = dateVal.Value;
                }

                var text = string.Concat(ins.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    elements.Add(new ImportTrackChange
                    {
                        Order = _elementOrder++,
                        ChangeType = Models.TrackChangeType.Insertion,
                        Author = author,
                        Date = date,
                        Text = text,
                        RevisionId = revId
                    });
                }
            }

            // Find all deletions
            foreach (var del in body.Descendants<DeletedRun>())
            {
                var author = del.Author?.Value;
                var dateVal = del.Date?.Value;
                int.TryParse(del.Id?.Value, out var revId);

                DateTime? date = null;
                if (dateVal.HasValue)
                {
                    date = dateVal.Value;
                }

                var text = string.Concat(del.Descendants<DeletedText>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    elements.Add(new ImportTrackChange
                    {
                        Order = _elementOrder++,
                        ChangeType = Models.TrackChangeType.Deletion,
                        Author = author,
                        Date = date,
                        Text = text,
                        RevisionId = revId
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to extract track changes: {ex.Message}"));
        }

        return elements;
    }

    private List<ImportElement> ExtractComments(MainDocumentPart mainPart)
    {
        var elements = new List<ImportElement>();

        try
        {
            var commentsPart = mainPart.WordprocessingCommentsPart;
            if (commentsPart?.Comments == null)
                return elements;

            foreach (var comment in commentsPart.Comments.Elements<Comment>())
            {
                int.TryParse(comment.Id?.Value, out var commentId);
                var author = comment.Author?.Value;
                var initials = comment.Initials?.Value;
                var dateStr = comment.Date?.Value;

                DateTime? date = null;
                if (dateStr != null)
                {
                    date = dateStr;
                }

                var text = ExtractTextFromParts(comment);
                if (!string.IsNullOrWhiteSpace(text) || author != null)
                {
                    elements.Add(new ImportComment
                    {
                        Order = _elementOrder++,
                        CommentId = commentId,
                        Author = author,
                        Initials = initials,
                        Date = date,
                        Text = text
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to extract comments: {ex.Message}"));
        }

        return elements;
    }

    private ImportMetadata ExtractMetadata(WordprocessingDocument doc)
    {
        var metadata = new ImportMetadata();

        try
        {
            var props = doc.PackageProperties;
            metadata.Author = props.Creator;
            metadata.Subject = props.Subject;
            metadata.Description = props.Description;
            metadata.Keywords = props.Keywords;
            metadata.Created = props.Created;
            metadata.Modified = props.Modified;

            // Try to get application info from extended properties
            var extProps = doc.ExtendedFilePropertiesPart?.Properties;
            if (extProps != null)
            {
                metadata.Application = extProps.Application?.Text;
                metadata.AppVersion = extProps.ApplicationVersion?.Text;
            }
        }
        catch
        {
            // Ignore metadata extraction errors
        }

        return metadata;
    }

    private List<ImportElement> ParseBody(Body body, MainDocumentPart mainPart)
    {
        var elements = new List<ImportElement>();

        foreach (var element in body.ChildElements)
        {
            var parsed = ParseElement(element, mainPart);
            if (parsed != null)
            {
                elements.AddRange(parsed);
            }
        }

        return elements;
    }

    private IEnumerable<ImportElement>? ParseElement(OpenXmlElement element, MainDocumentPart mainPart)
    {
        return element switch
        {
            Paragraph para => ParseParagraph(para, mainPart),
            Table table => ParseTable(table, mainPart),
            SdtBlock sdt => ParseSdtBlock(sdt, mainPart),
            _ => null // Skip unsupported elements
        };
    }

    private IEnumerable<ImportElement>? ParseSdtBlock(SdtBlock sdt, MainDocumentPart mainPart)
    {
        // Check if this is a TOC block
        var docPartGallery = sdt.SdtProperties?.GetFirstChild<SdtContentDocPartObject>()
            ?.DocPartGallery?.Val?.Value;

        if (docPartGallery == "Table of Contents")
        {
            return ParseTableOfContents(sdt, mainPart);
        }

        // Parse content of SDT block normally
        var content = sdt.SdtContentBlock;
        if (content != null)
        {
            var elements = new List<ImportElement>();
            foreach (var child in content.ChildElements)
            {
                var parsed = ParseElement(child, mainPart);
                if (parsed != null)
                {
                    elements.AddRange(parsed);
                }
            }
            return elements.Count > 0 ? elements : null;
        }

        return null;
    }

    private IEnumerable<ImportElement>? ParseTableOfContents(SdtBlock sdt, MainDocumentPart mainPart)
    {
        var toc = new ImportTableOfContents
        {
            Order = _elementOrder++,
            Title = "Table of Contents",
            Entries = []
        };

        // Extract TOC entries from the content
        var content = sdt.SdtContentBlock;
        if (content != null)
        {
            foreach (var para in content.Descendants<Paragraph>())
            {
                var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

                // Check for TOC heading style
                if (styleId?.StartsWith("TOCHeading", StringComparison.OrdinalIgnoreCase) == true)
                {
                    ExtractTextAndFormatting(para, out var titleText, out _);
                    if (!string.IsNullOrWhiteSpace(titleText))
                    {
                        toc.Title = titleText;
                    }
                    continue;
                }

                // Check for TOC entry styles (TOC1, TOC2, etc.)
                if (styleId?.StartsWith("TOC", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var levelStr = styleId.Substring(3);
                    if (int.TryParse(levelStr, out var level))
                    {
                        ExtractTextAndFormatting(para, out var entryText, out _);
                        if (!string.IsNullOrWhiteSpace(entryText))
                        {
                            // Try to extract page number (usually at the end after tab)
                            var parts = entryText.Split('\t');
                            var text = parts[0].Trim();
                            var pageNumber = parts.Length > 1 ? parts[^1].Trim() : null;

                            toc.Entries.Add(new TocEntry
                            {
                                Text = text,
                                Level = level,
                                PageNumber = pageNumber
                            });
                        }
                    }
                }
            }
        }

        // Also try to find field code
        var fieldCode = sdt.Descendants<FieldCode>().FirstOrDefault()?.Text;
        if (!string.IsNullOrEmpty(fieldCode))
        {
            toc.FieldCode = fieldCode;
        }

        return [toc];
    }

    private IEnumerable<ImportElement>? ParseParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        // Check for page breaks first
        var pageBreaks = para.Descendants<Break>()
            .Where(br => br.Type?.Value == BreakValues.Page)
            .ToList();

        if (pageBreaks.Count > 0)
        {
            var results = new List<ImportElement>();

            // Add a page break element for each page break found
            foreach (var _ in pageBreaks)
            {
                results.Add(new ImportPageBreak { Order = _elementOrder++ });
            }

            // If paragraph has content besides the page break, also parse it
            ExtractTextAndFormatting(para, out var pageBreakText, out _);
            if (!string.IsNullOrWhiteSpace(pageBreakText))
            {
                var pageBreakStyleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                var contentAfterBreak = CreateParagraph(para, pageBreakStyleId);
                if (contentAfterBreak != null)
                {
                    results.Add(contentAfterBreak);
                }
            }

            return results.Count > 0 ? results : null;
        }

        // Check if it's a heading
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var headingLevel = GetHeadingLevel(styleId, para);

        if (headingLevel.HasValue)
        {
            var heading = new ImportHeading
            {
                Order = _elementOrder++,
                Level = headingLevel.Value,
                StyleId = styleId
            };
            ExtractTextAndFormatting(para, out var text, out var formatting);
            heading.Text = text;
            heading.Formatting = formatting;
            return [heading];
        }

        // Check if it's a list item
        var numPr = para.ParagraphProperties?.NumberingProperties;
        if (numPr != null)
        {
            var listItem = CreateListItem(para, numPr, mainPart);
            if (listItem != null)
            {
                return [listItem];
            }
        }

        // Check if paragraph contains only an equation
        var mathElements = para.Descendants<M.OfficeMath>().ToList();
        if (mathElements.Count > 0 && IsEquationOnlyParagraph(para, mathElements))
        {
            var equations = new List<ImportElement>();
            foreach (var math in mathElements)
            {
                var equation = new ImportEquation
                {
                    Order = _elementOrder++,
                    OmmlXml = math.OuterXml,
                    IsInline = false
                };

                // Convert OMML to LaTeX if converter is available
                if (_ommlConverter != null)
                {
                    var (latex, success, error) = _ommlConverter.Convert(math.OuterXml);
                    equation.LatexContent = latex;
                    equation.ConversionSucceeded = success;
                    equation.ConversionError = error;

                    if (!success)
                    {
                        _warnings.Add(new ImportWarning(
                            ImportWarningType.EquationConversionFailed,
                            $"Equation conversion failed: {error}",
                            equation.Order));
                    }
                }

                equations.Add(equation);
            }
            return equations;
        }

        // Check if it's an abstract section (before code block to prevent misclassification)
        if (IsAbstract(para))
        {
            ExtractTextAndFormatting(para, out var absText, out var absFormatting);
            if (!string.IsNullOrWhiteSpace(absText))
            {
                return [new ImportAbstract
                {
                    Order = _elementOrder++,
                    Text = absText,
                    Formatting = _options.PreserveFormatting ? absFormatting : [],
                    StyleId = styleId
                }];
            }
        }

        // Check if it's a code block
        if (IsCodeBlock(para))
        {
            var codeBlock = new ImportCodeBlock
            {
                Order = _elementOrder++,
                StyleId = styleId,
                DetectionReason = GetCodeBlockDetectionReason(para)
            };
            ExtractTextAndFormatting(para, out var text, out _);
            codeBlock.Text = text;
            codeBlock.FontFamily = GetFontFamily(para);
            return [codeBlock];
        }

        // Check for images
        var images = ExtractImages(para, mainPart);
        if (images.Count > 0)
        {
            foreach (var img in images)
            {
                img.Order = _elementOrder++;
            }

            // If paragraph has only images, return just the images
            ExtractTextAndFormatting(para, out var text, out _);
            if (string.IsNullOrWhiteSpace(text))
            {
                return images;
            }

            // Otherwise, include both the paragraph and images
            var results = new List<ImportElement>();
            var paragraph = CreateParagraph(para, styleId);
            if (paragraph != null)
            {
                results.Add(paragraph);
            }
            results.AddRange(images);
            return results;
        }

        // Regular paragraph
        var normalPara = CreateParagraph(para, styleId);
        return normalPara != null ? [normalPara] : null;
    }

    private ImportParagraph? CreateParagraph(Paragraph para, string? styleId)
    {
        ExtractTextAndFormatting(para, out var text, out var formatting);

        // Skip empty paragraphs
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var paragraph = new ImportParagraph
        {
            Order = _elementOrder++,
            Text = text,
            Formatting = _options.PreserveFormatting ? formatting : [],
            StyleId = styleId,
            Style = GetParagraphStyle(styleId, para)
        };

        return paragraph;
    }

    private ImportListItem? CreateListItem(Paragraph para, NumberingProperties numPr, MainDocumentPart mainPart)
    {
        ExtractTextAndFormatting(para, out var text, out var formatting);

        // Skip empty list items
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var level = (int)(numPr.NumberingLevelReference?.Val?.Value ?? 0);
        var numId = numPr.NumberingId?.Val?.Value;

        // Determine if numbered or bulleted
        var isNumbered = IsNumberedList(numId, level, mainPart);

        return new ImportListItem
        {
            Order = _elementOrder++,
            Text = text,
            Formatting = _options.PreserveFormatting ? formatting : [],
            Level = level,
            IsNumbered = isNumbered,
            ListMarker = GetListMarker(numId, level, mainPart)
        };
    }

    private bool IsNumberedList(int? numId, int level, MainDocumentPart mainPart)
    {
        if (!numId.HasValue)
            return false;

        try
        {
            var numberingPart = mainPart.NumberingDefinitionsPart;
            if (numberingPart?.Numbering == null)
                return false;

            // Find the numbering instance
            var numInstance = numberingPart.Numbering
                .Elements<NumberingInstance>()
                .FirstOrDefault(n => n.NumberID?.Value == numId.Value);

            if (numInstance?.AbstractNumId?.Val?.Value == null)
                return false;

            var abstractNumId = numInstance.AbstractNumId.Val.Value;

            // Find the abstract numbering definition
            var abstractNum = numberingPart.Numbering
                .Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);

            if (abstractNum == null)
                return false;

            // Get the level definition
            var levelDef = abstractNum
                .Elements<Level>()
                .FirstOrDefault(l => l.LevelIndex?.Value == level);

            if (levelDef?.NumberingFormat?.Val?.Value == null)
                return false;

            // Check the numbering format
            var format = levelDef.NumberingFormat.Val.Value;
            return format != NumberFormatValues.Bullet;
        }
        catch
        {
            return false; // Default to bullet if we can't determine
        }
    }

    private string? GetListMarker(int? numId, int level, MainDocumentPart mainPart)
    {
        if (!numId.HasValue)
            return null;

        try
        {
            var numberingPart = mainPart.NumberingDefinitionsPart;
            if (numberingPart?.Numbering == null)
                return null;

            var numInstance = numberingPart.Numbering
                .Elements<NumberingInstance>()
                .FirstOrDefault(n => n.NumberID?.Value == numId.Value);

            if (numInstance?.AbstractNumId?.Val?.Value == null)
                return null;

            var abstractNumId = numInstance.AbstractNumId.Val.Value;

            var abstractNum = numberingPart.Numbering
                .Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);

            if (abstractNum == null)
                return null;

            var levelDef = abstractNum
                .Elements<Level>()
                .FirstOrDefault(l => l.LevelIndex?.Value == level);

            return levelDef?.LevelText?.Val?.Value;
        }
        catch
        {
            return null;
        }
    }

    private void ExtractTextAndFormatting(Paragraph para, out string text, out List<FormattingSpan> formatting)
    {
        var sb = new System.Text.StringBuilder();
        formatting = [];

        foreach (var run in para.Descendants<Run>())
        {
            var runText = string.Concat(run.Descendants<Text>().Select(t => t.Text));
            var startIndex = sb.Length;

            sb.Append(runText);

            if (_options.PreserveFormatting && runText.Length > 0)
            {
                var runProps = run.RunProperties;
                if (runProps != null)
                {
                    // Basic formatting
                    if (runProps.Bold != null)
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Bold));
                    if (runProps.Italic != null)
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Italic));
                    if (runProps.Underline != null)
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Underline));
                    if (runProps.Strike != null)
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Strikethrough));
                    if (runProps.VerticalTextAlignment?.Val?.Value == VerticalPositionValues.Superscript)
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Superscript));
                    if (runProps.VerticalTextAlignment?.Val?.Value == VerticalPositionValues.Subscript)
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Subscript));

                    // Font color
                    var color = runProps.Color?.Val?.Value;
                    if (!string.IsNullOrEmpty(color) && color != "auto")
                    {
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.FontColor, $"#{color}"));
                    }

                    // Font size (in half-points, convert to points)
                    var fontSize = runProps.FontSize?.Val?.Value;
                    if (!string.IsNullOrEmpty(fontSize) && int.TryParse(fontSize, out var sizeHalfPoints))
                    {
                        var sizePoints = sizeHalfPoints / 2.0;
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.FontSize, $"{sizePoints}pt"));
                    }

                    // Font family
                    var fonts = runProps.RunFonts;
                    var fontFamily = fonts?.Ascii?.Value ?? fonts?.HighAnsi?.Value ?? fonts?.ComplexScript?.Value;
                    if (!string.IsNullOrEmpty(fontFamily))
                    {
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.FontFamily, fontFamily));
                    }

                    // Highlight color
                    var highlight = runProps.Highlight?.Val?.Value;
                    if (highlight != null && highlight != HighlightColorValues.None)
                    {
                        formatting.Add(new FormattingSpan(startIndex, runText.Length, FormattingType.Highlight, highlight.ToString()));
                    }
                }
            }
        }

        text = sb.ToString();
    }

    private int? GetHeadingLevel(string? styleId, Paragraph para)
    {
        // Check for standard heading styles (Heading1, Heading2, etc.)
        if (!string.IsNullOrEmpty(styleId))
        {
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                var levelStr = styleId.Substring(7);
                if (int.TryParse(levelStr, out var level) && level >= 1 && level <= 9)
                {
                    if (level >= _options.MinHeadingLevelForSection && level <= _options.MaxHeadingLevelForSection)
                    {
                        return level;
                    }
                }
            }

            // Check for custom heading style patterns
            var lowerStyle = styleId.ToLowerInvariant();
            if (lowerStyle.Contains("title") && !lowerStyle.Contains("subtitle"))
                return 1;
            if (lowerStyle.Contains("section") || lowerStyle.Contains("chapter"))
            {
                // Try to extract level from style name
                var match = System.Text.RegularExpressions.Regex.Match(styleId, @"\d+");
                if (match.Success && int.TryParse(match.Value, out var lvl) && lvl >= 1 && lvl <= 6)
                    return lvl;
                return 1;
            }
        }

        // Check outline level in paragraph properties
        var outlineLevel = para.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (outlineLevel.HasValue)
        {
            var level = outlineLevel.Value + 1; // OutlineLevel is 0-based
            if (level >= _options.MinHeadingLevelForSection && level <= _options.MaxHeadingLevelForSection)
            {
                return level;
            }
        }

        // Heuristic: Detect headings by formatting and text patterns
        if (_options.DetectHeadingsByFormatting)
        {
            var detectedLevel = DetectHeadingByFormatting(para);
            if (detectedLevel.HasValue)
                return detectedLevel;
        }

        return null;
    }

    private int? DetectHeadingByFormatting(Paragraph para)
    {
        // Extract text for pattern matching
        var text = string.Concat(para.Descendants<Text>().Select(t => t.Text)).Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 200)
            return null; // Too long for a heading

        // Check if entire paragraph is bold (common heading indicator)
        var runs = para.Descendants<Run>().ToList();
        var allBold = runs.Count > 0 && runs.All(r => r.RunProperties?.Bold != null);

        // Get font size
        double? fontSize = null;
        var firstRun = runs.FirstOrDefault();
        if (firstRun?.RunProperties?.FontSize?.Val?.Value is string sizeStr)
        {
            if (double.TryParse(sizeStr, out var halfPoints))
                fontSize = halfPoints / 2.0; // Convert half-points to points
        }

        // Pattern: Numbered section headings like "1. Introduction" or "1.1 Methods" or "1.1.1 Subsection"
        var numberedPattern = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+(?:\.\d+)*)\s*\.?\s+([A-Z])");
        if (numberedPattern.Success)
        {
            var numberPart = numberedPattern.Groups[1].Value;
            var dotCount = numberPart.Count(c => c == '.');
            var level = Math.Min(dotCount + 1, 6);

            // For clearly numbered patterns (X.X.X format), be more lenient
            // They're headings even without bold/large font if:
            // - Pattern has 2+ levels (like 1.1 or 1.1.1)
            // - Text is short (< 100 chars)
            // - Starts with capital letter after number
            if (dotCount >= 1 && text.Length < 100)
            {
                return level;
            }

            // For single-level numbers (like "1 Introduction"), require bold or larger font
            if (allBold || (fontSize.HasValue && fontSize.Value >= 11))
            {
                return level;
            }
        }

        // Pattern: Roman numeral sections like "I. Introduction" or "II. Methods"
        var romanPattern = System.Text.RegularExpressions.Regex.Match(text, @"^([IVXLC]+)\.\s+\w", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (romanPattern.Success && (allBold || (fontSize.HasValue && fontSize.Value >= 11)))
        {
            var roman = romanPattern.Groups[1].Value.ToUpperInvariant();
            // Simple roman numeral to level mapping
            return roman.Length <= 2 ? 1 : 2;
        }

        // Pattern: All caps short text (likely a heading)
        if (text.Length <= 50 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
        {
            if (allBold || (fontSize.HasValue && fontSize.Value >= 12))
                return 1;
        }

        // Bold text with larger font size
        if (allBold && fontSize.HasValue && fontSize.Value >= 14)
            return 1;
        if (allBold && fontSize.HasValue && fontSize.Value >= 12)
            return 2;

        return null;
    }

    private bool IsEquationOnlyParagraph(Paragraph para, List<M.OfficeMath> mathElements)
    {
        // Check if the paragraph contains only math and whitespace
        var textContent = string.Concat(para.Descendants<Text>().Select(t => t.Text));
        return string.IsNullOrWhiteSpace(textContent) || mathElements.Count > 0;
    }

    private bool IsAbstract(Paragraph para)
    {
        if (!_options.DetectAbstractByStyle)
            return false;

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrEmpty(styleId))
            return false;

        foreach (var pattern in _options.AbstractStylePatterns)
        {
            if (styleId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool IsCodeBlock(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        // Check by style name
        if (_options.DetectCodeByStyle && !string.IsNullOrEmpty(styleId))
        {
            foreach (var pattern in _options.CodeStylePatterns)
            {
                if (styleId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Check by font family
        if (_options.DetectCodeByFont)
        {
            var fontName = GetFontFamily(para);
            if (!string.IsNullOrEmpty(fontName) && _options.MonospaceFonts.Contains(fontName))
                return true;
        }

        // Check by shading
        if (_options.DetectCodeByShading)
        {
            var shading = para.ParagraphProperties?.Shading;
            if (shading?.Fill?.Value is string fill && IsCodeShading(fill))
                return true;
        }

        return false;
    }

    private CodeBlockDetectionReason GetCodeBlockDetectionReason(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        if (_options.DetectCodeByStyle && !string.IsNullOrEmpty(styleId))
        {
            foreach (var pattern in _options.CodeStylePatterns)
            {
                if (styleId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return CodeBlockDetectionReason.StyleName;
            }
        }

        if (_options.DetectCodeByFont)
        {
            var fontName = GetFontFamily(para);
            if (!string.IsNullOrEmpty(fontName) && _options.MonospaceFonts.Contains(fontName))
                return CodeBlockDetectionReason.MonospaceFont;
        }

        if (_options.DetectCodeByShading)
        {
            var shading = para.ParagraphProperties?.Shading;
            if (shading?.Fill?.Value is string fill && IsCodeShading(fill))
                return CodeBlockDetectionReason.Shading;
        }

        return CodeBlockDetectionReason.Manual;
    }

    private string? GetFontFamily(Paragraph para)
    {
        // Check paragraph-level font from mark run properties
        var markProps = para.ParagraphProperties?.ParagraphMarkRunProperties;
        if (markProps != null)
        {
            var pFonts = markProps.GetFirstChild<RunFonts>();
            if (pFonts != null)
            {
                return pFonts.Ascii?.Value ?? pFonts.HighAnsi?.Value ?? pFonts.ComplexScript?.Value;
            }
        }

        // Check first run's font
        var firstRun = para.Descendants<Run>().FirstOrDefault();
        var rFonts = firstRun?.RunProperties?.RunFonts;
        return rFonts?.Ascii?.Value ?? rFonts?.HighAnsi?.Value ?? rFonts?.ComplexScript?.Value;
    }

    private static bool IsCodeShading(string fillColor)
    {
        // Check for gray shades commonly used for code blocks
        // Typical values: E0E0E0, F0F0F0, EEEEEE, D3D3D3
        if (fillColor.Length == 6 && fillColor.All(c => char.IsLetterOrDigit(c)))
        {
            try
            {
                var r = Convert.ToInt32(fillColor.Substring(0, 2), 16);
                var g = Convert.ToInt32(fillColor.Substring(2, 2), 16);
                var b = Convert.ToInt32(fillColor.Substring(4, 2), 16);

                // Check if it's a gray color (R ≈ G ≈ B) and light (> 180)
                var isGray = Math.Abs(r - g) < 20 && Math.Abs(g - b) < 20 && Math.Abs(r - b) < 20;
                var isLight = r > 180 && g > 180 && b > 180;
                return isGray && isLight;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private ParagraphStyle GetParagraphStyle(string? styleId, Paragraph para)
    {
        if (string.IsNullOrEmpty(styleId))
            return ParagraphStyle.Normal;

        var lower = styleId.ToLowerInvariant();

        if (lower.Contains("quote") || lower.Contains("blockquote"))
            return ParagraphStyle.Quote;
        if (lower.Contains("title"))
            return ParagraphStyle.Title;
        if (lower.Contains("subtitle"))
            return ParagraphStyle.Subtitle;
        if (lower.Contains("caption"))
            return ParagraphStyle.Caption;

        return ParagraphStyle.Normal;
    }

    private List<ImportImage> ExtractImages(Paragraph para, MainDocumentPart mainPart)
    {
        var images = new List<ImportImage>();

        if (!_options.ExtractImages)
            return images;

        var drawings = para.Descendants<Drawing>().ToList();
        foreach (var drawing in drawings)
        {
            var image = ExtractImage(drawing, mainPart);
            if (image != null)
            {
                images.Add(image);
            }
        }

        return images;
    }

    private ImportImage? ExtractImage(Drawing drawing, MainDocumentPart mainPart)
    {
        try
        {
            // Get the blip (image reference)
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value == null)
                return null;

            var relationshipId = blip.Embed.Value;
            var imagePart = mainPart.GetPartById(relationshipId) as ImagePart;
            if (imagePart == null)
                return null;

            // Get image data
            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var data = ms.ToArray();

            // Get dimensions
            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();

            // Get alt text
            var docProperties = drawing.Descendants<DW.DocProperties>().FirstOrDefault();
            var altText = docProperties?.Description?.Value ?? docProperties?.Name?.Value;

            return new ImportImage
            {
                Data = data,
                MimeType = imagePart.ContentType,
                RelationshipId = relationshipId,
                AltText = altText,
                WidthEmu = extent?.Cx?.Value,
                HeightEmu = extent?.Cy?.Value,
                Filename = Path.GetFileName(imagePart.Uri.ToString())
            };
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.ImageExtractionFailed,
                $"Failed to extract image: {ex.Message}"));
            return null;
        }
    }

    private IEnumerable<ImportElement>? ParseTable(Table table, MainDocumentPart mainPart)
    {
        if (!_options.ExtractTables)
            return null;

        try
        {
            var importTable = new ImportTable
            {
                Order = _elementOrder++
            };

            // Check for header row
            var firstRow = table.Elements<TableRow>().FirstOrDefault();
            if (firstRow != null)
            {
                var headerProp = firstRow.TableRowProperties?.GetFirstChild<TableHeader>();
                importTable.HasHeaderRow = headerProp != null;
            }

            foreach (var row in table.Elements<TableRow>())
            {
                var importRow = new List<ImportTableCell>();

                foreach (var cell in row.Elements<TableCell>())
                {
                    var importCell = new ImportTableCell();

                    // Extract cell text and formatting
                    var cellText = new System.Text.StringBuilder();
                    var cellFormatting = new List<FormattingSpan>();

                    foreach (var para in cell.Elements<Paragraph>())
                    {
                        ExtractTextAndFormatting(para, out var text, out var formatting);
                        if (cellText.Length > 0 && !string.IsNullOrEmpty(text))
                        {
                            cellText.Append('\n');
                        }
                        // Adjust formatting offsets
                        foreach (var span in formatting)
                        {
                            span.Start += cellText.Length;
                        }
                        cellText.Append(text);
                        cellFormatting.AddRange(formatting);
                    }

                    importCell.Text = cellText.ToString();
                    importCell.Formatting = cellFormatting;

                    // Check for merged cells
                    var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value;
                    if (gridSpan.HasValue)
                    {
                        importCell.ColSpan = (int)gridSpan.Value;
                    }

                    var vMerge = cell.TableCellProperties?.VerticalMerge;
                    if (vMerge != null && vMerge.Val?.Value == MergedCellValues.Restart)
                    {
                        // This is the start of a vertical merge
                        importCell.RowSpan = 1; // Will need to count following cells
                    }

                    importRow.Add(importCell);
                }

                importTable.Rows.Add(importRow);
            }

            return [importTable];
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to parse table: {ex.Message}"));
            return null;
        }
    }
}
