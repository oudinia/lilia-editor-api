using Lilia.Engines.Themes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// The document themes (Document settings → Look), from <c>themes.json</c>, the same descriptors
/// the LaTeX package is built from. <c>available</c> is false for a planned theme and for a theme
/// whose TeX packages this server does not have, so the picker can disable what would not compile.
/// </summary>
[ApiController]
[Route("api/themes")]
[Authorize]
public class ThemesController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(ThemeCatalog.All.Select(ToDto));

    /// <summary>
    /// The package itself, for Copy LaTeX ("Needs lilia-theme.sty: Download it"). The .zip export
    /// already carries it.
    /// </summary>
    [HttpGet("lilia-theme.sty")]
    public IActionResult Package() =>
        File(System.Text.Encoding.UTF8.GetBytes(ThemeCatalog.StySource), "application/x-tex", ThemeCatalog.StyFileName);

    /// <summary>
    /// A theme's beamer version (<c>beamerthemeLiliaCerulean.sty</c>, <c>beamerthemeLiliaIndex.sty</c>,
    /// <c>beamerthemeLiliaExposition.sty</c>), for Copy LaTeX on a beamer deck. The .zip export
    /// already carries it.
    /// </summary>
    [HttpGet("beamerthemeLilia{name}.sty")]
    public IActionResult BeamerPackage(string name) =>
        ThemeCatalog.FindBeamerSty($"beamerthemeLilia{name}.sty") is { Beamer: { } beamer }
            ? File(System.Text.Encoding.UTF8.GetBytes(ThemeCatalog.BeamerStySource(beamer)), "application/x-tex", beamer.StyFileName)
            : NotFound();

    internal static ThemeDto ToDto(ThemeDescriptor t) => new(
        t.Id, t.Name, t.For, ThemeAvailability.IsAvailable(t.Id), t.Status,
        t.Fonts, t.Colours, t.PrintSafe, t.Sequence, t.TablesDefault, t.Table, t.Classes,
        FigureColours.Resolve(t.Id, whitePaper: false));
}

public sealed record ThemeDto(
    string Id,
    string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("for")] string For,
    bool Available,
    string Status,
    ThemeFonts Fonts,
    ThemeColours Colours,
    ThemePrintSafe PrintSafe,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    IReadOnlyList<string>? Sequence,
    string TablesDefault,
    // The table styles' colours (Look → Tables), on the theme's paper and on white, for the
    // editor's Preview as; null for a planned theme.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    ThemeTable? Table = null,
    // The classes the theme is for: ["beamer"] for Exposition, null for the others (every class
    // they have a version for: beamer too for Cerulean and Index). The document's lookThemes is the
    // list to use.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    IReadOnlyList<string>? Classes = null,
    // The theme colour names a TikZ figure can use (lilia-ink … lilia-seq8), resolved on the
    // theme's own paper, lilia-chapter being the accent (an Index figure's own chapter colour is
    // GET …/blocks/{blockId}/figure/colours). For the editor's swatches and autocomplete.
    IReadOnlyDictionary<string, string>? TikzColours = null);
