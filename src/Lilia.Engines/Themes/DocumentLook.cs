using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lilia.Engines.Themes;

/// <summary>
/// The paper's table settings (Look → Tables, design 2f/2g), each optional: absent means the
/// default (the theme's <c>tablesDefault</c> style, normal density, captions above). Only the
/// values the author set are stored, so a theme change brings its own default style.
/// </summary>
public sealed record DocumentTables(string? Style, string? Density, string? Caption)
{
    public const string Ruled = "ruled";
    public const string Banded = "banded";
    public const string Header = "header";
    public const string Normal = "normal";
    public const string Compact = "compact";
    public const string Above = "above";
    public const string Below = "below";

    public static readonly string[] Styles = { Ruled, Banded, Header };
    public static readonly string[] Densities = { Normal, Compact };
    public static readonly string[] Captions = { Above, Below };
    internal static readonly string[] Keys = { "style", "density", "caption" };

    public static readonly DocumentTables None = new(null, null, null);

    public bool IsEmpty => Style is null && Density is null && Caption is null;
}

/// <summary>
/// A document's look, stored as jsonb in <c>documents.look</c>:
/// <c>{ "theme": "index", "paper": "theme"|"white", "pins": { "&lt;headingBlockId&gt;": 0..7 },
/// "tables": { "style": "ruled"|"banded"|"header", "density": "normal"|"compact", "caption": "above"|"below" } }</c>.
/// <c>tables</c> and each of its keys are optional. Null (no row value) means Classic.
/// </summary>
public sealed record DocumentLook(string Theme, string Paper, IReadOnlyDictionary<string, int> Pins)
{
    public const string PaperTheme = "theme";
    public const string PaperWhite = "white";
    private static readonly string[] Keys = { "theme", "paper", "pins", "tables" };

    public static readonly DocumentLook Classic = new(ThemeCatalog.Classic, PaperTheme, new Dictionary<string, int>());

    /// <summary>Export only, never stored: white paper and dark grounds swapped to their light pair.</summary>
    public bool PrintSafe { get; init; }

    /// <summary>The table settings the author chose; <see cref="DocumentTables.None"/> when none.</summary>
    public DocumentTables Tables { get; init; } = DocumentTables.None;

    /// <summary>
    /// A stored look that changes the PDF: a theme, or Classic with a table setting other than
    /// today's, on a class that does not lock the look; on beamer, Exposition (beamer ignores the
    /// table settings). A theme the class cannot use is Classic here, as it is in the LaTeX.
    /// </summary>
    public static bool IsThemed(string? storedLook, string? documentClass)
    {
        if (ThemeLock.Reason(documentClass) is not null) return false;
        var look = Parse(storedLook).ForClass(documentClass);
        return ThemeLock.IsBeamer(documentClass) ? !look.IsClassic : look.LoadsPackage;
    }

    /// <summary>
    /// The look as this class prints it: a theme the class cannot use (Exposition on article, a
    /// document theme on beamer, after a class change) falls back to Classic, keeping the table
    /// settings. The stored look is not touched, so switching the class back restores it.
    /// </summary>
    public DocumentLook ForClass(string? documentClass) =>
        IsClassic || ThemeLock.Allows(documentClass, Theme) ? this : this with { Theme = ThemeCatalog.Classic };

    public bool IsClassic => string.Equals(Theme, ThemeCatalog.Classic, StringComparison.Ordinal);

    /// <summary>The table style in effect: the author's, else the theme's default.</summary>
    public string TableStyle => Tables.Style ?? DefaultTableStyle;

    private string DefaultTableStyle => ThemeCatalog.Find(Theme)?.TablesDefault ?? DocumentTables.Ruled;

    public bool CompactTables => Tables.Density == DocumentTables.Compact;

    public bool CaptionsBelow => Tables.Caption == DocumentTables.Below;

    /// <summary>The table settings differ from the theme's defaults, so the package must say so.</summary>
    public bool HasTableOptions => TableStyle != DefaultTableStyle || CompactTables || CaptionsBelow;

    /// <summary>
    /// The package options the table settings add to the theme line: only what differs from the
    /// theme's defaults (<c>tables=…</c>, <c>tabledensity=compact</c>, <c>captions=below</c>).
    /// </summary>
    public IReadOnlyList<string> TableOptions()
    {
        var options = new List<string>();
        if (TableStyle != DefaultTableStyle) options.Add($"tables={TableStyle}");
        if (CompactTables) options.Add("tabledensity=compact");
        if (CaptionsBelow) options.Add("captions=below");
        return options;
    }

    /// <summary>
    /// Whether the document loads lilia-theme at all: any theme but Classic, and Classic when its
    /// tables are not today's (ruled, normal, captions above).
    /// </summary>
    public bool LoadsPackage => !IsClassic || HasTableOptions;

