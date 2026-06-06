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
}

public class LaTeXExportOptions
{
    public string DocumentClass { get; set; } = "article";
    public string FontSize { get; set; } = "11pt";
    public string PaperSize { get; set; } = "a4paper";
    public string Structure { get; set; } = "single";
    public string MultiFileLayout { get; set; } = "flat";
    public bool IncludePhysics { get; set; } = true;
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
    /// Citation backend: <c>natbib</c> (default — BibTeX, most journal-compatible,
    /// arXiv-safe) or <c>biblatex</c> (modern, needs Biber). When
    /// <c>biblatex</c>, the export loads <c>biblatex</c> + <c>\addbibresource</c>,
    /// prints with <c>\printbibliography</c>, and maps the citation commands
    /// (\citet→\textcite, \citep→\parencite, \cite→\autocite). The stored
    /// citation <em>mode</em> is backend-agnostic, so this is a pure export-time
    /// choice. Bound from <c>?citationBackend=biblatex</c>.
    /// </summary>
    public string CitationBackend { get; set; } = "natbib";
}
