using System.Globalization;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines.Themes;

/// <summary>
/// The theme a figure is drawn in: the theme as the document prints it (a locked class prints
/// Classic, a theme the class cannot use falls back to Classic), on the theme's paper or on
/// white, and the colour <c>lilia-chapter</c> takes for this figure (#RRGGBB).
/// </summary>
public sealed record FigureTheme(string Theme, bool WhitePaper, string Chapter)
{
    /// <summary>What the drawing depends on, for the content cache: <c>index|theme|#B8303A</c>.</summary>
    public string Key => $"{Theme}|{(WhitePaper ? DocumentLook.PaperWhite : DocumentLook.PaperTheme)}|{Chapter}";

    /// <summary>Classic on white, chapter = ink: what a figure without a document is drawn in.</summary>
    public static FigureTheme Classic => new(ThemeCatalog.Classic, false, FigureColours.Resolve(ThemeCatalog.Classic, false)["lilia-accent"]);
}

/// <summary>
/// Theme colours as TikZ colour names (TikZ figures, step 3, design 1d):
/// <c>lilia-ink</c>, <c>lilia-paper</c>, <c>lilia-accent</c>, <c>lilia-accent-soft</c> (the accent
/// at 18 % on the paper), <c>lilia-chapter</c> (Index: the figure's chapter colour, pins and the
/// appendix restart included; elsewhere the accent) and <c>lilia-seq1</c>…<c>lilia-seq8</c> (the
/// Index sequence, defined in every theme; Classic maps everything to ink and greys).
///
/// <para><c>lilia-theme.sty</c> defines them for every theme; this class resolves the same values
/// from <c>themes.json</c> for the editor (swatches, autocomplete) and decides when a document
/// needs the package for them (Classic's colours-only line). Literal colours are never touched.</para>
/// </summary>
public static class FigureColours
{
    public const string Ink = "lilia-ink";
    public const string Paper = "lilia-paper";
    public const string Accent = "lilia-accent";
    public const string AccentSoft = "lilia-accent-soft";
    public const string Chapter = "lilia-chapter";

    /// <summary>The accent's share in <c>lilia-accent-soft</c> (xcolor: <c>lilia-accent!18!lilia-paper</c>).</summary>
    public const int SoftPercent = 18;

    /// <summary>Every name, in the order the editor lists them.</summary>
    public static readonly IReadOnlyList<string> Names =
        new[] { Ink, Paper, Accent, AccentSoft, Chapter }
            .Concat(Enumerable.Range(1, ThemeCatalog.SequenceLength).Select(i => $"lilia-seq{i}"))
            .ToArray();

    /// <summary>Classic's sequence: ink and greys, so Classic stays monochrome (as in lilia-theme.sty).</summary>
    public static readonly IReadOnlyList<string> ClassicSequence =
        new[] { "#1A1A1A", "#595959", "#999999", "#3B3B3B", "#7A7A7A", "#B3B3B3", "#2B2B2B", "#6A6A6A" };

