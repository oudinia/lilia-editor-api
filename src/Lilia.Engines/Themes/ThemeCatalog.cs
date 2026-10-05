using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lilia.Engines.Themes;

public sealed record ThemeFonts(string Heading, string Body, string HeadingCss, string BodyCss);

public sealed record ThemeColours(string Paper, string Ink, string Accent, string Heading, string Kicker);

public sealed record ThemePrintSafe(string Paper, string Ink);

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
    IReadOnlyList<string> TexFiles)
{
    /// <summary>Built in <c>lilia-theme.sty</c>. Planned themes are listed but never compile.</summary>
    public bool IsBuilt => string.Equals(Status, "ready", StringComparison.Ordinal);
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

    public static IReadOnlyList<ThemeDescriptor> All => Catalog.Value.Themes;

    /// <summary>The Index sequence, <c>#RRGGBB</c>.</summary>
    public static IReadOnlyList<string> Sequence => Catalog.Value.Sequence;

    public static IReadOnlyList<string> Ids => All.Select(t => t.Id).ToList();

    public static ThemeDescriptor? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(t => string.Equals(t.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The source of <c>lilia-theme.sty</c>.</summary>
    public static string StySource => Sty.Value;

    /// <summary>
    /// Whether this LaTeX loads the theme package, so a compile has to carry the .sty beside it.
    /// </summary>
    public static bool UsesThemePackage(string? latex) =>
        !string.IsNullOrEmpty(latex) && latex.Contains("{" + PackageName + "}", StringComparison.Ordinal);

    /// <summary>
    /// Write <c>lilia-theme.sty</c> into a compile directory when the source loads it and the
    /// directory does not already have one (an exported project carries its own copy).
    /// </summary>
    public static void StageIfUsed(string latex, string directory)
    {
        if (!UsesThemePackage(latex)) return;
        var path = Path.Combine(directory, StyFileName);
        if (!File.Exists(path)) File.WriteAllText(path, StySource);
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
