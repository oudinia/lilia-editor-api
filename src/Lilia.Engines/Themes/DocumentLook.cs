using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lilia.Engines.Themes;

/// <summary>
/// A document's look, stored as jsonb in <c>documents.look</c>:
/// <c>{ "theme": "index", "paper": "theme"|"white", "pins": { "&lt;headingBlockId&gt;": 0..7 } }</c>.
/// Null (no row value) means Classic.
/// </summary>
public sealed record DocumentLook(string Theme, string Paper, IReadOnlyDictionary<string, int> Pins)
{
    public const string PaperTheme = "theme";
    public const string PaperWhite = "white";
    private static readonly string[] Keys = { "theme", "paper", "pins" };

    public static readonly DocumentLook Classic = new(ThemeCatalog.Classic, PaperTheme, new Dictionary<string, int>());

    /// <summary>Export only, never stored: white paper and dark grounds swapped to their light pair.</summary>
    public bool PrintSafe { get; init; }

    /// <summary>A stored look that changes the PDF: not Classic, on a class that does not lock it.</summary>
    public static bool IsThemed(string? storedLook, string? documentClass) =>
        !Parse(storedLook).IsClassic && ThemeLock.Reason(documentClass) is null;

    public bool IsClassic => string.Equals(Theme, ThemeCatalog.Classic, StringComparison.Ordinal);

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
            return (null, new[] { $"look must be an object: {{ \"theme\": {themeIds}, \"paper\": \"theme\" | \"white\", \"pins\": {{ \"<headingBlockId>\": 0-7 }} }}." });

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

        return errors.Count > 0 || theme is null
            ? (null, errors)
            : (new DocumentLook(theme, paper, pins), errors);
    }

    /// <summary>The stored form, or null for Classic (which clears the column).</summary>
    public string? ToStorage()
    {
        if (IsClassic) return null;
        var pins = new JsonObject();
        foreach (var (id, k) in Pins.OrderBy(p => p.Key, StringComparer.Ordinal)) pins[id] = k;
        return new JsonObject { ["theme"] = Theme, ["paper"] = Paper, ["pins"] = pins }.ToJsonString();
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