    private static readonly Regex NameUse = new(
        @"(?<![A-Za-z0-9@])lilia-(?:ink|paper|accent-soft|accent|chapter|seq[1-8])(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    /// <summary>Whether this text names one of the colours.</summary>
    public static bool Uses(string? text) => !string.IsNullOrEmpty(text) && NameUse.IsMatch(text);

    /// <summary>
    /// Whether anything the document prints names one of the colours: a figure's source, an embed,
    /// any block's text, or the custom preamble. Then the colours must be defined, so Classic gains
    /// its colours-only line.
    /// </summary>
    public static bool UsedBy(Document doc, IEnumerable<Block>? blocks)
    {
        if (Uses(doc.CustomPreamble)) return true;
        if (blocks is null) return false;
        foreach (var block in blocks)
        {
            try
            {
                if (block.Content is { } content && Uses(content.RootElement.GetRawText())) return true;
            }
            catch (ObjectDisposedException) { /* a disposed content names nothing */ }
        }
        return false;
    }

    /// <summary>
    /// Every name resolved for one theme on its paper (or on white), <c>lilia-chapter</c> being
    /// <paramref name="chapter"/> when given, else the accent. Unknown themes are Classic.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolve(string? themeId, bool whitePaper, string? chapter = null)
    {
        var theme = ThemeCatalog.Find(themeId) ?? ThemeCatalog.Find(ThemeCatalog.Classic)!;
        var classic = theme.Id == ThemeCatalog.Classic;
        // On white (paper=white, print-safe) a theme prints its print-safe pair. Exposition's own
        // ground is dark: its print-safe version is cream with burgundy ink and structure.
        var paper = whitePaper ? theme.PrintSafe.Paper : theme.Colours.Paper;
        var ink = whitePaper ? theme.PrintSafe.Ink : theme.Colours.Ink;
        var accent = whitePaper && IsDark(theme.Colours.Paper) ? theme.PrintSafe.Ink : theme.Colours.Accent;
        var sequence = classic ? ClassicSequence : ThemeCatalog.Sequence;

        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Ink] = Hex(ink),
            [Paper] = Hex(paper),
            [Accent] = Hex(accent),
            [AccentSoft] = Mix(accent, SoftPercent, paper),
            [Chapter] = Hex(chapter ?? accent),
        };
        for (var i = 0; i < ThemeCatalog.SequenceLength; i++) map[$"lilia-seq{i + 1}"] = Hex(sequence[i]);
        return map;
    }

    /// <summary>The names for a figure in this theme.</summary>
    public static IReadOnlyDictionary<string, string> Resolve(FigureTheme t) => Resolve(t.Theme, t.WhitePaper, t.Chapter);

    /// <summary>
    /// The figure's theme in its document: the theme as printed, the paper, and lilia-chapter. In
    /// Index (not on beamer) it is the colour of the chapter the figure is in
    /// (<see cref="ThemeSections"/>: pins, the appendix restart, neutral ink before the first
    /// numbered chapter or under an unnumbered one); elsewhere the accent.
    /// </summary>
    /// <param name="place">The figure's place among the body's headings, or null when unknown.</param>
    public static FigureTheme For(Document doc, SectionPlace? place)
    {
        var cls = doc.LatexDocumentClass;
        if (ThemeLock.Reason(cls) is not null) return FigureTheme.Classic;
        var look = DocumentLook.Parse(doc.Look).ForClass(cls);
        var beamer = ThemeLock.IsBeamer(cls);
        var white = look.Paper == DocumentLook.PaperWhite;
        var colours = Resolve(look.Theme, white);
        var chapter = colours[Accent];
        if (look.Theme == ThemeCatalog.Index && !beamer)
            chapter = place?.Colour is { } c ? Hex(c) : colours[Ink];
        return new FigureTheme(look.Theme, white, chapter);
    }

    /// <summary>
    /// The lines a figure compiled on its own needs for the names: the theme's colours only, then
    /// this figure's chapter colour, so moving it to another chapter recolours it.
    /// </summary>
    public static string StandaloneLines(FigureTheme t)
    {
        var options = new List<string> { $"theme={t.Theme}" };
        if (t.WhitePaper) options.Add($"paper={DocumentLook.PaperWhite}");
        options.Add("coloursonly");
        return $"\\usepackage[{string.Join(", ", options)}]{{{ThemeCatalog.PackageName}}}\n"
             + $"\\definecolor{{{Chapter}}}{{HTML}}{{{t.Chapter.TrimStart('#')}}}\n";
    }

    /// <summary><paramref name="percent"/> % of <paramref name="a"/> over <paramref name="b"/>, as xcolor mixes <c>a!p!b</c>.</summary>
    public static string Mix(string a, int percent, string b)
    {
        var (ar, ag, ab) = Rgb(a);
        var (br, bg, bb) = Rgb(b);
        int Ch(int x, int y) => (int)Math.Round((percent * x + (100 - percent) * y) / 100.0, MidpointRounding.AwayFromZero);
        return $"#{Ch(ar, br):X2}{Ch(ag, bg):X2}{Ch(ab, bb):X2}";
    }

    private static string Hex(string colour) => "#" + colour.TrimStart('#').ToUpperInvariant();

    private static (int R, int G, int B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        return (int.Parse(h[..2], NumberStyles.HexNumber), int.Parse(h[2..4], NumberStyles.HexNumber), int.Parse(h[4..6], NumberStyles.HexNumber));
    }

    private static bool IsDark(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;
    }
}
