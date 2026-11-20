namespace Lilia.Import.Interfaces;

/// <summary>
/// What the last read cost at Mathpix, for the plan's PDF pages (the owner, 20 Nov 2026: count in pages, as Mathpix bills:
/// $0.005 a PDF page on /v3/pdf, $0.002 an image on /v3/text, an image of more than 12 rows of text at the page rate).
/// Set once Mathpix has taken the file (a pdf_id, or an answer from /v3/text), so an import that fails after that is still
/// counted; null while nothing has reached Mathpix, so a refused or rejected file costs nothing (PDF and scan import,
/// 20 Nov 2026).
/// </summary>
public interface IMathpixCharge
{
    MathpixCharge? LastCharge { get; }
}

/// <param name="Pages">The pages Mathpix reported reading, or null when it didn't say (the caller's own count is used).</param>
public sealed record MathpixCharge(int? Pages);
