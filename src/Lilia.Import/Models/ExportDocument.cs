namespace Lilia.Import.Models;

/// <summary>
/// Document structure for DOCX export.
/// This is the input format for the DocxExportService.
/// </summary>
public class ExportDocument
{
    /// <summary>
    /// Document title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Document author.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Document language code (e.g., "en", "fr").
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Paper size (a4, letter, legal).
    /// </summary>
    public string PaperSize { get; set; } = "a4";

    /// <summary>
    /// Default font family.
    /// </summary>
    public string FontFamily { get; set; } = "Charter";

    /// <summary>
    /// Default font size in points.
    /// </summary>
    public int FontSize { get; set; } = 11;

    /// <summary>
    /// Document blocks in order.
    /// </summary>
    public List<ExportBlock> Blocks { get; set; } = [];

    /// <summary>
    /// Bibliography entries (optional).
    /// </summary>
    public List<ExportBibliographyEntry>? Bibliography { get; set; }

    /// <summary>
    /// Document metadata.
    /// </summary>
    public ExportMetadata? Metadata { get; set; }
}

/// <summary>
/// A block element for export.
/// </summary>
public class ExportBlock
{
    /// <summary>
    /// Block type: paragraph, heading, equation, code, table, figure, list,
    /// blockquote, theorem, abstract, header, footer, footnote, endnote,
    /// tableOfContents, comment.
    /// </summary>
    public string Type { get; set; } = "paragraph";

    /// <summary>
    /// Block content (varies by type).
    /// </summary>
    public ExportBlockContent Content { get; set; } = new();
}

/// <summary>
/// Content of an export block.
/// </summary>
public class ExportBlockContent
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Rich text with formatting spans.
    /// </summary>
    public List<ExportRichTextSpan>? RichText { get; set; }

    /// <summary>
    /// Heading level (1-6).
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// LaTeX content for equations.
    /// </summary>
    public string? Latex { get; set; }

    /// <summary>
    /// Whether equation is display mode (true) or inline (false).
    /// </summary>
    public bool? DisplayMode { get; set; }

    /// <summary>
    /// Code content.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Programming language for code blocks.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Table rows.
    /// </summary>
    public List<List<ExportTableCell>>? Rows { get; set; }

    /// <summary>
    /// Whether table has a header row.
    /// </summary>
    public bool? HasHeader { get; set; }

    /// <summary>
    /// Image data for figures.
    /// </summary>
    public ExportImageData? Image { get; set; }

    /// <summary>
    /// Caption for figures/tables.
    /// </summary>
    public string? Caption { get; set; }

    /// <summary>
    /// List items.
    /// </summary>
    public List<ExportListItem>? Items { get; set; }

    /// <summary>
    /// List type: "bullet" or "ordered".
    /// </summary>
    public string? ListType { get; set; }

    /// <summary>
    /// Theorem type for theorem blocks (theorem, lemma, proposition, corollary, definition, remark, example).
    /// </summary>
    public string? TheoremType { get; set; }

    /// <summary>
    /// Theorem number.
    /// </summary>
    public int? TheoremNumber { get; set; }

    /// <summary>
    /// Header type: default, first, even.
    /// </summary>
    public string? HeaderType { get; set; }

    /// <summary>
    /// Footer type: default, first, even.
    /// </summary>
    public string? FooterType { get; set; }

    /// <summary>
    /// Note ID for footnotes/endnotes.
    /// </summary>
    public int? NoteId { get; set; }

    /// <summary>
    /// Table of contents entries.
    /// </summary>
    public List<ExportTocEntry>? TocEntries { get; set; }

    /// <summary>
    /// Comment ID.
    /// </summary>
    public int? CommentId { get; set; }

    /// <summary>
    /// Author for comments.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Date for comments.
    /// </summary>
    public DateTime? Date { get; set; }
}

/// <summary>
/// Rich text span with formatting.
/// </summary>
public class ExportRichTextSpan
{
    /// <summary>
    /// Text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Bold formatting.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Italic formatting.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Underline formatting.
    /// </summary>
    public bool Underline { get; set; }

    /// <summary>
    /// Strikethrough formatting.
    /// </summary>
    public bool Strikethrough { get; set; }

    /// <summary>
    /// Superscript formatting.
    /// </summary>
    public bool Superscript { get; set; }

    /// <summary>
    /// Subscript formatting.
    /// </summary>
    public bool Subscript { get; set; }

    /// <summary>
    /// Inline equation LaTeX.
    /// </summary>
    public string? Equation { get; set; }

    /// <summary>
    /// Font color (hex, e.g., "#FF0000").
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Font size (e.g., "12pt").
    /// </summary>
    public string? FontSize { get; set; }

    /// <summary>
    /// Font family.
    /// </summary>
    public string? FontFamily { get; set; }

    /// <summary>
    /// Highlight/background color.
    /// </summary>
    public string? Highlight { get; set; }

