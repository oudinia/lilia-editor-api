using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lilia.Import.Detection;
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
    private readonly IImportTelemetrySink _telemetry;
    private ImportOptions _options = ImportOptions.Default;
    private readonly List<ImportWarning> _warnings = [];
    private int _elementOrder;
    private ElementDetectionPipeline? _pipeline;
    private string? _currentSourceFile;
    // Per-parse cache: styleId → StyleName from styles.xml. Populated lazily
    // on first lookup per MainDocumentPart. Language-neutral style names
    // ("heading 1", "heading 2", …) let us detect headings regardless of the
    // authoring language (Word writes Titre/berschrift/Titolo/etc. as the
    // styleId but always "heading N" as the w:name).
    private readonly Dictionary<MainDocumentPart, Dictionary<string, string>> _styleNameCache = [];

    /// <summary>
    /// Create a DocxParser without OMML conversion (OMML XML only).
    /// </summary>
    public DocxParser()
    {
        _ommlConverter = null;
        _telemetry = new NoopImportTelemetrySink();
    }

    /// <summary>
    /// Create a DocxParser with OMML to LaTeX conversion.
    /// </summary>
    /// <param name="ommlConverter">The OMML to LaTeX converter.</param>
    /// <param name="telemetry">Optional sink for FT-TELEMETRY-001 events; defaults to no-op.</param>
    public DocxParser(IOmmlConverter ommlConverter, IImportTelemetrySink? telemetry = null)
    {
        _ommlConverter = ommlConverter ?? throw new ArgumentNullException(nameof(ommlConverter));
        _telemetry = telemetry ?? new NoopImportTelemetrySink();
    }

    /// <summary>
    /// Bridge from the existing _warnings collection to the telemetry sink.
    /// Each ImportWarning becomes an `import_telemetry_events` row tagged
    /// `parser_warning` so the production telemetry table reflects DOCX
    /// fall-throughs alongside the LaTeX ones (FT-TELEMETRY-001 stage 2).
    /// Called from ParseAsync after the file has been processed; we drain
    /// the per-call warning batch in one shot to keep the parser hot path
    /// uncoupled from the sink.
    /// </summary>
    private void EmitWarningsAsTelemetry()
    {
        foreach (var w in _warnings)
        {
            _telemetry.Record(new ImportTelemetryRecord
            {
                EventKind = ClassifyWarning(w.Type),
                Severity = "warn",
                SourceFormat = "docx",
                TokenOrEnv = w.Type.ToString(),
                SampleText = w.Message.Length > 200 ? w.Message[..200] : w.Message,
                SourceFileName = _currentSourceFile,
                Metadata = w.Details is null && w.ElementIndex is null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(new
                    {
                        elementIndex = w.ElementIndex,
                        details = w.Details,
                    }),
            });
        }
    }

    /// <summary>
    /// Map ImportWarningType → telemetry event_kind. Closed vocabulary
    /// matches the CHECK constraint in the import_telemetry_events table.
    /// </summary>
    private static string ClassifyWarning(ImportWarningType type) => type switch
    {
        ImportWarningType.UnsupportedElement       => "unsupported_block_emitted",
        ImportWarningType.UnknownStyle             => "unknown_token",
        ImportWarningType.EquationConversionFailed => "partial_parse",
        ImportWarningType.ImageExtractionFailed    => "partial_parse",
        ImportWarningType.FormattingLost           => "cmd_passthrough",
        ImportWarningType.NestedTableSkipped       => "silent_fallback",
        ImportWarningType.MergedCellsSimplified    => "silent_fallback",
        ImportWarningType.ContentTruncated         => "partial_parse",
        _                                          => "parser_warning",
    };

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
        _frontAuthors.Clear();
        _frontDate = null;
        _currentSourceFile = Path.GetFileName(filePath);

        // Build the detection pipeline
        var defaultRules = DefaultRuleRegistry.BuildRules(_options);
        _pipeline = new ElementDetectionPipeline(
            defaultRules,
            _options.CustomDetectionRules,
            _options.DisabledRuleIds);

        var importDoc = new ImportDocument
        {
            SourcePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath)
        };
        string? repairedCopy = null;

        try
        {
            using var doc = OpenTolerant(filePath, out repairedCopy);
            _currentMainPart = doc.MainDocumentPart;

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

                // The Title-style paragraph is the document's title, not body text (MCP-124: it became a
                // paragraph, and the title came from the file name). Else the file's title property.
                var titlePara = elements.OfType<ImportParagraph>()
                    .FirstOrDefault(p => p.Style == ParagraphStyle.Title && !string.IsNullOrWhiteSpace(p.Text));
                if (titlePara != null)
                {
                    importDoc.Metadata.SourceTitle = titlePara.Text.Trim();
                    allElements.Remove(titlePara);
                    elements.Remove(titlePara);
                }
                else if (!string.IsNullOrWhiteSpace(doc.PackageProperties.Title))
                    importDoc.Metadata.SourceTitle = doc.PackageProperties.Title.Trim();
                if (importDoc.Metadata.SourceTitle is { } sourceTitle) importDoc.Title = sourceTitle;
                ReadPageSetup(body, importDoc.Metadata);

                importDoc.Elements = allElements;
                if (_frontAuthors.Count > 0) importDoc.Metadata.SourceAuthor = string.Join(" \\and ", _frontAuthors);
                if (_frontDate is not null) importDoc.Metadata.SourceDate = _frontDate;
                importDoc.Metadata.HeadingNumbering = Lilia.Import.Detection.HeadingNumbering.Detect(elements);

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

            // Attach paragraph traces from the pipeline
            if (_pipeline != null)
            {
                importDoc.ParagraphTraces = [.. _pipeline.Traces];
            }
        }
        catch (Exception ex)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.UnsupportedElement,
                $"Failed to parse document: {ex.Message}"));
            importDoc.Warnings = [.. _warnings];
            importDoc.ParseError = ex.Message;
        }
        finally
        {
            if (repairedCopy is not null) try { File.Delete(repairedCopy); } catch (IOException) { }
        }

        // FT-TELEMETRY-001 stage 2: drain ImportWarnings to the central
        // telemetry sink so DOCX fall-throughs surface in
        // import_telemetry_events alongside LaTeX ones. No-op sink in
        // tests; production sink batches via Channel + background flusher.
        EmitWarningsAsTelemetry();

        return Task.FromResult(importDoc);
    }

    /// <summary>
    /// Opens the package, and when it won't open, a copy with its exact duplicates removed: a part declared twice in
    /// [Content_Types].xml, or a relationship given twice (same type and target). Word and LibreOffice open such files;
    /// the SDK refused them, and the import came back empty (launch-bot #196, 18 Nov 2026).
    /// </summary>
    private WordprocessingDocument OpenTolerant(string filePath, out string? repairedCopy)
    {
        repairedCopy = null;
        try
        {
            return WordprocessingDocument.Open(filePath, false);
        }
        catch (Exception first) when (first is not OutOfMemoryException)
        {
            var copy = Path.Combine(Path.GetTempPath(), $"lilia-docx-repair-{Guid.NewGuid():N}.docx");
            File.Copy(filePath, copy);
            if (!RemoveDuplicatePackageEntries(copy))
            {
                File.Delete(copy);
                throw;
            }
            repairedCopy = copy;
            var doc = WordprocessingDocument.Open(copy, false);
            _warnings.Add(new ImportWarning(ImportWarningType.UnsupportedElement,
                $"The file declared a part or relationship twice; the duplicate was ignored ({first.Message})"));
            return doc;
        }
    }

    /// <summary>Removes duplicate Override entries and duplicate relationships in place. True when something changed.</summary>
    internal static bool RemoveDuplicatePackageEntries(string path)
    {
        var changed = false;
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
        foreach (var entry in zip.Entries.ToList())
        {
            var isTypes = entry.FullName == "[Content_Types].xml";
            if (!isTypes && !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
            System.Xml.Linq.XDocument xml;
            using (var s = entry.Open()) xml = System.Xml.Linq.XDocument.Load(s);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dupes = xml.Root!.Elements()
                .Where(e => !seen.Add(isTypes
                    ? e.Name.LocalName + "|" + ((string?)e.Attribute("PartName") ?? (string?)e.Attribute("Extension") ?? "")
                    : (string?)e.Attribute("Type") + "|" + (string?)e.Attribute("Target")))
                .ToList();
            if (dupes.Count == 0) continue;
            foreach (var d in dupes) d.Remove();
            var name = entry.FullName;
            entry.Delete();
            var replacement = zip.CreateEntry(name);
            using (var w = replacement.Open()) xml.Save(w);
            changed = true;
        }
        return changed;
    }

    // ========================================================================
    // Public/internal methods exposed for detection rules
    // ========================================================================

    /// <summary>
    /// Get the next element order number and increment the counter.
    /// </summary>
    internal int NextElementOrder() => _elementOrder++;

    /// <summary>
    /// Whether formatting should be preserved based on current options.
    /// </summary>
    internal bool ShouldPreserveFormatting() => _options.PreserveFormatting;

    /// <summary>
    /// Analyze a paragraph and extract pre-computed properties for the detection pipeline.
    /// </summary>
    internal ParagraphAnalysis AnalyzeParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        ExtractTextAndFormatting(para, out var text, out var formatting);

        var runs = para.Descendants<Run>().ToList();
        var allBold = runs.Count > 0 && runs.All(r => r.RunProperties?.Bold != null);
        var allItalic = runs.Count > 0 && runs.All(r => r.RunProperties?.Italic != null);

        // Font size from first run
        double? fontSize = null;
        var firstRun = runs.FirstOrDefault();
        if (firstRun?.RunProperties?.FontSize?.Val?.Value is string sizeStr)
        {
            if (double.TryParse(sizeStr, out var halfPoints))
                fontSize = halfPoints / 2.0;
        }

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        // Resolve the language-neutral StyleName (from styles.xml <w:name>).
        // Cheap: amortised O(1) per paragraph after the first lookup.
        var styleName = styleId is null ? null : ResolveStyleName(mainPart, styleId);
        // Launch-bot #76 (12 Nov 2026): Word's "List Number" / "List Bullet" styles (and python-docx)
        // number a paragraph through its style, with no numPr on the paragraph, so the list became
        // plain paragraphs. A non-heading style's numbering now counts; numbered heading styles stay headings.
        var numPr = para.ParagraphProperties?.NumberingProperties
            ?? (styleId is not null && !IsHeadingStyle(styleName, styleId) ? StyleNumberingProperties(mainPart, styleId) : null);
        var mathElements = para.Descendants<M.OfficeMath>().ToList();
        var shading = para.ParagraphProperties?.Shading;

        // Indentation
        var indentLeft = 0;
        var indentVal = para.ParagraphProperties?.Indentation?.Left?.Value;
        if (indentVal != null && int.TryParse(indentVal, out var twips))
        {
            indentLeft = twips;
        }

        // Left border
        var borders = para.ParagraphProperties?.ParagraphBorders;
        var hasLeftBorder = borders?.LeftBorder != null
            && borders.LeftBorder.Val?.Value != BorderValues.None
            && borders.LeftBorder.Val?.Value != BorderValues.Nil;

        return new ParagraphAnalysis
        {
            StyleId = styleId,
            StyleName = styleName,
            Text = text,
            TextOutsideMath = string.Concat(para.Descendants<Text>().Select(t => t.Text)),
            Formatting = formatting,
            FontFamily = GetFontFamily(para),
            FontSizePoints = fontSize,
            AllBold = allBold,
            AllItalic = allItalic,
            AllCaps = !string.IsNullOrEmpty(text) && text == text.ToUpperInvariant() && text.Any(char.IsLetter),
            HasNumberingProperties = numPr != null,
            StyleNumbered = styleId is not null && StyleHasNumbering(mainPart, styleId),
            NumberingProperties = numPr,
            NumberingLevel = (int)(numPr?.NumberingLevelReference?.Val?.Value ?? 0),
            NumberingId = numPr != null ? (int?)numPr.NumberingId?.Val?.Value : null,
            HasMathElements = mathElements.Count > 0,
            MathElements = mathElements,
            HasDrawings = para.Descendants<Drawing>().Any(),
            HasPageBreaks = para.Descendants<Break>().Any(br => br.Type?.Value == BreakValues.Page),
            ShadingFill = shading?.Fill?.Value,
            OutlineLevel = para.ParagraphProperties?.OutlineLevel?.Val?.Value != null
                ? (int?)para.ParagraphProperties.OutlineLevel.Val.Value
                : null,
            IndentLeftTwips = indentLeft,
            HasLeftBorder = hasLeftBorder,
            RawParagraph = para,
            MainDocumentPart = mainPart,
            Runs = runs
        };
    }

    /// <summary>
    /// Create a paragraph ImportElement from a raw paragraph. Used by detection rules.
    /// </summary>
    internal ImportParagraph? CreateParagraphElement(Paragraph para, string? styleId)
    {
        ExtractTextAndFormatting(para, out var text, out var formatting);

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return new ImportParagraph
        {
            Order = _elementOrder++,
            Text = text,
            Formatting = _options.PreserveFormatting ? formatting : [],
            StyleId = styleId,
            Style = GetParagraphStyle(styleId, para)
        };
    }

    /// <summary>
    /// Create a list item ImportElement from a raw paragraph. Used by detection rules.
    /// </summary>
    internal ImportListItem? CreateListItemElement(Paragraph para, NumberingProperties numPr, MainDocumentPart mainPart)
    {
        ExtractTextAndFormatting(para, out var text, out var formatting);

        if (string.IsNullOrWhiteSpace(text))
            return null;

        var level = (int)(numPr.NumberingLevelReference?.Val?.Value ?? 0);
        var numId = numPr.NumberingId?.Val?.Value;
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

    /// <summary>
    /// Create equation ImportElements from a list of OMML math elements. Used by detection rules.
    /// </summary>
    internal IEnumerable<ImportElement> CreateEquationElements(List<M.OfficeMath> mathElements)
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

    /// <summary>
    /// Extract images from a paragraph. Used by detection rules.
    /// </summary>
    internal List<ImportImage> ExtractImagesFromParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        return ExtractImages(para, mainPart);
    }

    // ========================================================================
    // Private parsing methods
    // ========================================================================

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

    /// <summary>
    /// Resolve a Word style's language-neutral name (e.g. "heading 1") from
    /// its localised styleId (e.g. "Titre1" in FR, "berschrift1" in DE).
    /// Word always writes the <w:name> child in English regardless of the
    /// authoring language, so matching on StyleName is language-independent.
    /// Cached per MainDocumentPart.
    /// </summary>
    /// <summary>The list numbering the style (or one it is based on) gives its paragraphs; null when none or numId 0.</summary>
    private static NumberingProperties? StyleNumberingProperties(MainDocumentPart mainPart, string styleId)
    {
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null) return null;
        var id = styleId;
        for (var hop = 0; hop < 6 && id is not null; hop++)
        {
            var style = styles.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
            if (style is null) return null;
            var num = style.StyleParagraphProperties?.NumberingProperties;
            if (num?.NumberingId?.Val?.Value is int n) return n != 0 ? num : null;
            id = style.BasedOn?.Val?.Value;
        }
        return null;
    }

    private static bool IsHeadingStyle(string? styleName, string styleId) =>
        (styleName ?? styleId).StartsWith("heading", StringComparison.OrdinalIgnoreCase)
        || string.Equals(styleName ?? styleId, "title", StringComparison.OrdinalIgnoreCase);

    /// <summary>The style, or one it is based on, carries list numbering (numId other than 0).</summary>
    private static bool StyleHasNumbering(MainDocumentPart mainPart, string styleId)
    {
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null) return false;
        var id = styleId;
        for (var hop = 0; hop < 6 && id is not null; hop++)
        {
            var style = styles.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
            if (style is null) return false;
            var num = style.StyleParagraphProperties?.NumberingProperties;
            if (num?.NumberingId?.Val?.Value is int n) return n != 0;
            id = style.BasedOn?.Val?.Value;
        }
        return false;
    }

    private string? ResolveStyleName(MainDocumentPart mainPart, string styleId)
    {
        if (!_styleNameCache.TryGetValue(mainPart, out var cache))
        {
            cache = BuildStyleNameMap(mainPart);
            _styleNameCache[mainPart] = cache;
        }
        return cache.TryGetValue(styleId, out var name) ? name : null;
    }

    private static Dictionary<string, string> BuildStyleNameMap(MainDocumentPart mainPart)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stylesPart = mainPart.StyleDefinitionsPart?.Styles;
        if (stylesPart is null) return map;
        foreach (var style in stylesPart.Descendants<Style>())
        {
            var id = style.StyleId?.Value;
            var name = style.StyleName?.Val?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                map[id] = name;
        }
        return map;
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
            if (element is Paragraph frontPara && FrontMatterStyle(frontPara, mainPart) is { } front)
            {
                // pandoc's Author and Date paragraphs go to the title block, and its "Abstract" label isn't a section
                // above the abstract (launch-bot #195, 18 Nov 2026).
                ExtractTextAndFormatting(frontPara, out var frontText, out _);
                frontText = frontText.Trim();
                if (front == "author" && frontText.Length > 0) _frontAuthors.Add(frontText);
                else if (front == "date" && frontText.Length > 0) _frontDate = frontText;
                _pipeline?.AddNonParagraphTrace("Paragraph", frontText, 0, "FrontMatter:" + front);
                continue;
            }
            if (element is Paragraph para)
            {
                // Paragraphs are traced by the pipeline's Evaluate method
                var parsed = ParseParagraph(para, mainPart);
                if (parsed != null)
                {
                    var list = parsed.ToList();
                    elements.AddRange(list);
                }
            }
            else if (element is Table table)
            {
                var parsed = ParseTable(table, mainPart);
                var list = parsed?.ToList();
                _pipeline?.AddNonParagraphTrace("Table", $"[Table with {table.Descendants<TableRow>().Count()} rows]", list?.Count ?? 0, "Table");
                if (list != null)
                    elements.AddRange(list);
            }
            else if (element is SdtBlock sdt)
            {
                var parsed = ParseSdtBlock(sdt, mainPart);
                var list = parsed?.ToList();
                var sdtType = sdt.SdtProperties?.GetFirstChild<SdtContentDocPartObject>()?.DocPartGallery?.Val?.Value ?? "SdtBlock";
                _pipeline?.AddNonParagraphTrace("SdtBlock", $"[{sdtType}]", list?.Count ?? 0, sdtType);
                if (list != null)
                    elements.AddRange(list);
            }
            else
            {
                _pipeline?.AddNonParagraphTrace(element.GetType().Name, string.Empty, 0, "Skipped");
            }
        }

        PairCaptions(elements);
        return elements;
    }

    private readonly List<string> _frontAuthors = [];
    private string? _frontDate;

    private string? FrontMatterStyle(Paragraph para, MainDocumentPart mainPart)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId is null) return null;
        var name = ResolveStyleName(mainPart, styleId) ?? styleId;
        foreach (var candidate in new[] { styleId, name })
        {
            if (string.Equals(candidate, "Author", StringComparison.OrdinalIgnoreCase)) return "author";
            if (string.Equals(candidate, "Date", StringComparison.OrdinalIgnoreCase)) return "date";
            if (string.Equals(candidate, "AbstractTitle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, "Abstract Title", StringComparison.OrdinalIgnoreCase)) return "abstractTitle";
        }
        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex TableCaptionText = new(
        @"^\s*(Table|Tableau|Tabla|Tabelle)\s*\d+\s*[.:]\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex FigureCaptionText = new(
        @"^\s*(Figure|Fig\.?|Figura|Abbildung|Abb\.)\s*\d+\s*[.:]\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// A caption paragraph next to its table or image is that float's caption, said once: the Word caption stayed a body
    /// paragraph, so a figure's caption printed twice and "Figure 1: Figure 1. …", and a table had none (launch-bot #189,
    /// #190, 18 Nov 2026). A table's caption is the paragraph before it (Word's and pandoc's place) or after it; an
    /// image's, the paragraph after it. Caption style, or text starting "Table 3." / "Figure 2:".
    /// </summary>
    internal static void PairCaptions(List<ImportElement> elements)
    {
        static bool IsCaption(ImportElement? e, System.Text.RegularExpressions.Regex label) =>
            e is ImportParagraph p && !string.IsNullOrWhiteSpace(p.Text)
            && (p.Style == ParagraphStyle.Caption || label.IsMatch(p.Text));
        static string Clean(string text, System.Text.RegularExpressions.Regex label) => label.Replace(text.Trim(), "", 1).Trim();

        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] is ImportTable table && string.IsNullOrWhiteSpace(table.Caption))
            {
                var before = i > 0 ? elements[i - 1] : null;
                var after = i + 1 < elements.Count ? elements[i + 1] : null;
                if (IsCaption(before, TableCaptionText))
                {
                    table.Caption = Clean(((ImportParagraph)before!).Text, TableCaptionText);
                    elements.RemoveAt(i - 1);
                    i--;
                }
                else if (IsCaption(after, TableCaptionText) && !FigureCaptionText.IsMatch(((ImportParagraph)after!).Text))
                {
                    table.Caption = Clean(((ImportParagraph)after).Text, TableCaptionText);
                    elements.RemoveAt(i + 1);
                }
            }
            else if (elements[i] is ImportImage image && i + 1 < elements.Count && IsCaption(elements[i + 1], FigureCaptionText)
                     && !TableCaptionText.IsMatch(((ImportParagraph)elements[i + 1]).Text))
            {
                image.Caption = Clean(((ImportParagraph)elements[i + 1]).Text, FigureCaptionText);
                elements.RemoveAt(i + 1);
            }
        }
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

    /// <summary>
    /// Parse a paragraph using the detection pipeline.
    /// </summary>
    private IEnumerable<ImportElement>? ParseParagraph(Paragraph para, MainDocumentPart mainPart)
    {
        var analysis = AnalyzeParagraph(para, mainPart);
        return _pipeline!.Evaluate(analysis, this);
    }

    // ========================================================================
    // Internal helper methods (used by detection rules via DocxParser reference)
    // ========================================================================

    /// <summary>
    /// A paragraph's text in reading order, with its inline LaTeX (launch-bot #187–#197, 18 Nov 2026). Before, only the
    /// runs' w:t were read, so a paragraph lost its inline equations ($…$), its links (\href), its footnotes (\footnote)
    /// and its line breaks (a code block's lines ran into one).
    /// </summary>
    internal void ExtractTextAndFormatting(Paragraph para, out string text, out List<FormattingSpan> formatting)
    {
        var sb = new System.Text.StringBuilder();
        var spans = new List<FormattingSpan>();
        WalkInline(para, sb, spans, _currentMainPart, depth: 0);
        formatting = spans;
        text = sb.ToString();
    }

    private MainDocumentPart? _currentMainPart;

    private void WalkInline(OpenXmlElement container, System.Text.StringBuilder sb, List<FormattingSpan> formatting, MainDocumentPart? mainPart, int depth)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    AppendRun(run, sb, formatting, mainPart, depth);
                    break;
                case DeletedRun:
                    break; // a tracked deletion is not in the text
                case Hyperlink link:
                    AppendHyperlink(link, sb, formatting, mainPart, depth);
                    break;
                case M.OfficeMath math:
                    AppendInlineMath(math.OuterXml, sb);
                    break;
                case M.Paragraph mathPara:
                    foreach (var m in mathPara.Elements<M.OfficeMath>()) AppendInlineMath(m.OuterXml, sb);
                    break;
                case ParagraphProperties:
                    break;
                default:
                    if (child.HasChildren) WalkInline(child, sb, formatting, mainPart, depth);
                    break;
            }
        }
    }

    private void AppendInlineMath(string ommlXml, System.Text.StringBuilder sb)
    {
        var latex = "";
        if (_ommlConverter is not null && _ommlConverter.Convert(ommlXml) is (var converted, true, _)) latex = converted.Trim();
        // Without a converter (or when it fails), the equation's own characters, still as maths.
        if (latex.Length == 0)
            latex = string.Concat(System.Xml.Linq.XElement.Parse(ommlXml).Descendants()
                .Where(e => e.Name.LocalName == "t").Select(e => e.Value)).Trim();
        if (latex.Length > 0) sb.Append('$').Append(latex).Append('$');
    }

    // A link keeps its target: \href{url}{text}, or \url{url} when the text is the address (launch-bot #197).
    private void AppendHyperlink(Hyperlink link, System.Text.StringBuilder sb, List<FormattingSpan> formatting, MainDocumentPart? mainPart, int depth)
    {
        var id = link.Id?.Value;
        var url = id is null || mainPart is null ? null
            : mainPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri?.OriginalString;
        if (string.IsNullOrWhiteSpace(url))
        {
            WalkInline(link, sb, formatting, mainPart, depth);
            return;
        }
        var escaped = url.Replace("\\", "/").Replace("%", "\\%").Replace("#", "\\#").Replace("{", "%7B").Replace("}", "%7D");
        var linkText = string.Concat(link.Descendants<Text>().Select(t => t.Text)).Trim();
        if (linkText.Length == 0 || string.Equals(linkText, url.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals("mailto:" + linkText, url.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("\\url{").Append(escaped).Append('}');
            return;
        }
        sb.Append("\\href{").Append(escaped).Append("}{");
        WalkInline(link, sb, formatting, mainPart, depth);
        sb.Append('}');
    }

    // A footnote's text at its mark: \footnote{…} (launch-bot #188). Word kept the note in footnotes.xml, and the
    // reference was dropped.
    private void AppendNote(OpenXmlElement noteRoot, System.Text.StringBuilder sb, MainDocumentPart? mainPart, int depth)
    {
        if (depth > 2) return;
        var inner = new System.Text.StringBuilder();
        var ignored = new List<FormattingSpan>();
        foreach (var p in noteRoot.Elements<Paragraph>())
        {
            if (inner.Length > 0) inner.Append(' ');
            WalkInline(p, inner, ignored, mainPart, depth + 1);
        }
        var note = inner.ToString().Trim();
        if (note.Length > 0) sb.Append("\\footnote{").Append(note).Append('}');
    }

    private void AppendRun(Run run, System.Text.StringBuilder sb, List<FormattingSpan> formatting, MainDocumentPart? mainPart, int depth)
    {
        var startIndex = sb.Length;
        foreach (var part in run.ChildElements)
        {
            switch (part)
            {
                case Text t:
                    sb.Append(t.Text);
                    break;
                // A line break is a new line: a code block's lines stayed apart (launch-bot #192). Page and column breaks
                // are read elsewhere.
                case Break br when br.Type is null || br.Type.Value == BreakValues.TextWrapping:
                    sb.Append('\n');
                    break;
                case TabChar:
                    sb.Append(' ');
                    break;
                case FootnoteReference fr when mainPart?.FootnotesPart?.Footnotes is { } notes:
                    if (notes.Elements<Footnote>().FirstOrDefault(n => n.Id?.Value == fr.Id?.Value) is { } footnote)
                        AppendNote(footnote, sb, mainPart, depth);
                    break;
                case EndnoteReference er when mainPart?.EndnotesPart?.Endnotes is { } endnotes:
                    if (endnotes.Elements<Endnote>().FirstOrDefault(n => n.Id?.Value == er.Id?.Value) is { } endnote)
                        AppendNote(endnote, sb, mainPart, depth);
                    break;
            }
        }
        var runText = sb.ToString(startIndex, sb.Length - startIndex);

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

    internal bool IsNumberedList(int? numId, int level, MainDocumentPart mainPart)
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

    internal string? GetListMarker(int? numId, int level, MainDocumentPart mainPart)
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

    internal string? GetFontFamily(Paragraph para)
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

    internal static bool IsCodeShading(string fillColor)
    {
        // Check for gray shades commonly used for code blocks
        if (fillColor.Length == 6 && fillColor.All(c => char.IsLetterOrDigit(c)))
        {
            try
            {
                var r = Convert.ToInt32(fillColor.Substring(0, 2), 16);
                var g = Convert.ToInt32(fillColor.Substring(2, 2), 16);
                var b = Convert.ToInt32(fillColor.Substring(4, 2), 16);

                // Exclude white and near-white backgrounds
                if (r >= 250 && g >= 250 && b >= 250)
                    return false;

                // Check if it's a gray color (R ≈ G ≈ B) and light (> 180 but < 250)
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

    // Page sizes in twentieths of a point (portrait), as Lilia names them.
    private static readonly (string Name, int W, int H)[] PageSizes =
    [
        ("a4", 11906, 16838), ("letter", 12240, 15840), ("legal", 12240, 20160),
        ("a5", 8391, 11906), ("executive", 10440, 15120), ("b5", 9979, 14173),
    ];

    /// <summary>
    /// The page the Word file is set on: its last section's size and margins (MCP-124: a US Letter
    /// file with 1 in and 1.25 in margins came in as A4 with the class's margins). A size Lilia has
    /// no name for keeps the document's default.
    /// </summary>
    internal static void ReadPageSetup(Body body, ImportMetadata metadata)
    {
        var section = body.Elements<SectionProperties>().LastOrDefault() ?? body.Descendants<SectionProperties>().LastOrDefault();
        if (section is null) return;
        if (section.GetFirstChild<PageSize>() is { Width.HasValue: true, Height.HasValue: true } size)
        {
            int w = (int)size.Width!.Value, h = (int)size.Height!.Value;
            if (w > h) (w, h) = (h, w);
            var named = PageSizes.FirstOrDefault(p => Math.Abs(p.W - w) <= 30 && Math.Abs(p.H - h) <= 30);
            if (named.Name is not null) metadata.PaperSize = named.Name;
        }
        if (section.GetFirstChild<PageMargin>() is { } m)
        {
            metadata.MarginTop = Length(m.Top?.Value);
            metadata.MarginBottom = Length(m.Bottom?.Value);
            metadata.MarginLeft = Length(m.Left?.Value is uint l ? (int)l : null);
            metadata.MarginRight = Length(m.Right?.Value is uint r ? (int)r : null);
        }
    }

    // Twentieths of a point as Lilia writes a margin: inches when a whole quarter inch, else centimetres.
    internal static string? Length(int? twips)
    {
        if (twips is not int t || t <= 0) return null;
        if (t % 360 == 0) return $"{(t / 1440m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}in";
        return $"{(t * 2.54m / 1440m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}cm";
    }

    internal ParagraphStyle GetParagraphStyle(string? styleId, Paragraph para)
    {
        if (string.IsNullOrEmpty(styleId))
            return ParagraphStyle.Normal;

        var lower = styleId.ToLowerInvariant();

        if (lower.Contains("quote") || lower.Contains("blockquote"))
            return ParagraphStyle.Quote;
        // Subtitle first: "subtitle" contains "title", so a Subtitle paragraph read as the Title.
        if (lower.Contains("subtitle"))
            return ParagraphStyle.Subtitle;
        if (lower.Contains("title"))
            return ParagraphStyle.Title;
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

    /// <summary>
    /// A vertical merge's first cell spans the cells marked <see cref="ImportTableCell.MergedAbove"/> under it, found by
    /// grid column (gridSpan counted).
    /// </summary>
    internal static void CountRowSpans(List<List<ImportTableCell>> rows)
    {
        static int GridColumn(List<ImportTableCell> row, int index) => row.Take(index).Sum(c => Math.Max(1, c.ColSpan));
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = rows[r][c];
                if (cell.MergedAbove) continue;
                var col = GridColumn(rows[r], c);
                var span = 1;
                for (var below = r + 1; below < rows.Count; below++)
                {
                    var under = rows[below].Select((x, i) => (x, i)).FirstOrDefault(t => GridColumn(rows[below], t.i) == col).x;
                    if (under is null || !under.MergedAbove) break;
                    span++;
                }
                cell.RowSpan = span;
            }
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
                // Launch-bot #76: a table whose style formats the first row as a header (tblLook
                // firstRow, Word's default for an inserted table) has its own headers, not "Column 1".
                var look = table.GetFirstChild<TableProperties>()?.GetFirstChild<TableLook>();
                var lookFirstRow = look?.FirstRow?.Value == true
                    || (look?.Val?.Value is string hex && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var bits) && (bits & 0x0020) != 0);
                importTable.HasHeaderRow = headerProp != null || (lookFirstRow && table.Elements<TableRow>().Count() > 1);
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

                    // w:vMerge without a value continues the merge above (launch-bot #189: merges were lost).
                    var vMerge = cell.TableCellProperties?.VerticalMerge;
                    if (vMerge != null && vMerge.Val?.Value != MergedCellValues.Restart)
                        importCell.MergedAbove = true;

                    importRow.Add(importCell);
                }

                importTable.Rows.Add(importRow);
            }
            CountRowSpans(importTable.Rows);

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
