using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// Maths Ask Lilia reads (18 Oct): a photo or screenshot sent as an image attachment, or text pasted
/// from somewhere Lilia cannot convert by itself (Word's linear maths, Unicode like "x² + y² = r²").
/// <c>AfterBlockId</c>: where the editor would insert it, for the paragraphs around it.
/// </summary>
public sealed record MathAskInput(string? Text = null, string? AfterBlockId = null);

/// <summary>
/// What Ask Lilia read: one entry per formula, each checked by TeX with the document's own preamble
/// before the author sees it. <c>Attempts</c>: 1, or 2 when a check failed and the error went back
/// to the model.
/// </summary>
public sealed record MathAskProposal(IReadOnlyList<MathAskFormula> Formulas, int Attempts);

/// <summary>
/// One formula. <c>Display</c>: an equation of its own (true) or maths inside a sentence (false).
/// <c>Ok</c>: TeX printed it with this document's preamble; when false, <c>Error</c> says why.
/// </summary>
public sealed record MathAskFormula(string Latex, string? Name, bool Display, bool Ok, string? Error = null);

/// <summary>A model reply, taken apart: the formulas in order, and the prose around them.</summary>
public sealed record MathAskReply(string Prose, IReadOnlyList<MathAskReplyFormula> Formulas);

public sealed record MathAskReplyFormula(string Latex, string? Name, bool Display);

/// <summary>
/// Ask Lilia reading maths (18 Oct): the prompt and how its reply is read. Pure: <see cref="MathAskRunner"/>
/// calls the model and TeX; <see cref="AskLiliaService"/> brings the gate, the model and the metering.
/// </summary>
public static partial class MathAsk
{
    public const string SkillId = "lilia-equation";

    /// <summary>At most this many formulas from one photo: a whole page is a document import, not this.</summary>
    public const int MaxFormulas = 12;

    private const int MaxMacros = 60;
    private const int MaxTextChars = 4000;

    /// <summary>
    /// The system prompt. <paramref name="macros"/>: the shortcuts this document's maths may use
    /// (Lilia's own and the document's preamble), so the reading uses \R where the author would.
    /// </summary>
    public static string SystemPrompt(IReadOnlyList<KeyValuePair<string, string>> macros, string? paragraphBefore, string proficiency)
    {
        var sb = new StringBuilder();
        sb.AppendLine(proficiency).AppendLine();
        sb.AppendLine("""
            You read mathematics and write it as LaTeX for Lilia, a LaTeX editor. The author sends a photo,
            a screenshot or a scan (printed or handwritten), or text pasted from elsewhere (Word's linear
            maths, Unicode symbols, plain text). Write exactly what is there; never solve, simplify or
            complete it. If a symbol is unreadable, give your best reading and say which one in one short
            sentence.

            REPLY FORMAT, exactly:
            Name: <a short name for the formula, e.g. Quadratic formula; or "-" if it has none>
            Display: <equation, if it stands on its own line; inline, if it sits inside a sentence>
            ```latex
            <the LaTeX, without $, \[ \] or \begin{equation}>
            ```
            One such entry per formula, in reading order, at most 12. A system of equations, or lines
            aligned on = signs, is ONE formula: use \begin{aligned} ... \end{aligned} or \begin{cases}.
            After the entries, at most two short sentences for the author, only if needed (an unclear
            symbol, or no maths in the image). If there is no mathematics at all, write no entries and
            say so.

            LATEX — amsmath and amssymb are loaded. Use \frac, \sqrt, \sum_{i=1}^{n}, \int_a^b, \lim_{x \to 0},
            \mathbb{R}, \vec{v}, \hat{x}, \left( \right) for tall brackets; \operatorname{name} for an
            operator that has no command. Write \cdot only where a dot is printed. Text inside maths:
            \text{...}. No \label, no \tag, no comments.
            """);
        if (macros.Count > 0)
        {
            sb.AppendLine("SHORTCUTS this document defines (prefer them when they print what is shown):");
            foreach (var (name, body) in macros.Take(MaxMacros))
                sb.Append("  ").Append(name).Append(" = ").AppendLine(body);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(paragraphBefore))
            sb.AppendLine("The text just before where it will go, for its notation only:")
              .AppendLine(Cap(paragraphBefore.Trim(), 1200));
        return sb.ToString();
    }

