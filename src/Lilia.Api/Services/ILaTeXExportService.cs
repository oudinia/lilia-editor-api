namespace Lilia.Api.Services;

public interface ILaTeXExportService
{
    Task<Stream> ExportToZipAsync(Guid documentId, LaTeXExportOptions options);

    /// <summary>
    /// Build the single-file main.tex content from in-memory document
    /// + blocks. Skips the zip step — used by:
    ///   - <c>?mode=tex</c> for direct .tex download
    ///   - <c>?mode=preview</c> for inline browser display
    ///   - LatexRoundtripTests for parse → blocks → export → re-parse
    /// </summary>
    string BuildSingleFileLatex(
        Lilia.Core.Entities.Document doc,
        List<Lilia.Core.Entities.Block> blocks,
        List<Lilia.Core.Entities.BibliographyEntry> bibEntries,
        LaTeXExportOptions options);

    /// <summary>The main.tex and references.bib a submission would hold now (the LaTeX diff).</summary>
    (string MainTex, string? ReferencesBib) SubmissionSource(
        Lilia.Core.Entities.Document doc,
        List<Lilia.Core.Entities.Block> blocks,
        List<Lilia.Core.Entities.BibliographyEntry> bibEntries);

    /// <summary>The figure images, under dir/figures (the marked-up PDF's compile).</summary>
    Task WriteFiguresAsync(List<Lilia.Core.Entities.Block> blocks, string dir);
}

public class LaTeXExportOptions
{
    /// <summary>The class for a document that has none of its own (a fallback, not a choice).</summary>
    public string DocumentClass { get; set; } = "article";

    /// <summary>
    /// The class for this export only, over the document's own (the LaTeX export page's Document
    /// Class). Null uses the document's class. Only a class Lilia ships counts. Bound from
    /// <c>?classOverride=</c>; the document itself is not changed.
    /// </summary>
    public string? ClassOverride { get; set; }
    // Null: the document's own font size and paper. These defaulted to "11pt" and "a4paper", which the
    // preamble builder applied OVER the document, and the web app never sends them, so every .tex/.zip
    // download and every LaTeX PDF export was 11pt A4 (5 Oct review). Set them only to override.
    public string? FontSize { get; set; }
    public string? PaperSize { get; set; }
    public string Structure { get; set; } = "single";
    public string MultiFileLayout { get; set; } = "flat";
    // Off by default: physics redefines siunitx's \qty, which Lilia's shared preamble loads, and
    // the check emitter never loads physics (MCP-09; LaTeXPreamble explains the choice).
    public bool IncludePhysics { get; set; }
    public bool IncludeChemistry { get; set; }
    public string BibliographyStyle { get; set; } = "plain";
    public double LineSpacing { get; set; } = 1.0;
    public bool IncludeImages { get; set; } = true;

    /// <summary>
    /// arXiv-ready export: also compile the project (pdflatex + BibTeX) and
    /// bundle the resulting <c>main.bbl</c> in the ZIP, so the submission
    /// compiles with pdflatex alone (arXiv's safest path) without re-running
    /// BibTeX. Bound from <c>?arxiv=true</c>. Zip mode only.
    /// </summary>
    public bool Arxiv { get; set; }

    /// <summary>
    /// A submission to the document's publisher (publisher templates step 6): one <c>main.tex</c>,
    /// <c>references.bib</c>, <c>main.bbl</c>, the publisher's <c>.bst</c>, the figures, and
    /// <c>lilia-export-manifest.json</c> with a SHA-256 per file. No README; a figure whose image
    /// can't be read stops it (<see cref="SubmissionFigureException"/>). Bound from <c>?submission=true</c>.
    /// </summary>
    public bool Submission { get; set; }

    /// <summary>
    /// ACM's TAPS names the ZIP <c>&lt;Proceeding Acronym&gt;-&lt;Paper ID&gt;.zip</c> (its dashboard shows
    /// both). Bound from <c>?tapsAcronym=</c> and <c>?tapsPaperId=</c>; only the file name changes.
    /// </summary>
    public string? TapsAcronym { get; set; }

    /// <summary>
    /// Who is downloading a submission: set by the LaTeX export route, never bound from the query.
    /// When set, the submission's main.tex and references.bib are kept (document_submissions), so
    /// "what changed since the submission" is against what was really sent.
    /// </summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string? SubmittedBy { get; set; }
    public string? TapsPaperId { get; set; }

    /// <summary>
    /// Citation backend: <c>natbib</c> (default — BibTeX, most journal-compatible,
    /// arXiv-safe) or <c>biblatex</c> (modern, needs Biber). When
    /// <c>biblatex</c>, the export loads <c>biblatex</c> + <c>\addbibresource</c>,
    /// prints with <c>\printbibliography</c>, and maps the citation commands
    /// (\citet→\textcite, \citep→\parencite, \cite→\autocite). The stored
    /// citation <em>mode</em> is backend-agnostic, so this is a pure export-time
    /// choice. Bound from <c>?citationBackend=biblatex</c>.
    /// </summary>
    public string CitationBackend { get; set; } = "natbib";

    /// <summary>
    /// The look for this export only (Export PDF: Look ▾ and Print-safe); null uses the
    /// document's own. Set by the PDF route from <c>?look=</c> / <c>?printSafe=</c>, never bound
    /// from this type's query string.
    /// </summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public Lilia.Engines.Themes.DocumentLook? LookOverride { get; set; }

    /// <summary>
    /// The project is compiled on this server (the PDF export), so a theme it cannot print fails
    /// the export instead of being written for compiling elsewhere.
    /// </summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool CompileHere { get; set; }
}

/// <summary>A submission's figures whose images could not be read: the export stops instead of framing them.</summary>
public sealed class SubmissionFigureException(IReadOnlyList<string> figures)
    : Exception($"These figures have no image: {string.Join(", ", figures)}.")
{
    public IReadOnlyList<string> Figures { get; } = figures;
}
