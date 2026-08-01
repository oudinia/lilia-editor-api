using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Font coverage — can this font render this text, and if not, what can?
///
/// <para>Answers from measured facts rather than a maintained list: <c>fc-list</c>
/// reports each font's exact charset, so this is a lookup. The point is to move
/// the question <i>before</i> the compile. P0.1 detects dropped glyphs afterwards,
/// from the log; these endpoints let the editor say "this font cannot render the
/// Hebrew you just pasted" while there is still something to do about it.</para>
/// </summary>
[ApiController]
[Route("api/fonts")]
[Authorize]
public class FontsController : ControllerBase
{
    private readonly IFontCoverageService _fonts;

    public FontsController(IFontCoverageService fonts) => _fonts = fonts;

    /// <summary>
    /// Which characters of <paramref name="text"/> the given family cannot
    /// render — the ones LaTeX would silently drop.
    ///
    /// <para><c>available: false</c> means no font catalogue is configured. That
    /// is deliberately distinct from an empty <c>uncovered</c> list: "nothing is
    /// missing" and "I cannot tell you" are different answers, and conflating
    /// them is how a coverage check becomes worse than none.</para>
    /// </summary>
    [HttpGet("{family}/uncovered")]
    public async Task<IActionResult> Uncovered(string family, [FromQuery] string text, CancellationToken ct)
    {
        var uncovered = await _fonts.UncoveredCodePointsAsync(family, text ?? "", ct);

        return Ok(new
        {
            available = _fonts.IsAvailable,
            family,
            uncovered = uncovered.Select(cp => new
            {
                codePoint = cp,
                hex = $"U+{cp:X4}",
                character = char.ConvertFromUtf32(cp),
            }),
        });
    }

    /// <summary>
    /// Families that cover every non-ASCII character in <paramref name="text"/>,
    /// portable ones first.
    ///
    /// <para><c>portable</c> is the field that matters. A <c>system</c> font
    /// renders perfectly here and breaks the moment the <c>.tex</c> reaches a
    /// collaborator or Overleaf — the author cannot tell the difference by
    /// looking, so the answer has to say which is which.</para>
    /// </summary>
    [HttpGet("covering")]
    public async Task<IActionResult> Covering([FromQuery] string text, CancellationToken ct)
    {
        var codePoints = FontCoverageService.InterestingCodePoints(text);
        var options = await _fonts.FontsCoveringAsync(codePoints, ct);

        return Ok(new
        {
            available = _fonts.IsAvailable,
            // Echoed so the caller can see what was actually asked — ASCII is
            // excluded, and a caller comparing counts would otherwise be puzzled.
            codePoints = codePoints.Select(cp => $"U+{cp:X4}"),
            fonts = options.Select(o => new
            {
                family = o.Family,
                provenance = o.Provenance,
                portable = o.IsPortable,
            }),
        });
    }
}