    /// <summary>Read the stored value. Anything unreadable is Classic: a bad row never breaks a compile.</summary>
    public static DocumentLook Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Classic;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var (look, errors) = Validate(doc.RootElement);
            return errors.Count == 0 && look is not null ? look : Classic;
        }
        catch (JsonException)
        {
            return Classic;
        }
    }

    /// <summary>
    /// Check a look sent by a client. Returns the normalised look, or the errors, each one naming
    /// the valid values. Availability (fonts installed) and the publisher-class lock are checked
    /// by the caller, which knows the server and the document.
    /// </summary>
    public static (DocumentLook? Look, IReadOnlyList<string> Errors) Validate(JsonElement element)
    {
        var errors = new List<string>();
        var themeIds = string.Join(", ", ThemeCatalog.Ids);
        if (element.ValueKind != JsonValueKind.Object)
            return (null, new[] { $"look must be an object: {{ \"theme\": {themeIds}, \"paper\": \"theme\" | \"white\", \"pins\": {{ \"<headingBlockId>\": 0-7 }}, \"tables\": {{ \"style\", \"density\", \"caption\" }} }}." });

        foreach (var p in element.EnumerateObject())
            if (!Keys.Contains(p.Name))
                errors.Add($"look.{p.Name} is not a look setting. Valid keys: {string.Join(", ", Keys)}.");

        string? theme = null;
        if (!element.TryGetProperty("theme", out var t) || t.ValueKind != JsonValueKind.String)
            errors.Add($"look.theme is required. Valid values: {themeIds}.");
        else if (ThemeCatalog.Find(t.GetString()) is not { } descriptor)
            errors.Add($"look.theme '{t.GetString()}' is not a theme. Valid values: {themeIds}.");
        else
            theme = descriptor.Id;

        var paper = PaperTheme;
        if (element.TryGetProperty("paper", out var pa) && pa.ValueKind != JsonValueKind.Null)
        {
            var value = pa.ValueKind == JsonValueKind.String ? pa.GetString()?.Trim().ToLowerInvariant() : null;
            if (value is PaperTheme or PaperWhite) paper = value;
            else errors.Add($"look.paper '{(pa.ValueKind == JsonValueKind.String ? pa.GetString() : pa.GetRawText())}' is not valid. Valid values: theme, white.");
        }

        var pins = new Dictionary<string, int>(StringComparer.Ordinal);
        if (element.TryGetProperty("pins", out var pi) && pi.ValueKind != JsonValueKind.Null)
        {
            if (pi.ValueKind != JsonValueKind.Object)
            {
                errors.Add("look.pins must be an object mapping a heading block id to a colour index 0-7.");
            }
            else
            {
                foreach (var pin in pi.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(pin.Name))
                        errors.Add("look.pins has an empty heading block id.");
                    else if (pin.Value.ValueKind != JsonValueKind.Number || !pin.Value.TryGetInt32(out var k)
                             || k < 0 || k >= ThemeCatalog.SequenceLength)
                        errors.Add($"look.pins.{pin.Name} must be an integer from 0 to {ThemeCatalog.SequenceLength - 1} (an index into the Index sequence).");
                    else
                        pins[pin.Name] = k;
                }
            }
        }

        var tables = DocumentTables.None;
        if (element.TryGetProperty("tables", out var tb) && tb.ValueKind != JsonValueKind.Null)
        {
            if (tb.ValueKind != JsonValueKind.Object)
            {
                errors.Add("look.tables must be an object: { \"style\": \"ruled\" | \"banded\" | \"header\", \"density\": \"normal\" | \"compact\", \"caption\": \"above\" | \"below\" }.");
            }
            else
            {
                foreach (var p in tb.EnumerateObject())
                    if (!DocumentTables.Keys.Contains(p.Name))
                        errors.Add($"look.tables.{p.Name} is not a table setting. Valid keys: {string.Join(", ", DocumentTables.Keys)}.");
                tables = new DocumentTables(
                    TableValue(tb, "style", DocumentTables.Styles, errors),
                    TableValue(tb, "density", DocumentTables.Densities, errors),
                    TableValue(tb, "caption", DocumentTables.Captions, errors));
            }
        }

        return errors.Count > 0 || theme is null
            ? (null, errors)
            : (new DocumentLook(theme, paper, pins) { Tables = tables }, errors);
    }

    /// <summary>One optional table setting: absent or null is the default; otherwise one of <paramref name="valid"/>.</summary>
    private static string? TableValue(JsonElement tables, string key, string[] valid, List<string> errors)
    {
        if (!tables.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        var value = v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim().ToLowerInvariant() : null;
        if (value is not null && valid.Contains(value)) return value;
        errors.Add($"look.tables.{key} '{(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())}' is not valid. Valid values: {string.Join(", ", valid)}.");
        return null;
    }

    /// <summary>
    /// The stored form, or null for Classic without table settings (which clears the column).
    /// <c>tables</c> is written only when the author set one of its values.
    /// </summary>
    public string? ToStorage()
    {
        if (IsClassic && Tables.IsEmpty) return null;
        var pins = new JsonObject();
        foreach (var (id, k) in Pins.OrderBy(p => p.Key, StringComparer.Ordinal)) pins[id] = k;
        var stored = new JsonObject { ["theme"] = Theme, ["paper"] = Paper, ["pins"] = pins };
        if (!Tables.IsEmpty)
        {
            var tables = new JsonObject();
            if (Tables.Style is not null) tables["style"] = Tables.Style;
            if (Tables.Density is not null) tables["density"] = Tables.Density;
            if (Tables.Caption is not null) tables["caption"] = Tables.Caption;
            stored["tables"] = tables;
        }
        return stored.ToJsonString();
    }

    /// <summary>The look with an export's overrides applied (Export PDF: Look ▾ and Print-safe).</summary>
    public DocumentLook With(string? theme, bool printSafe)
    {
        var look = this;
        if (!string.IsNullOrWhiteSpace(theme) && ThemeCatalog.Find(theme) is { } d && d.Id != Theme)
            look = look with { Theme = d.Id };
        if (printSafe) look = look with { Paper = PaperWhite, PrintSafe = true };
        return look;
    }
}