    /// <summary>
    /// Hyperlink URL.
    /// </summary>
    public string? Link { get; set; }
}

/// <summary>
/// Table cell for export.
/// </summary>
public class ExportTableCell
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Rich text content.
    /// </summary>
    public List<ExportRichTextSpan>? RichText { get; set; }

    /// <summary>
    /// Column span (default 1).
    /// </summary>
    public int ColSpan { get; set; } = 1;

    /// <summary>
    /// Row span (default 1).
    /// </summary>
    public int RowSpan { get; set; } = 1;
}

/// <summary>
/// Image data for export.
/// </summary>
public class ExportImageData
{
    /// <summary>
    /// Base64-encoded image data.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// MIME type (e.g., "image/png").
    /// </summary>
    public string MimeType { get; set; } = "image/png";

    /// <summary>
    /// Original filename.
    /// </summary>
    public string? Filename { get; set; }

    /// <summary>
    /// Alt text for accessibility.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Width in pixels.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Height in pixels.
    /// </summary>
    public double? Height { get; set; }
}

/// <summary>
/// List item for export.
/// </summary>
public class ExportListItem
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Rich text content.
    /// </summary>
    public List<ExportRichTextSpan>? RichText { get; set; }

    /// <summary>
    /// Nesting level (0-based).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Child items for nested lists.
    /// </summary>
    public List<ExportListItem>? Children { get; set; }
}

/// <summary>
/// Table of contents entry.
/// </summary>
public class ExportTocEntry
{
    /// <summary>
    /// Entry text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Heading level.
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Page number (if known).
    /// </summary>
    public string? PageNumber { get; set; }
}

/// <summary>
/// Bibliography entry for export.
/// </summary>
public class ExportBibliographyEntry
{
    /// <summary>
    /// Citation key.
    /// </summary>
    public string CiteKey { get; set; } = string.Empty;

    /// <summary>
    /// Entry type (article, book, etc.).
    /// </summary>
    public string EntryType { get; set; } = "article";

    /// <summary>
    /// BibTeX fields.
    /// </summary>
    public Dictionary<string, string> Fields { get; set; } = [];
}

/// <summary>
/// Export metadata.
/// </summary>
public class ExportMetadata
{
    /// <summary>
    /// Document author.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Document subject.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Keywords.
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// Creation date.
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Last modified date.
    /// </summary>
    public DateTime? Modified { get; set; }
}

/// <summary>
/// Options for DOCX export.
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// Default export options.
    /// </summary>
    public static ExportOptions Default { get; } = new();

    /// <summary>
    /// Include table of contents.
    /// </summary>
    public bool IncludeTableOfContents { get; set; } = false;

    /// <summary>
    /// Include page numbers in footer.
    /// </summary>
    public bool IncludePageNumbers { get; set; } = true;

    /// <summary>
    /// Include bibliography section.
    /// </summary>
    public bool IncludeBibliography { get; set; } = true;

    /// <summary>
    /// Template path (optional, for custom templates).
    /// </summary>
    public string? TemplatePath { get; set; }

    /// <summary>
    /// Page margins in inches.
    /// </summary>
    public ExportMargins Margins { get; set; } = new();

    /// <summary>
    /// Line spacing (1.0, 1.5, 2.0).
    /// </summary>
    public double LineSpacing { get; set; } = 1.15;

    /// <summary>
    /// Whether to embed fonts.
    /// </summary>
    public bool EmbedFonts { get; set; } = false;

    /// <summary>
    /// Page width in twentieths of a point (A4 = 11906).
    /// </summary>
    public uint PageWidth { get; set; } = 11906; // A4 width

    /// <summary>
    /// Page height in twentieths of a point (A4 = 16838).
    /// </summary>
    public uint PageHeight { get; set; } = 16838; // A4 height

    /// <summary>
    /// Top margin in twentieths of a point.
    /// </summary>
    public int MarginTop { get; set; } = 1440; // 1 inch

    /// <summary>
    /// Bottom margin in twentieths of a point.
    /// </summary>
    public int MarginBottom { get; set; } = 1440; // 1 inch

    /// <summary>
    /// Left margin in twentieths of a point.
    /// </summary>
    public uint MarginLeft { get; set; } = 1440; // 1 inch

    /// <summary>
    /// Right margin in twentieths of a point.
    /// </summary>
    public uint MarginRight { get; set; } = 1440; // 1 inch
}

/// <summary>
/// Page margins for export.
/// </summary>
public class ExportMargins
{
    /// <summary>
    /// Top margin in inches.
    /// </summary>
    public double Top { get; set; } = 1.0;

    /// <summary>
    /// Bottom margin in inches.
    /// </summary>
    public double Bottom { get; set; } = 1.0;

    /// <summary>
    /// Left margin in inches.
    /// </summary>
    public double Left { get; set; } = 1.0;

    /// <summary>
    /// Right margin in inches.
    /// </summary>
    public double Right { get; set; } = 1.0;
}
