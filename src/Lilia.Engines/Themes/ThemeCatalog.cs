using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lilia.Engines.Themes;

public sealed record ThemeFonts(string Heading, string Body, string HeadingCss, string BodyCss);

public sealed record ThemeColours(string Paper, string Ink, string Accent, string Heading, string Kicker);

public sealed record ThemePrintSafe(string Paper, string Ink);

/// <summary>
/// A table style's colours on one paper: the Banded stripe is <paramref name="Band"/> % of
/// <paramref name="Hue"/> over the paper, the Header band is <paramref name="Head"/> with
/// <paramref name="HeadInk"/> (white where the fill takes it at 4.5:1, else charcoal).
/// <c>"chapter"</c> as a hue or a fill means Index's current chapter colour.
/// </summary>
public sealed record ThemeTableColours(string Hue, int Band, string Head, string HeadInk);

/// <summary>The table colours on the theme's own paper and on white (paper=white, print-safe).</summary>
public sealed record ThemeTable(ThemeTableColours OnPaper, ThemeTableColours OnWhite);

/// <summary>One document theme, as <c>themes.json</c> describes it.</summary>
public sealed record ThemeDescriptor(
    string Id,
    string Name,
    [property: JsonPropertyName("for")] string For,
    string Status,
    ThemeFonts Fonts,
    ThemeColours Colours,
    ThemePrintSafe PrintSafe,
    IReadOnlyList<string>? Sequence,
    string TablesDefault,
    IReadOnlyList<string> TexFiles,
    ThemeTable? Table = null,
    IReadOnlyList<string>? Classes = null)
{
    /// <summary>
    /// Built: in <c>lilia-theme.sty</c>, or for a beamer theme in its own
    /// <c>beamertheme….sty</c>. Planned themes are listed but never compile.
    /// </summary>
    public bool IsBuilt => string.Equals(Status, "ready", StringComparison.Ordinal);

    /// <summary>A beamer theme (<c>classes: ["beamer"]</c>): loaded with <c>\usetheme</c>, for beamer only.</summary>
    public bool IsBeamerTheme => Classes?.Contains(ThemeLock.Beamer, StringComparer.OrdinalIgnoreCase) == true;
}

/// <summary>
/// The document themes (Document settings → Look). The single source of truth is
/// <c>Themes/themes.json</c>, embedded in this assembly, which <c>GET /api/themes</c>, the
/// preamble builder and the <c>lilia-theme.sty</c> tests all read. The package itself is
/// <c>Themes/lilia-theme.sty</c>, embedded beside it and staged into every compile that uses it.
/// </summary>
public static class ThemeCatalog
{
    public const string Classic = "classic";
    public const string StyFileName = "lilia-theme.sty";
    public const string PackageName = "lilia-theme";

    /// <summary>Exposition, the beamer theme: <c>\usetheme{LiliaExposition}</c>.</summary>
    public const string Exposition = "exposition";
    public const string ExpositionBeamerTheme = "LiliaExposition";
    public const string ExpositionStyFileName = "beamerthemeLiliaExposition.sty";

    /// <summary>The colours an Index paper's chapters take, in order.</summary>
    public const int SequenceLength = 8;

    private sealed record CatalogFile(IReadOnlyList<string> Sequence, IReadOnlyList<ThemeDescriptor> Themes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly Lazy<CatalogFile> Catalog = new(() =>
        JsonSerializer.Deserialize<CatalogFile>(ReadResource("Lilia.Engines.Themes.themes.json"), JsonOptions)
        ?? throw new InvalidOperationException("themes.json is empty."));

    private static readonly Lazy<string> Sty = new(() => ReadResource("Lilia.Engines.Themes.lilia-theme.sty"));

    private static readonly Lazy<string> ExpositionSty = new(() => ReadResource("Lilia.Engines.Themes.beamerthemeLiliaExposition.sty"));

    public static IReadOnlyList<ThemeDescriptor> All => Catalog.Value.Themes;

    /// <summary>The Index sequence, <c>#RRGGBB</c>.</summary>
    public static IReadOnlyList<string> Sequence => Catalog.Value.Sequence;

    public static IReadOnlyList<string> Ids => All.Select(t => t.Id).ToList();

    public static ThemeDescriptor? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(t => string.Equals(t.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The source of <c>lilia-theme.sty</c>.</summary>
    public static string StySource => Sty.Value;

    /// <summary>The source of <c>beamerthemeLiliaExposition.sty</c> (Olivia's, 6 Oct 2026).</summary>
    public static string ExpositionStySource => ExpositionSty.Value;

    /// <summary>
    /// Whether this LaTeX loads the theme package, so a compile has to carry the .sty beside it.
    /// </summary>
    public static bool UsesThemePackage(string? latex) =>
        !string.IsNullOrEmpty(latex) && latex.Contains("{" + PackageName + "}", StringComparison.Ordinal);

    /// <summary>
    /// Whether this LaTeX loads the Exposition beamer theme (<c>\usetheme{LiliaExposition}</c>,
    /// with or without <c>[printsafe]</c>), so a compile has to carry its .sty beside it.
    /// </summary>
    public static bool UsesExposition(string? latex) =>
        !string.IsNullOrEmpty(latex) && latex.Contains("{" + ExpositionBeamerTheme + "}", StringComparison.Ordinal);

    /// <summary>The theme files this LaTeX loads, by file name: what a compile or a .zip must carry.</summary>
    public static IReadOnlyList<(string FileName, string Source)> FilesUsedBy(string? latex)
    {
        var files = new List<(string, string)>();
        if (UsesThemePackage(latex)) files.Add((StyFileName, StySource));
        if (UsesExposition(latex)) files.Add((ExpositionStyFileName, ExpositionStySource));
        return files;
    }

    /// <summary>
    /// Write the theme files (<c>lilia-theme.sty</c>, <c>beamerthemeLiliaExposition.sty</c>) into a
    /// compile directory when the source loads them and the directory does not already have them
    /// (an exported project carries its own copy).
    /// </summary>
    public static void StageIfUsed(string latex, string directory)
    {
        foreach (var (name, source) in FilesUsedBy(latex))
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) File.WriteAllText(path, source);
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
