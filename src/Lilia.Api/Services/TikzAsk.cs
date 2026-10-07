using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Blocks;
using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

/// <summary>
/// The figure Ask Lilia is working on (TikZ step 3, 1c), sent by the editor with the message.
/// <c>BlockId</c>: the TikZ figure (absent for <i>Draw with Ask Lilia…</i>, a figure not inserted
/// yet: then <c>AfterBlockId</c> is where it would go). <c>Source</c>, <c>Caption</c>, <c>Label</c>:
/// what the author sees right now (the split view may be ahead of the last save); absent, the saved
/// block's. <c>Intent</c>: the suggestion the message came from (<c>draw</c>, <c>change</c>,
/// <c>explain</c>, <c>caption</c>, <c>fix</c>), or absent for a typed message. <c>Error</c>: the
/// figure's current error, for <i>Fix this error</i>.
/// </summary>
public sealed record TikzAskFigure(
    string? BlockId = null,
    string? AfterBlockId = null,
    string? Source = null,
    string? Caption = null,
    string? Label = null,
    string? Intent = null,
    TikzAskFigureError? Error = null);

public sealed record TikzAskFigureError(int? Line, string? Message);

/// <summary>
/// What Ask Lilia answered about the figure, beside the reply text.
/// <para><c>Kind</c>: <c>draw</c> (a new drawing, for the drawing card) · <c>change</c> (new source
/// for an existing figure, shown as a diff) · <c>failed</c> (two attempts did not draw:
/// <c>Source</c> is the last one, <c>Error</c> where it broke; <i>Open in split view</i> only) ·
/// <c>explain</c> (<c>Lines</c>: the line chips) · <c>caption</c> (<c>Caption</c>) · <c>answer</c>
/// (text only).</para>
/// <para><c>Svg</c> is the drawing of <c>Source</c> (draw and change only: it drew). <c>Attempts</c>:
/// 1, or 2 when the first did not draw and the error went back to the model.
/// <c>PreambleAdditions</c>: the TikZ setup lines it needs that the preamble lacks (the editor
/// adds them on Insert or Keep). <c>Packages</c>: the packages it uses beyond tikz, which Lilia
/// loads by itself (pgfplots, tikz-cd).</para>
/// </summary>
public sealed record TikzAskProposal(
    string Kind,
    string? Source = null,
    string? Svg = null,
    int? DrawMs = null,
    int Attempts = 0,
    int? LineCount = null,
    int? ChangedLines = null,
    IReadOnlyList<string>? PreambleAdditions = null,
    IReadOnlyList<string>? Packages = null,
    TikzAskProposalError? Error = null,
    string? Caption = null,
    IReadOnlyList<TikzAskLine>? Lines = null);

public sealed record TikzAskProposalError(string Kind, string Message, int? Line, string? Excerpt);

/// <summary>A line chip: lines <c>From</c>–<c>To</c> (1-based, inclusive) and what they do.</summary>
public sealed record TikzAskLine(int From, int To, string Text);

/// <summary>
/// Everything Ask Lilia reads about a figure — and nothing else: the figure's source, caption and
/// label, the preamble's TikZ lines, the theme's colour names, and the paragraphs on either side.
/// Not the whole document.
/// </summary>
public sealed record TikzAskContext(
    bool IsNew,
    string Source,
    string Caption,
    string Label,
    string PreambleSetup,
    IReadOnlyDictionary<string, string> Colours,
    string? ParagraphBefore,
    string? ParagraphAfter,
    TikzAskFigureError? Error);

/// <summary>A model reply, taken apart.</summary>
public sealed record TikzAskReply(
    string Prose,
    string? Source,
    IReadOnlyList<string> PreambleLines,
    string? Caption,
    IReadOnlyList<TikzAskLine> Lines);

/// <summary>One line of a line diff: <c>' '</c> kept, <c>'-'</c> removed, <c>'+'</c> added.</summary>
public readonly record struct TikzDiffLine(char Op, string Text);

/// <summary>
/// Ask Lilia on a figure (TikZ step 3, 1c): the prompt, the context it may read, how its reply is
/// read, and the checks on the TikZ it writes. Pure: <see cref="TikzAskRunner"/> calls the model and
/// the compiler; <see cref="AskLiliaService"/> brings the gate, the model and the metering.
/// </summary>
public static class TikzAsk
{
    public const string SkillId = "lilia-figure";

