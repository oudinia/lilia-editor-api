namespace Lilia.Import.Models;

public class MathpixOptions
{
    public string AppId { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.mathpix.com";
    public int PollIntervalMs { get; set; } = 2000;
    public int TimeoutSeconds { get; set; } = 300;
    public int MaxFileSizeMb { get; set; } = 50;

    /// <summary>
    /// Ask Mathpix for its LaTeX output (tex.zip) with the Markdown, and import that through the LaTeX project
    /// import; the Markdown stays the fallback (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    public bool RequestTexZip { get; set; } = true;

    /// <summary>
    /// How long to wait for the tex.zip conversion once the PDF itself is read. Past it the Markdown is used
    /// (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    public int TexZipTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Delete the PDF and its outputs at Mathpix (<c>DELETE /v3/pdf/{pdf_id}</c>) once Lilia has what it needs
    /// (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    public bool DeleteAfterImport { get; set; } = true;
}
