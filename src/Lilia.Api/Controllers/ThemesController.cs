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

    internal static ThemeDto ToDto(ThemeDescriptor t) => new(
        t.Id, t.Name, t.For, ThemeAvailability.IsAvailable(t.Id), t.Status,
        t.Fonts, t.Colours, t.PrintSafe, t.Sequence, t.TablesDefault);
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
    string TablesDefault);