    public static readonly IReadOnlySet<string> Intents =
        new HashSet<string>(StringComparer.Ordinal) { "draw", "change", "explain", "caption", "fix" };

    /// <summary>Intents that propose something for the figure: only someone who may write gets them.</summary>
    public static bool Proposes(string? intent) => intent is not ("explain");

    private const int MaxParagraphChars = 1500;
    private const int MaxSourceChars = 20_000;

    /// <summary>The reply when two attempts did not draw (the line, when TeX named one).</summary>
    public static string CouldNotDraw(int? line) =>
        line is { } n
            ? $"I couldn't get this to draw. Here's the source; the error is on line {n}."
            : "I couldn't get this to draw. Here's the source.";

    // ── Context ─────────────────────────────────────────────────────────

    /// <summary>
    /// The context for one figure of <paramref name="doc"/>. <paramref name="figure"/>'s own source,
    /// caption and label win over the saved block's (the author may be ahead of the save).
    /// Null when <c>BlockId</c> names no TikZ figure of this document.
    /// </summary>
    public static TikzAskContext? Context(DocumentDto doc, TikzAskFigure figure, IReadOnlyDictionary<string, string> colours)
    {
        var blocks = (doc.Blocks ?? new List<BlockDto>()).OrderBy(b => b.SortOrder).ToList();
        BlockDto? block = null;
        int before, after;   // index of the block before / after the figure's place
        if (!string.IsNullOrWhiteSpace(figure.BlockId))
        {
            if (!Guid.TryParse(figure.BlockId, out var id)) return null;
            var at = blocks.FindIndex(b => b.Id == id);
            if (at < 0) return null;
            block = blocks[at];
            if (block.Type is not ("figure" or "image") || !TikzFigure.IsTikz(block.Content)) return null;
            before = at - 1;
            after = at + 1;
        }
        else
        {
            var at = Guid.TryParse(figure.AfterBlockId, out var afterId) ? blocks.FindIndex(b => b.Id == afterId) : -1;
            if (at < 0) at = blocks.Count - 1;
            before = at;
            after = at + 1;
        }

        var source = figure.Source ?? (block is null ? "" : TikzFigure.Source(block.Content));
        if (source.Length > MaxSourceChars) source = source[..MaxSourceChars];
        return new TikzAskContext(
            IsNew: block is null,
            Source: source,
            Caption: (figure.Caption ?? StringField(block, "caption")).Trim(),
            Label: (figure.Label ?? StringField(block, "label")).Trim(),
            PreambleSetup: TikzFigure.SetupLines(doc.CustomPreamble),
            Colours: colours,
            ParagraphBefore: Paragraph(blocks, before, -1),
            ParagraphAfter: Paragraph(blocks, after, +1),
            Error: figure.Error);
    }

    private static string StringField(BlockDto? block, string name) =>
        block is not null && block.Content.ValueKind == JsonValueKind.Object
        && block.Content.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    /// <summary>The nearest paragraph from <paramref name="from"/> in direction <paramref name="step"/>, within two blocks, not past a heading.</summary>
    private static string? Paragraph(List<BlockDto> blocks, int from, int step)
    {
        for (int i = from, seen = 0; i >= 0 && i < blocks.Count && seen < 2; i += step, seen++)
        {
            var b = blocks[i];
            if (b.Type is "heading" or "header" or "title") return null;
            if (b.Type != "paragraph") continue;
            var text = StringField(b, "text").Trim();
            if (text.Length == 0) continue;
            return text.Length > MaxParagraphChars ? text[..MaxParagraphChars] + "…" : text;
        }
        return null;
    }

    // ── Prompt ──────────────────────────────────────────────────────────

