namespace Lilia.Import.Models;

/// <summary>
/// Base class for all elements extracted from a DOCX document.
/// </summary>
public abstract class ImportElement
{
    /// <summary>
    /// Order of this element in the document (0-based).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Type of the import element.
    /// </summary>
    public abstract ImportElementType Type { get; }
}

/// <summary>
/// A heading element (H1-H9) that will become a Section in Lilia.
/// </summary>
public class ImportHeading : ImportElement
{
    public override ImportElementType Type => ImportElementType.Heading;

    /// <summary>
    /// Heading level (1-9, where 1 is the highest level).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Plain text content of the heading.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the heading text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];

    /// <summary>
    /// Original style ID from the DOCX (e.g., "Heading1", "Heading2").
    /// </summary>
    public string? StyleId { get; set; }
}

/// <summary>
/// A paragraph element that will become a Paragraph block in Lilia.
/// </summary>
public class ImportParagraph : ImportElement
{
    public override ImportElementType Type => ImportElementType.Paragraph;

    /// <summary>
    /// Plain text content of the paragraph.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the paragraph text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];

    /// <summary>
    /// Style classification of the paragraph.
    /// </summary>
    public ParagraphStyle Style { get; set; } = ParagraphStyle.Normal;

    /// <summary>
    /// Original style ID from the DOCX.
    /// </summary>
    public string? StyleId { get; set; }

    /// <summary>
    /// Indentation level (for lists or nested content).
    /// </summary>
    public int IndentLevel { get; set; }
}

/// <summary>
/// A math equation element (OMML) that will become an Equation block in Lilia.
/// </summary>
public class ImportEquation : ImportElement
{
    public override ImportElementType Type => ImportElementType.Equation;

    /// <summary>
    /// Original OMML XML content.
    /// </summary>
    public string OmmlXml { get; set; } = string.Empty;

    /// <summary>
    /// Converted LaTeX content (if conversion succeeded).
    /// </summary>
    public string? LatexContent { get; set; }

    /// <summary>
    /// Whether the OMML to LaTeX conversion was successful.
    /// </summary>
    public bool ConversionSucceeded { get; set; }

    /// <summary>
    /// Error message if conversion failed.
    /// </summary>
    public string? ConversionError { get; set; }

    /// <summary>
    /// Whether this is an inline equation (within text) or display equation (on its own line).
    /// </summary>
    public bool IsInline { get; set; }
}

/// <summary>
/// A code block element detected by style, font, or shading.
/// </summary>
public class ImportCodeBlock : ImportElement
{
    public override ImportElementType Type => ImportElementType.CodeBlock;

    /// <summary>
    /// Plain text content of the code block.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// How this code block was detected.
    /// </summary>
    public CodeBlockDetectionReason DetectionReason { get; set; }

    /// <summary>
    /// Programming language (if detectable from style or content).
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Original style ID that triggered code detection.
    /// </summary>
    public string? StyleId { get; set; }

    /// <summary>
    /// Font family used (if detected by font).
    /// </summary>
    public string? FontFamily { get; set; }
}

/// <summary>
/// A table element that will become a Table block in Lilia.
/// </summary>
public class ImportTable : ImportElement
{
    public override ImportElementType Type => ImportElementType.Table;

    /// <summary>
    /// Table rows, each containing a list of cells.
    /// </summary>
    public List<List<ImportTableCell>> Rows { get; set; } = [];

    /// <summary>
    /// Number of columns in the table.
    /// </summary>
    public int ColumnCount => Rows.Count > 0 ? Rows[0].Count : 0;

    /// <summary>
    /// Number of rows in the table.
    /// </summary>
    public int RowCount => Rows.Count;

    /// <summary>
    /// Whether the first row is a header row.
    /// </summary>
    public bool HasHeaderRow { get; set; }
}

/// <summary>
/// A cell within an ImportTable.
/// </summary>
public class ImportTableCell
{
    /// <summary>
    /// Plain text content of the cell.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the cell text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];

    /// <summary>
    /// Column span (for merged cells).
    /// </summary>
    public int ColSpan { get; set; } = 1;

    /// <summary>
    /// Row span (for merged cells).
    /// </summary>
    public int RowSpan { get; set; } = 1;
}

/// <summary>
/// An image element that will become a Figure block with an Asset in Lilia.
/// </summary>
public class ImportImage : ImportElement
{
    public override ImportElementType Type => ImportElementType.Image;

    /// <summary>
    /// Binary image data.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// MIME type of the image (e.g., "image/png", "image/jpeg").
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Original filename (if available).
    /// </summary>
    public string? Filename { get; set; }

    /// <summary>
    /// Alt text / description.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Width in EMUs (English Metric Units) or null if not specified.
    /// </summary>
    public long? WidthEmu { get; set; }

    /// <summary>
    /// Height in EMUs (English Metric Units) or null if not specified.
    /// </summary>
    public long? HeightEmu { get; set; }

    /// <summary>
    /// Width in pixels (converted from EMUs, 1 inch = 914400 EMUs, 96 DPI).
    /// </summary>
    public double? WidthPixels => WidthEmu.HasValue ? WidthEmu.Value / 914400.0 * 96 : null;

