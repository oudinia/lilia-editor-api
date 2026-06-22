namespace Lilia.Core.Entities;

/// <summary>
/// One standalone, gateable tool in the public tool suite (the `tools` registry).
///
/// Tools are good-UI, gated, public front-doors over Lilia's existing engines
/// (bibliography lookup, table render, DOCX import) — see
/// lilia-docs/features/2026-06-22-standalone-tools-strategy.md. DB-authoritative,
/// loaded into memory at startup like the other catalogs (latex_tokens, ai_models);
/// adding a tool is a row + a lilia-cloud lander, not a deploy.
/// </summary>
public class Tool
{
    /// <summary>URL slug + primary key, e.g. <c>doi-to-bibtex</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string SeoDescription { get; set; } = string.Empty;

    /// <summary>text | grid | file — drives the widget input shape.</summary>
    public string InputKind { get; set; } = "text";

    /// <summary>source | pdf | image — drives watermark eligibility (rendered only).</summary>
    public string OutputKind { get; set; } = "source";

    /// <summary>Dispatch key for the executor: doi | table | word.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>Free anonymous uses/day. 0 = unlimited (no quota).</summary>
    public int FreeLimitPerDay { get; set; }

    /// <summary>Max input size for the free tier in bytes. 0 = no cap.</summary>
    public int FreeSizeCapBytes { get; set; }

    /// <summary>Cross-sell CTA label (e.g. "Open in Lilia editor").</summary>
    public string? CrossSellLabel { get; set; }

    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