    /// <summary>
    /// The rules for the TikZ it writes. Kept as their own text so the tests can hold them to the
    /// handoff: only the lines needed, untouched lines byte for byte, the author's naming and
    /// indentation, theme colour names, no unneeded libraries.
    /// </summary>
    public const string TikzRules = """
        RULES FOR THE TIKZ YOU WRITE
        • Change only the lines the request needs. Never reformat, re-indent, reorder or rewrap untouched lines: copy them byte for byte.
        • Keep the author's naming (node names, styles, macros) and their indentation.
        • Prefer the theme colour names (lilia-ink, lilia-accent, lilia-accent-soft, lilia-chapter, lilia-seq1…lilia-seq8) to literal colours, unless the author names a colour. Never replace a literal colour the author chose unless asked.
        • Never add a TikZ library the figure does not need, and never one the preamble already loads. pgfplots and tikz-cd are loaded by Lilia when used: do not add \usepackage lines.
        • No \input, \include, \write, \immediate, \openout, \catcode or anything that reads or writes files.
        """;

    private const string Formats = """
        HOW TO ANSWER — by what the author asks:
        • Draw, change or fix: one or two short sentences saying what you did, then the COMPLETE figure source in ONE ```tikz fenced block. The source is the picture only (\begin{tikzpicture}…\end{tikzpicture} or \begin{tikzcd}…\end{tikzcd}, and anything wrapping it): no \documentclass, no \begin{document}, no figure environment, no \caption, no \label, and no line numbers. If it needs a TikZ library the preamble does not load, add ONE ```preamble fenced block holding only those \usetikzlibrary{…} or \usepgfplotslibrary{…} lines.
        • Explain: never write source. One sentence on what the figure shows, then one line per part, in source order, at most six, each starting with its lines: "[L2-4] Three nodes …" or "[L7] The composite …". Line numbers are the ones shown below.
        • Write a caption: one caption in ONE ```caption fenced block (plain text, maths in $…$), nothing else.
        • Anything else about the figure: answer briefly, without a source block unless the author asked for a change.
        Lilia compiles every source you send before the author sees it; one that does not draw comes back to you with TeX's error.
        """;

    /// <summary>The system prompt: the figure-only role, the answer formats, the TikZ rules and the context.</summary>
    public static string SystemPrompt(TikzAskContext ctx, string? intent, string proficiencyPreamble = "")
    {
        var sb = new StringBuilder();
        if (proficiencyPreamble.Length > 0) sb.AppendLine(proficiencyPreamble).AppendLine();
        sb.AppendLine("You are Ask Lilia, working on ONE TikZ figure in a Lilia document (a LaTeX-first academic editor). "
            + "You see only this figure and what is listed under FIGURE CONTEXT, not the rest of the document: if the author asks about anything else, say it is outside what you can see here.");
        sb.AppendLine();
        sb.AppendLine(Formats);
        sb.AppendLine(TikzRules);
        sb.AppendLine(IntentNote(ctx, intent));
        sb.AppendLine();
        sb.Append(ContextBlock(ctx));
        return sb.ToString();
    }

    private static string IntentNote(TikzAskContext ctx, string? intent)
    {
        var empty = string.IsNullOrWhiteSpace(ctx.Source);
        return intent switch
        {
            "explain" => "THIS REQUEST: explain the figure. Do not write or change source.",
            "caption" => "THIS REQUEST: write a caption for the figure, from the drawing and the paragraphs around it.",
            "fix" => "THIS REQUEST: fix the error so the figure draws, changing as little as possible.",
            _ when empty => "THIS REQUEST: the figure is empty, so draw it from the description (and the paragraphs around it).",
            _ => "THIS REQUEST: as asked. A change returns the complete source with only the needed lines changed.",
        };
    }

    /// <summary>The figure context, as the model reads it.</summary>
    public static string ContextBlock(TikzAskContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FIGURE CONTEXT");
        sb.AppendLine(ctx.IsNew ? "Figure: a new figure, not inserted yet." : "Figure: an existing TikZ figure.");
        sb.AppendLine($"Caption: {(ctx.Caption.Length > 0 ? ctx.Caption : "(none)")}");
        sb.AppendLine($"Label: {(ctx.Label.Length > 0 ? ctx.Label : "(none)")}");
        if (string.IsNullOrWhiteSpace(ctx.Source))
            sb.AppendLine("Source: (empty)");
        else
        {
            sb.AppendLine("Source, with line numbers (the numbers are not part of it):");
            sb.AppendLine(Numbered(ctx.Source));
        }
        if (ctx.Error is { } e)
            sb.AppendLine($"It does not draw: {(e.Line is { } l ? $"line {l}: " : "")}{e.Message}");
        sb.AppendLine("Preamble TikZ lines: " + (string.IsNullOrWhiteSpace(ctx.PreambleSetup) ? "(none)" : "\n" + ctx.PreambleSetup));
        if (ctx.Colours.Count > 0)
            sb.AppendLine("Theme colour names: " + string.Join(", ", ctx.Colours.Select(kv => $"{kv.Key} {kv.Value}")));
        if (ctx.ParagraphBefore is { } pb) sb.AppendLine("Paragraph before the figure: " + pb);
        if (ctx.ParagraphAfter is { } pa) sb.AppendLine("Paragraph after the figure: " + pa);
        return sb.ToString();
    }

