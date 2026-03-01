namespace Lilia.Api.Services;

public interface ILaTeXExportService
{
    Task<Stream> ExportToZipAsync(Guid documentId, LaTeXExportOptions options);
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
