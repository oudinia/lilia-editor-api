using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// What pasted text will do when it meets pdflatex.
///
/// <para><b>Why a server call and not the map shipped to the browser.</b> The
/// classification is not a lookup. It is "in the catalog, else not something
/// inputenc handles" — and the catalog takes precedence over the inputenc range,
/// which a test found only by failing. A browser copy of those rules is a second
/// implementation that will drift from the one the validator uses. Asking
/// <see cref="IUnicodeShimService.Classify"/> means the paste check and the
/// compile give the same answer by construction. A paste is a rare event and
/// this touches no compiler, so the round trip is cheap.</para>
///
/// <para><b>What was measured before this was written</b> (24 Sep, real
/// compiles): <c>γ × — ≈ →</c> compile, because the shim maps them;
/// <c>é ß Ł ½</c> compile, because inputenc handles them; <c>ℵ 中 🙂</c> fail
/// fatally. So "won't compile" is true of the unmapped set and false of the
/// mapped one — which is the distinction this response keeps.</para>
/// </summary>
[ApiController]
[Route("api/unicode")]
[Authorize]
public class UnicodeController : ControllerBase
{
    /// <summary>A paste is prose, not a document. Anything larger is not a paste.</summary>
    public const int MaxTextLength = 100_000;

    private readonly IUnicodeShimService _shim;

    public UnicodeController(IUnicodeShimService shim) => _shim = shim;

    /// <summary>
    /// Classify the distinct non-ASCII characters in a run of text.
    /// </summary>
    /// <returns>
    /// <c>wontCompile</c> — fail under pdflatex, in Lilia and everywhere.
    /// <c>shimmed</c> — compile in Lilia only because the shim maps them; source
    /// copied out to another editor carries the raw character and fails there.
    /// <c>checked: false</c> when the catalog is not loaded: with no map the
    /// service cannot tell the two apart and would report nothing, and "nothing
    /// reported" must not be read as "all fine".
    /// </returns>
    [HttpPost("check")]
    [ProducesResponseType(typeof(UnicodeCheckResponse), StatusCodes.Status200OK)]
    public IActionResult Check([FromBody] UnicodeCheckRequest request)
    {
        var text = request.Text ?? string.Empty;
        if (text.Length > MaxTextLength)
            return BadRequest(new { error = $"Text must be at most {MaxTextLength} characters." });

        if (_shim.MappedCount == 0)
            return Ok(new UnicodeCheckResponse(false, [], []));

        var classified = _shim.Classify(text);
        var counts = CountCodepoints(text);

        var wontCompile = classified.Unmapped
            .Select(cp => new UnicodeCharDto(char.ConvertFromUtf32(cp), cp, null, counts.GetValueOrDefault(cp), UnicodeKind.Of(cp)))
            .ToList();
        var shimmed = classified.Shimmed
            .Select(kv => new UnicodeCharDto(char.ConvertFromUtf32(kv.Key), kv.Key, kv.Value, counts.GetValueOrDefault(kv.Key), UnicodeKind.Of(kv.Key)))
            .ToList();

        return Ok(new UnicodeCheckResponse(true, wontCompile, shimmed));
    }

    /// <summary>
    /// Occurrences, not distinct characters — the author pasted "2 characters",
    /// not "1 kind of character twice". Surrogate pairs count once.
    /// </summary>
    private static Dictionary<int, int> CountCodepoints(string text)
    {
        var counts = new Dictionary<int, int>();
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            var cp = char.ConvertToUtf32(text, i);
            if (cp > 0x7F) counts[cp] = counts.GetValueOrDefault(cp) + 1;
        }
        return counts;
    }
}

public record UnicodeCheckRequest(string? Text);

/// <param name="Checked">False when the catalog is not loaded — nothing below
/// can be trusted, and an empty list does not mean the text is clean.</param>
public record UnicodeCheckResponse(
    bool Checked,
    IReadOnlyList<UnicodeCharDto> WontCompile,
    IReadOnlyList<UnicodeCharDto> Shimmed);

/// <param name="Replacement">The LaTeX the shim substitutes; null for a character
/// that has none, which is why it will not compile.</param>
/// <param name="Count">Occurrences in the text.</param>
/// <param name="Kind"><c>symbol</c>, <c>script</c> or <c>emoji</c> — see
/// <see cref="UnicodeKind"/>. Whether a lookup can help: only a symbol might
/// have a command.</param>
public record UnicodeCharDto(string Char, int Codepoint, string? Replacement, int Count, string Kind);