    /// <summary>
    /// The shortcuts a formula may use in this document: its own preamble's first (they are what the
    /// author writes), then Lilia's that it does not redefine. Not the editor-only siunitx stand-ins.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Shortcuts(string? customPreamble)
    {
        var all = Lilia.Engines.MathMacros.ForEditor(customPreamble);
        var own = all.Where(kv => Lilia.Engines.MathMacros.BuiltIn.TryGetValue(kv.Key, out var b)
                ? b != kv.Value
                : !Lilia.Engines.MathMacros.EditorStandIns.ContainsKey(kv.Key)).ToList();
        var lilia = Lilia.Engines.MathMacros.BuiltIn.Where(kv => own.All(o => o.Key != kv.Key));
        return own.Concat(lilia).ToList();
    }

    /// <summary>The author's message: what they typed, the pasted text, or the default for a photo.</summary>
    public static string UserMessage(string? typed, string? pasted)
    {
        var sb = new StringBuilder(string.IsNullOrWhiteSpace(typed) ? "Read the maths." : typed.Trim());
        if (!string.IsNullOrWhiteSpace(pasted))
            sb.Append("\n\nPasted:\n").Append(Cap(pasted.Trim(), MaxTextChars));
        return sb.ToString();
    }

    /// <summary>The second try, when TeX did not print one or more formulas.</summary>
    public static string RetryMessage(IEnumerable<(int Index, string Error)> failures)
    {
        var sb = new StringBuilder("TeX could not print these, with this document's preamble:\n");
        foreach (var (i, e) in failures) sb.Append("  formula ").Append(i + 1).Append(": ").AppendLine(e);
        sb.Append("Reply again with ALL the formulas, in the same format, fixed.");
        return sb.ToString();
    }

    [GeneratedRegex(@"```(?:latex|tex)?[ \t]*\r?\n(?<body>[\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex Fence();

    [GeneratedRegex(@"^\s*Name\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex NameLine();

    [GeneratedRegex(@"^\s*Display\s*:\s*(?<v>.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DisplayLine();

    /// <summary>
    /// Takes a reply apart. Each fenced block is a formula; the Name and Display lines before it (and
    /// after the previous one) are its own. Delimiters the model added anyway are taken off.
    /// </summary>
    public static MathAskReply Parse(string text)
    {
        text ??= "";
        var formulas = new List<MathAskReplyFormula>();
        var prose = new StringBuilder();
        var at = 0;
        foreach (Match m in Fence().Matches(text))
        {
            var head = text[at..m.Index];
            at = m.Index + m.Length;
            var latex = Unwrap(m.Groups["body"].Value);
            if (latex.Length == 0 || formulas.Count >= MaxFormulas) continue;
            var name = NameLine().Matches(head).LastOrDefault()?.Groups["v"].Value.Trim();
            if (name is "-" or "" or "—") name = null;
            var display = DisplayLine().Matches(head).LastOrDefault()?.Groups["v"].Value.Trim().ToLowerInvariant();
            formulas.Add(new MathAskReplyFormula(latex, name, display is null || !display.StartsWith("inline")));
            AppendProse(prose, head);
        }
        AppendProse(prose, text[at..]);
        return new MathAskReply(prose.ToString().Trim(), formulas);
    }

    private static void AppendProse(StringBuilder prose, string part)
    {
        var rest = DisplayLine().Replace(NameLine().Replace(part, ""), "").Trim();
        if (rest.Length > 0) prose.AppendLine(rest);
    }

    /// <summary>The LaTeX without the delimiters the model was told not to write: $…$, $$…$$, \[…\], \(…\), equation(*).</summary>
    public static string Unwrap(string latex)
    {
        var s = latex.Trim();
        static string? Strip(string s, string open, string close) =>
            s.StartsWith(open, StringComparison.Ordinal) && s.EndsWith(close, StringComparison.Ordinal) && s.Length >= open.Length + close.Length
                ? s[open.Length..^close.Length].Trim() : null;
        s = Strip(s, "$$", "$$") ?? Strip(s, @"\[", @"\]") ?? Strip(s, @"\(", @"\)")
            ?? (Strip(s, "$", "$") is { } inner && !inner.Contains('$') ? inner : null)
            ?? Strip(s, @"\begin{equation*}", @"\end{equation*}") ?? Strip(s, @"\begin{equation}", @"\end{equation}") ?? s;
        return Regex.Replace(s, @"\\label\{[^}]*\}", "").Trim();
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
