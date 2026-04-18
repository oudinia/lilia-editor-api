namespace Lilia.Import.Models;

/// <summary>
/// The intermediate representation of a DOCX document after parsing.
/// Contains all extracted elements with their formatting metadata,
/// ready for conversion to Lilia's native format.
/// </summary>
public class ImportDocument
{
    /// <summary>
    /// Path to the source DOCX file.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Document title (extracted from document properties or first heading).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All elements in document order.
    /// </summary>
    public List<ImportElement> Elements { get; set; } = [];

    /// <summary>
    /// Warnings generated during parsing.
    /// </summary>
    public List<ImportWarning> Warnings { get; set; } = [];

    /// <summary>
    /// Document metadata.
    /// </summary>
    public ImportMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Raw output from the import engine (e.g. Mathpix markdown).
    /// Stored for debugging and integration testing without re-calling external APIs.
    /// </summary>
    public string? RawImportData { get; set; }

    /// <summary>
    /// Paragraph-level diagnostic trace of the entire document body.
    /// Each entry records what happened to a body element (matched rule, detected type, raw text).
    /// </summary>
    public List<ParagraphTraceEntry> ParagraphTraces { get; set; } = [];

    /// <summary>
    /// Get all elements of a specific type.
    /// </summary>
    public IEnumerable<T> GetElements<T>() where T : ImportElement
    {
        return Elements.OfType<T>();
    }

    /// <summary>
    /// Get count of elements by type.
    /// </summary>
    public Dictionary<ImportElementType, int> GetElementCounts()
    {
        return Elements
            .GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Check if there are any warnings of a specific type.
    /// </summary>
    public bool HasWarnings(ImportWarningType type)
    {
        return Warnings.Any(w => w.Type == type);
    }

    /// <summary>
    /// Get a summary of the imported document.
    /// </summary>
    public string GetSummary()
    {
        var counts = GetElementCounts();
        var parts = counts.Select(kv => $"{kv.Value} {kv.Key}(s)");
        var warningCount = Warnings.Count;
        return $"Imported: {string.Join(", ", parts)}. Warnings: {warningCount}";
    }
}

/// <summary>
/// Metadata extracted from the DOCX document properties.
/// </summary>
public class ImportMetadata
{
    /// <summary>
    /// Document author (dc:creator).
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Document subject (dc:subject).
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Document description (dc:description).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Keywords (cp:keywords).
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// Created date (dcterms:created).
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Modified date (dcterms:modified).
    /// </summary>
    public DateTime? Modified { get; set; }

    /// <summary>
    /// Application that created the document (e.g., "Microsoft Office Word", "Google Docs").
    /// </summary>
    public string? Application { get; set; }

    /// <summary>
    /// Application version.
    /// </summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Whether this appears to be a Google Docs export.
    /// </summary>
    public bool IsGoogleDocsExport =>
        Application?.Contains("Google", StringComparison.OrdinalIgnoreCase) == true;

    // --- CV-style personal info captured from \name, \email, \phone etc.
    // in the preamble. Kept optional so non-CV imports stay unaffected.

    /// <summary>Combined display name from \name{first}{last}.</summary>
    public string? PersonName { get; set; }

    /// <summary>Primary email from \email{...}.</summary>
    public string? Email { get; set; }

    /// <summary>Phone numbers from \phone[type]{number} — kind is "mobile", "fixed", "fax", etc.</summary>
    public List<(string Kind, string Number)> Phones { get; set; } = new();

    /// <summary>Homepage / personal site URL from \homepage{...}.</summary>
    public string? Homepage { get; set; }

    /// <summary>Photo filename from \photo[h][frame]{file} — captured for a later photo block.</summary>
    public string? PhotoFilename { get; set; }

    /// <summary>Social handles from \social[network]{handle} — linkedin, github, etc.</summary>
    public List<(string Network, string Handle)> Socials { get; set; } = new();

    /// <summary>Free-form extra info from \extrainfo{...}.</summary>
    public string? ExtraInfo { get; set; }

    // ── LaTeX-specific preamble metadata ──────────────────────────────

    /// <summary>
    /// LaTeX \documentclass (e.g. "article", "report", "book", "beamer").
    /// Only populated when parsed from a .tex source.
    /// </summary>
    public string? DocumentClass { get; set; }

    /// <summary>
    /// Options passed to \documentclass[...]{...} (e.g. "11pt,a4paper,twocolumn").
    /// </summary>
    public string? DocumentClassOptions { get; set; }

    /// <summary>
    /// Packages requested via \usepackage — name → optional bracket args.
    /// </summary>
    public List<LatexPackageReference> Packages { get; set; } = [];

    /// <summary>
    /// Raw \date{} content if present.
    /// </summary>
    public string? Date { get; set; }

    /// <summary>
    /// Requested bibliography style (\bibliographystyle{...}).
    /// </summary>
    public string? BibliographyStyle { get; set; }

    /// <summary>
    /// Page-layout options from \usepackage[opts]{geometry} (e.g. "margin=1in,a4paper").
    /// Null if no geometry package was loaded.
    /// </summary>
    public string? GeometryOptions { get; set; }

    /// <summary>
    /// Whether the source loads \usepackage{titlesec} for custom section formatting.
    /// We don't currently apply the customizations on render, but we record the fact
    /// so we can warn the user that section styling won't round-trip exactly.
    /// </summary>
    public bool UsesTitlesec { get; set; }

    /// <summary>
    /// Primary document language extracted from \usepackage[lang]{babel} or
    /// \setdefaultlanguage{lang} (polyglossia). E.g. "english", "french", "spanish".
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// All cite keys referenced via \cite{}, \citep{}, \citet{}, \parencite{} etc.
    /// in any paragraph or paragraph-style element. Lets the editor validate
    /// citations against the bibliography.
    /// </summary>
    public List<string> CitedKeys { get; set; } = [];

    /// <summary>
    /// All labels referenced via \ref{}, \eqref{}, \cref{}, \Cref{}, \autoref{}.
    /// Lets the editor validate cross-references against actual labels.
    /// </summary>
    public List<string> ReferencedLabels { get; set; } = [];

    /// <summary>
    /// Line-spacing setting from \onehalfspacing / \doublespacing / \setstretch{N}
    /// (setspace package). Values: "single", "onehalf", "double", or a numeric stretch
    /// factor like "1.5". Null if no spacing override was found.
    /// </summary>
    public string? LineSpacing { get; set; }

    /// <summary>
    /// Whether the document uses \pagestyle{fancy} (fancyhdr package).
    /// The raw header/footer definitions are stored in FancyhdrSource for passthrough.
    /// </summary>
    public bool UsesFancyhdr { get; set; }

    /// <summary>
    /// Raw fancyhdr setup commands (\fancyhead, \fancyfoot, \fancyhf, \renewcommand\headrulewidth)
    /// preserved for round-trip export. Not rendered in the editor preview.
    /// </summary>
    public string? FancyhdrSource { get; set; }
}

/// <summary>
/// A single \usepackage entry extracted from a LaTeX preamble.
/// </summary>
public class LatexPackageReference
{
    /// <summary>Package name (e.g. "amsmath", "tikz", "hyperref").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional bracket options, if present (e.g. "utf8" for inputenc).</summary>
    public string? Options { get; set; }
}
