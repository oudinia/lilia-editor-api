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
}