    public static string Numbered(string source)
    {
        var lines = SplitLines(source);
        var width = lines.Length.ToString().Length;
        return string.Join("\n", lines.Select((l, i) => $"{(i + 1).ToString().PadLeft(width)}| {l}"));
    }

    /// <summary>What goes back to the model when its source did not draw.</summary>
    public static string RetryMessage(TikzRenderError e) =>
        "That did not draw. TeX says"
        + (e.Line is { } l ? $" (line {l} of your source" + (string.IsNullOrEmpty(e.Excerpt) ? ")" : $": `{e.Excerpt.Trim()}`)") : "")
        + $": {e.Message}\nSend the corrected COMPLETE source in one ```tikz block, with the same rules.";

    // ── Reading the reply ───────────────────────────────────────────────

    private static readonly Regex Fence = new(@"```[ \t]*([A-Za-z-]*)[^\n]*\n(.*?)(?:\n```|```)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Chip = new(@"^\s*(?:[-*•]\s*)?\[L\s*(\d+)(?:\s*[-–—]\s*L?\s*(\d+))?\]\s*[:–—-]?\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex LibraryLine = new(@"^\\use(?:tikz|pgfplots)library\s*(?:\[[^\]]*\])?\s*\{[^{}]+\}\s*$", RegexOptions.Compiled);
    private static readonly Regex NumberPrefix = new(@"^\s*\d+\s?\|\s?", RegexOptions.Compiled);

    public static TikzAskReply Parse(string? reply, int sourceLineCount)
    {
        var text = (reply ?? "").Replace("\r\n", "\n");
        string? source = null, caption = null;
        var preamble = new List<string>();
        foreach (Match m in Fence.Matches(text))
        {
            var lang = m.Groups[1].Value.ToLowerInvariant();
            var body = m.Groups[2].Value;
            switch (lang)
            {
                case "preamble":
                    preamble.AddRange(body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
                    break;
                case "caption":
                    caption ??= body.Trim();
                    break;
                case "tikz" or "latex" or "tex" or "":
                    if (source is null && body.Contains(@"\begin{", StringComparison.Ordinal)) source = StripNumbers(body.TrimEnd());
                    break;
            }
        }
        var prose = Fence.Replace(text, "").Trim();

        var lines = new List<TikzAskLine>();
        var rest = new List<string>();
        foreach (var line in prose.Split('\n'))
        {
            var m = Chip.Match(line);
            if (m.Success && sourceLineCount > 0)
            {
                var from = Math.Clamp(int.Parse(m.Groups[1].Value), 1, sourceLineCount);
                var to = m.Groups[2].Success ? Math.Clamp(int.Parse(m.Groups[2].Value), from, sourceLineCount) : from;
                lines.Add(new TikzAskLine(from, to, m.Groups[3].Value.Trim()));
            }
            else rest.Add(line);
        }
        prose = Regex.Replace(string.Join("\n", rest), @"\n{3,}", "\n\n").Trim();
        return new TikzAskReply(prose, source, preamble, caption, lines);
    }

    /// <summary>Line numbers the model copied from the prompt: removed only when every line carries one.</summary>
    internal static string StripNumbers(string source)
    {
        var lines = source.Split('\n');
        var content = lines.Where(l => l.Trim().Length > 0).ToList();
        if (content.Count == 0 || !content.All(l => NumberPrefix.IsMatch(l))) return source;
        return string.Join("\n", lines.Select(l => NumberPrefix.Replace(l, "", 1)));
    }

    /// <summary>
    /// The preamble lines the proposal needs: library lines only, each once, and none the preamble
    /// already has (in any spelling of its list: <c>{arrows.meta, positioning}</c> covers both).
    /// </summary>
    public static IReadOnlyList<string> PreambleAdditions(IEnumerable<string> lines, string? preamble)
    {
        var have = LibrariesIn(preamble ?? "");
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!LibraryLine.IsMatch(line)) continue;
            var libs = LibrariesIn(line);
            if (libs.Count == 0 || libs.All(have.Contains)) continue;
            result.Add(line);
            have.UnionWith(libs);
        }
        return result;
    }

