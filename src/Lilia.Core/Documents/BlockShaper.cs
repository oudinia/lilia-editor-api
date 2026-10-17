using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Core.Documents;

/// <summary>What support may know of a block (block_shapes): its shape, never its words.</summary>
public sealed record BlockShapeInfo(string Shape, string? Preview, int Words, int Characters);

/// <summary>
/// Turns a block into its shape for the admin console (the user, 17 Oct: "a block view, errors view,
/// text replaced by lorem ipsum, formula would be fixed only by user"; "data driven, every block").
/// Every string an author wrote becomes lorem ipsum of the same number of words; formulas, code,
/// personal details, invoices and links are reduced to sizes; layout values picked from a list (a
/// heading level, a code language) pass through. Each type in <see cref="BlockTypes.All"/> has its
/// own description here, and a test fails when a new type has none.
/// </summary>
public static partial class BlockShaper
{
    private enum Show { Prose, Size, Nothing }

    private sealed record Kind(string Label, Show Show, int MaxWords = 60, Func<JsonElement, string?>? Detail = null);

    private static readonly Dictionary<string, Kind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [BlockTypes.Paragraph] = new("Paragraph", Show.Prose),
        [BlockTypes.Heading] = new("Heading", Show.Prose, 12, c => Layout(c, "level") is { } l ? $"level {l}" : null),
        [BlockTypes.List] = new("List", Show.Prose, 60, c => Count(c, "items") is { } n ? $"{n} items" : null),
        [BlockTypes.Blockquote] = new("Quotation", Show.Prose),
        [BlockTypes.Quote] = new("Quotation", Show.Prose),
        [BlockTypes.Abstract] = new("Abstract", Show.Prose),
        [BlockTypes.Theorem] = new("Theorem-like", Show.Prose, 40, c => Layout(c, "kind") ?? Layout(c, "variant")),
        [BlockTypes.Callout] = new("Callout", Show.Prose, 40, c => Layout(c, "variant") ?? Layout(c, "kind")),
        [BlockTypes.Footnote] = new("Footnote", Show.Prose, 30),
        [BlockTypes.Verse] = new("Verse", Show.Prose, 40),
        [BlockTypes.Aside] = new("Aside", Show.Prose, 40),
        [BlockTypes.Annotation] = new("Annotation", Show.Prose, 30),
        [BlockTypes.FrontMatter] = new("Front matter", Show.Prose, 40),
        [BlockTypes.BackMatter] = new("Back matter", Show.Prose, 40),
        [BlockTypes.Title] = new("Title page", Show.Prose, 20),
        [BlockTypes.Cover] = new("Cover", Show.Prose, 20),
        [BlockTypes.Slide] = new("Slide", Show.Prose, 40),
        [BlockTypes.CvEntry] = new("CV entry", Show.Prose, 30),
        [BlockTypes.CvSection] = new("CV section", Show.Prose, 10),
        [BlockTypes.Figure] = new("Figure", Show.Prose, 20),
        [BlockTypes.Image] = new("Image", Show.Prose, 20),
        [BlockTypes.Equation] = new("Formula", Show.Size, Detail: _ => "only the author edits it"),
        [BlockTypes.Code] = new("Code", Show.Size, Detail: c => Layout(c, "language")),
        [BlockTypes.Algorithm] = new("Algorithm", Show.Size),
        [BlockTypes.Table] = new("Table", Show.Size, Detail: c => TableSize(c) is { } t ? $"{t.Rows} × {t.Columns}" : null),
        [BlockTypes.Bibliography] = new("Bibliography", Show.Size, Detail: c => Count(c, "entries") is { } n ? $"{n} entries" : null),
        [BlockTypes.Embed] = new("Embed", Show.Size, Detail: _ => "the link is hidden"),
        [BlockTypes.Photo] = new("Photo", Show.Nothing),
        [BlockTypes.PersonalInfo] = new("Personal details", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.ColumnLayout] = new("Column layout", Show.Nothing, Detail: c => Layout(c, "columns") is { } n ? $"{n} columns" : null),
        [BlockTypes.TableOfContents] = new("Table of contents", Show.Nothing),
        [BlockTypes.PageBreak] = new("Page break", Show.Nothing),
        [BlockTypes.ColumnBreak] = new("Column break", Show.Nothing),
        [BlockTypes.ChapterBreak] = new("Chapter break", Show.Nothing),
        [BlockTypes.Divider] = new("Divider", Show.Nothing),
        // Invoices hold names, addresses and amounts: sizes only.
        [BlockTypes.InvHeader] = new("Invoice header", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvParty] = new("Invoice party", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvLineItems] = new("Invoice lines", Show.Size, Detail: c => Count(c, "items") is { } n ? $"{n} lines" : "hidden"),
        [BlockTypes.InvTaxSummary] = new("Tax summary", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvTotals] = new("Invoice totals", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvPayment] = new("Payment details", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvAllowanceCharge] = new("Allowance or charge", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvDelivery] = new("Delivery", Show.Size, Detail: _ => "hidden"),
        [BlockTypes.InvNote] = new("Invoice note", Show.Prose, 30),
    };

    /// <summary>The types with a description of their own (the rest get the generic one).</summary>
    public static IReadOnlyCollection<string> DescribedTypes => Kinds.Keys;

    public static BlockShapeInfo Shape(string type, JsonElement content, Guid seed)
    {
        var strings = AuthoredStrings(content).ToList();
        var words = strings.Sum(WordCount);
        var chars = strings.Sum(s => s.Length);
        var kind = Kinds.GetValueOrDefault(type) ?? new Kind(type, Show.Prose);
        var detail = kind.Detail?.Invoke(content);
        var size = kind.Show switch
        {
            Show.Prose => words > 0 ? $"{words} words" : "empty",
            Show.Size when type == BlockTypes.Code || type == BlockTypes.Algorithm => $"{strings.Sum(s => s.Split('\n').Length)} lines",
            Show.Size => $"{chars} characters",
            _ => null,
        };
        var shape = string.Join(" · ", new[] { kind.Label, detail, size }.Where(s => !string.IsNullOrEmpty(s)));
        var preview = kind.Show == Show.Prose && words > 0
            ? Lorem(Math.Min(words, kind.MaxWords), seed) + (words > kind.MaxWords ? " …" : "")
            : null;
        return new BlockShapeInfo(shape, preview, words, chars);
    }

    private static readonly string[] Words =
        ("lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et " +
         "dolore magna aliqua enim ad minim veniam quis nostrud exercitation ullamco laboris nisi aliquip ex ea " +
         "commodo consequat duis aute irure in reprehenderit voluptate velit esse cillum fugiat nulla pariatur").Split(' ');

    // Picked from a list in the editor, not written freely.
    private static readonly HashSet<string> LayoutKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "level", "language", "lang", "align", "alignment", "style", "variant", "kind", "type", "ordered",
        "listType", "numbering", "engine", "environment", "placement", "width", "size", "columns",
    };

    /// <summary>Same number of words, the same every time for a block (seeded by its id).</summary>
    public static string Lorem(int words, Guid seed)
    {
        if (words <= 0) return "";
        var hash = BitConverter.ToInt32(seed.ToByteArray(), 0);
        var rng = new Random(hash);
        var w = Enumerable.Range(0, words).Select(_ => Words[rng.Next(Words.Length)]).ToArray();
        w[0] = char.ToUpperInvariant(w[0][0]) + w[0][1..];
        return string.Join(' ', w) + ".";
    }

    public static int WordCount(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Every string the author wrote (for counting; never stored).</summary>
    public static IEnumerable<string> AuthoredStrings(JsonElement e, string? key = null)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String when key is null || !LayoutKeys.Contains(key):
                yield return e.GetString() ?? "";
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var s in AuthoredStrings(p.Value, p.Name)) yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var s in AuthoredStrings(item, key)) yield return s;
                break;
        }
    }

    private static string? Layout(JsonElement content, string key) =>
        content.ValueKind == JsonValueKind.Object && content.TryGetProperty(key, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.String => v.GetString() is { Length: <= 24 } s && SafeToken().IsMatch(s) ? s : null,
                JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
                _ => null,
            }
            : null;

    private static int? Count(JsonElement content, string key) =>
        content.ValueKind == JsonValueKind.Object && content.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.GetArrayLength() : null;

    private static (int Rows, int Columns)? TableSize(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return null;
        var cols = content.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Array ? h.GetArrayLength()
            : rows.EnumerateArray().Select(r => r.ValueKind == JsonValueKind.Array ? r.GetArrayLength() : 0).DefaultIfEmpty(0).Max();
        return (rows.GetArrayLength(), cols);
    }

    /// <summary>
    /// A validation or compile message reduced to a kind support can act on. LaTeX quotes the
    /// source ("l.12 …") and names files, so anything not recognised is a plain "error".
    /// </summary>
    public static string ErrorKind(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "error";
        var first = message.Split('\n')[0];
        foreach (var (pattern, label) in KnownErrors)
        {
            if (!first.Contains(pattern, StringComparison.OrdinalIgnoreCase)) continue;
            if (label == "Undefined control sequence" && Command().Match(message) is { Success: true } m)
                return $"{label} {m.Value}";
            return label;
        }
        return "error (the message quotes the text, so it is hidden)";
    }

    private static readonly (string Pattern, string Label)[] KnownErrors =
    [
        ("Undefined control sequence", "Undefined control sequence"),
        ("Missing $ inserted", "Missing $ (maths outside maths mode)"),
        ("Missing } inserted", "Missing }"),
        ("Missing { inserted", "Missing {"),
        ("Extra }", "Extra }"),
        ("Extra alignment tab", "Too many & in a row"),
        ("Misplaced alignment tab", "& outside a table"),
        ("Runaway argument", "Runaway argument (an unclosed brace)"),
        ("Paragraph ended before", "Paragraph ended inside a command"),
        ("not found", "A file or package is not found"),
        ("Environment", "Unknown or unclosed environment"),
        ("Double superscript", "Double superscript"),
        ("Double subscript", "Double subscript"),
        ("Illegal unit", "Illegal unit of measure"),
        ("Too many }", "Too many }"),
        ("timed out", "Compile timed out"),
    ];

    [GeneratedRegex(@"\\[A-Za-z@]{1,40}")]
    private static partial Regex Command();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+$")]
    private static partial Regex SafeToken();
}