    /// <summary>
    /// Height in pixels (converted from EMUs).
    /// </summary>
    public double? HeightPixels => HeightEmu.HasValue ? HeightEmu.Value / 914400.0 * 96 : null;

    /// <summary>
    /// Relationship ID in the DOCX package.
    /// </summary>
    public string? RelationshipId { get; set; }
}

/// <summary>
/// A list item element (bullet or numbered).
/// </summary>
public class ImportListItem : ImportElement
{
    public override ImportElementType Type => ImportElementType.ListItem;

    /// <summary>
    /// Plain text content of the list item.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the list item text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];

    /// <summary>
    /// Nesting level (0 = top level, 1 = sub-item, etc.).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Whether this is a numbered list item (vs bullet).
    /// </summary>
    public bool IsNumbered { get; set; }

    /// <summary>
    /// List number/bullet text (e.g., "1.", "a)", "•").
    /// </summary>
    public string? ListMarker { get; set; }
}

/// <summary>
/// A header element from the document.
/// </summary>
public class ImportHeader : ImportElement
{
    public override ImportElementType Type => ImportElementType.Header;

    /// <summary>
    /// Header type (default, first, even).
    /// </summary>
    public HeaderFooterType HeaderType { get; set; } = HeaderFooterType.Default;

    /// <summary>
    /// Plain text content of the header.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the header text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];
}

/// <summary>
/// A footer element from the document.
/// </summary>
public class ImportFooter : ImportElement
{
    public override ImportElementType Type => ImportElementType.Footer;

    /// <summary>
    /// Footer type (default, first, even).
    /// </summary>
    public FooterType FooterType { get; set; } = FooterType.Default;

    /// <summary>
    /// Plain text content of the footer.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the footer text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];
}

/// <summary>
/// A footnote element from the document.
/// </summary>
public class ImportFootnote : ImportElement
{
    public override ImportElementType Type => ImportElementType.Footnote;

    /// <summary>
    /// Footnote ID.
    /// </summary>
    public int NoteId { get; set; }

    /// <summary>
    /// Plain text content of the footnote.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the footnote text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];
}

/// <summary>
/// An endnote element from the document.
/// </summary>
public class ImportEndnote : ImportElement
{
    public override ImportElementType Type => ImportElementType.Endnote;

    /// <summary>
    /// Endnote ID.
    /// </summary>
    public int NoteId { get; set; }

    /// <summary>
    /// Plain text content of the endnote.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formatting spans within the endnote text.
    /// </summary>
    public List<FormattingSpan> Formatting { get; set; } = [];
}

/// <summary>
/// Header type in DOCX.
/// </summary>
public enum HeaderFooterType
{
    Default,  // Used for most pages
    First,    // Used for first page only
    Even      // Used for even pages (when different)
}

/// <summary>
/// Footer type in DOCX.
/// </summary>
public enum FooterType
{
    Default,
    First,
    Even
}

/// <summary>
/// A Table of Contents element from the document.
/// </summary>
public class ImportTableOfContents : ImportElement
{
    public override ImportElementType Type => ImportElementType.TableOfContents;

    /// <summary>
    /// Title of the TOC (e.g., "Table of Contents", "Contents").
    /// </summary>
    public string Title { get; set; } = "Table of Contents";

    /// <summary>
    /// Entries in the TOC.
    /// </summary>
    public List<TocEntry> Entries { get; set; } = [];

    /// <summary>
    /// Raw field code text (e.g., "TOC \\o \"1-3\"").
    /// </summary>
    public string? FieldCode { get; set; }
}

/// <summary>
/// An entry in a Table of Contents.
/// </summary>
public class TocEntry
{
    /// <summary>
    /// Text of the entry.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Heading level (1-9).
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Page number (if available).
    /// </summary>
    public string? PageNumber { get; set; }
}

/// <summary>
/// A comment element from the document.
/// </summary>
public class ImportComment : ImportElement
{
    public override ImportElementType Type => ImportElementType.Comment;

    /// <summary>
    /// Comment ID.
    /// </summary>
    public int CommentId { get; set; }

    /// <summary>
    /// Author of the comment.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Date/time the comment was created.
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// Initials of the author.
    /// </summary>
    public string? Initials { get; set; }

    /// <summary>
    /// Plain text content of the comment.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The text that the comment is anchored to (if extractable).
    /// </summary>
    public string? AnchoredText { get; set; }
}

/// <summary>
/// A tracked change (revision) from the document.
/// </summary>
public class ImportTrackChange : ImportElement
{
    public override ImportElementType Type => ImportElementType.TrackChange;

    /// <summary>
    /// Type of change (insertion or deletion).
    /// </summary>
    public TrackChangeType ChangeType { get; set; }

    /// <summary>
    /// Author who made the change.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Date/time the change was made.
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// The changed text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Revision ID.
    /// </summary>
    public int RevisionId { get; set; }
}

/// <summary>
/// Type of track change.
/// </summary>
public enum TrackChangeType
{
    Insertion,
    Deletion
}

/// <summary>
/// A page break element that forces content to start on a new page.
/// </summary>
public class ImportPageBreak : ImportElement
{
    public override ImportElementType Type => ImportElementType.PageBreak;
}