    private static HashSet<string> LibrariesIn(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"\\use(tikz|pgfplots)library\s*(?:\[[^\]]*\])?\s*\{([^{}]+)\}"))
            foreach (var lib in m.Groups[2].Value.Split(','))
                if (lib.Trim().Length > 0) set.Add(m.Groups[1].Value + ":" + lib.Trim());
        return set;
    }

    // ── The author's TikZ ───────────────────────────────────────────────

    private static readonly Regex AsksForLayout = new(@"\b(indent|re-?indent|format|reformat|tidy|clean ?up|align|whitespace|spacing|pretty)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The diff minimality check. Lines the model only re-indented or re-spaced go back to the
    /// author's (unless the author asked for layout): a change shows the lines the request needed,
    /// not a reformatted figure. Line endings follow the author's too.
    /// </summary>
    public static string KeepUntouchedLines(string before, string proposed, string message)
    {
        if (string.IsNullOrEmpty(before) || AsksForLayout.IsMatch(message ?? "")) return proposed;
        var diff = Diff(before, proposed);
        var result = new List<string>();
        for (var i = 0; i < diff.Count;)
        {
            if (diff[i].Op == ' ') { result.Add(diff[i].Text); i++; continue; }
            var removed = new List<string>();
            var added = new List<string>();
            while (i < diff.Count && diff[i].Op != ' ')
            {
                (diff[i].Op == '-' ? removed : added).Add(diff[i].Text);
                i++;
            }
            if (removed.Count == added.Count)
                for (var k = 0; k < added.Count; k++)
                    result.Add(Squash(removed[k]) == Squash(added[k]) ? removed[k] : added[k]);
            else
                result.AddRange(added);
        }
        var joined = string.Join("\n", result);
        return before.EndsWith('\n') && !joined.EndsWith('\n') ? joined + "\n" : joined;
    }

    private static string Squash(string line) => Regex.Replace(line, @"\s+", "");

    /// <summary>A line diff (longest common subsequence), removed lines before added ones in each hunk.</summary>
    public static IReadOnlyList<TikzDiffLine> Diff(string before, string after)
    {
        var a = SplitLines(before);
        var b = SplitLines(after);
        var n = a.Length;
        var m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var result = new List<TikzDiffLine>();
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && a[x] == b[y]) { result.Add(new(' ', a[x])); x++; y++; }
            else
            {
                // Collect the whole hunk: removed first, then added.
                var removed = new List<string>();
                var added = new List<string>();
                while ((x < n || y < m) && !(x < n && y < m && a[x] == b[y]))
                {
                    if (y >= m || (x < n && lcs[x + 1, y] >= lcs[x, y + 1])) removed.Add(a[x++]);
                    else added.Add(b[y++]);
                }
                result.AddRange(removed.Select(r => new TikzDiffLine('-', r)));
                result.AddRange(added.Select(s => new TikzDiffLine('+', s)));
            }
        }
        return result;
    }

    /// <summary>How many lines a change touched: per hunk, the larger of removed and added.</summary>
    public static int ChangedLines(string before, string after)
    {
        var diff = Diff(before, after);
        int total = 0, removed = 0, added = 0;
        foreach (var d in diff.Append(new TikzDiffLine(' ', "")))
        {
            if (d.Op == '-') removed++;
            else if (d.Op == '+') added++;
            else { total += Math.Max(removed, added); removed = added = 0; }
        }
        return total;
    }

    public static string[] SplitLines(string text)
    {
        var t = (text ?? "").Replace("\r\n", "\n");
        if (t.EndsWith('\n')) t = t[..^1];
        return t.Length == 0 ? Array.Empty<string>() : t.Split('\n');
    }
}
