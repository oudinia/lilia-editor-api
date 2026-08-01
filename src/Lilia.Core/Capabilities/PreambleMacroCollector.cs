using System.Text.RegularExpressions;

namespace Lilia.Core.Capabilities;

/// <summary>A macro the document defines for itself.</summary>
/// <param name="Name">With its leading backslash, e.g. <c>\R</c>.</param>
/// <param name="Arity">How many arguments it takes. Zero for a plain shorthand.</param>
/// <param name="Body">The expansion, verbatim.</param>
/// <param name="Form">Which spelling defined it — <c>newcommand</c>, <c>def</c>, …</param>
public sealed record MacroDefinition(string Name, int Arity, string Body, string Form);

/// <summary>
/// Collects the macros a document defines in its own preamble.
///
/// <para><b>Why this is not optional.</b> Measured across 20,602 TeX.SE posts,
/// <b>26.2%</b> of those containing maths also define a macro. More than one
/// document in four cannot have its equations understood in isolation.</para>
///
/// <para>And the names collide with nothing and everything: <c>\R</c> appears in
/// the corpus meaning <c>\mathbb{R}</c>, <c>\mathbbm{R}</c>, a hand-built
/// <c>\mbox{$I\!\!R$}</c> — and the number <b>8</b>. <c>\x</c> means
/// <c>\mathbf{x}</c>, <c>\thepage</c>, <c>0.872</c> and <c>.1</c> in four
/// different documents. There is no fact of the matter about what <c>\x</c>
/// means, so no catalogue can ever hold it. The only correct answer comes from
/// reading the document.</para>
///
/// <para><b>What it unblocks, in two places at once.</b> The capability report
/// stops flagging <c>\x</c> on documents that define <c>\x</c> — currently the
/// largest single group of commands nothing can resolve. And the math parser
/// gets the macro map it needs: neither KaTeX nor Temml persists definitions
/// across render calls, so the document's macros must be collected once and
/// handed in, which is precisely this.</para>
/// </summary>
public static partial class PreambleMacroCollector
{
    /// <summary>
    /// Every macro defined in the given LaTeX source.
    /// </summary>
    /// <remarks>
    /// Later definitions win, because that is what TeX does: a
    /// <c>\renewcommand</c> after a <c>\newcommand</c> replaces it, and a
    /// document that defines the same name twice ends up with the second.
    /// </remarks>
    public static IReadOnlyDictionary<string, MacroDefinition> Collect(string? source)
    {
        var macros = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(source)) return macros;

        var text = StripComments(source);

        foreach (Match match in DefinitionStart().Matches(text))
        {
            var form = match.Groups["form"].Value;
            var index = match.Index + match.Length;

            // \newcommand{\name} and \newcommand\name are both legal, and both
            // appear in the corpus.
            string name;
            if (index < text.Length && text[index] == '{')
            {
                if (!TryReadGroup(text, index, out var braced, out index)) continue;
                name = braced.Trim();
            }
            else
            {
                var m = CommandName().Match(text, index);
                if (!m.Success || m.Index != index) continue;
                name = m.Value;
                index = m.Index + m.Length;
            }

            if (!name.StartsWith('\\') || name.Length < 2) continue;

            var arity = 0;

            if (form == "def")
            {
                // \def\name#1#2{body} — arity is written as parameter tokens
                // between the name and the body, not as a bracketed count.
                while (index < text.Length && text[index] == '#')
                {
                    index += 2; // skip #1
                    arity++;
                }
            }
            else
            {
                index = SkipWhitespace(text, index);

                // [n] argument count, then optionally [default] for the first
                // argument. Only the first bracket is the count.
                if (index < text.Length && text[index] == '[')
                {
                    if (TryReadBracket(text, index, out var countText, out var afterCount)
                        && int.TryParse(countText.Trim(), out var parsed))
                    {
                        arity = parsed;
                        index = afterCount;

                        index = SkipWhitespace(text, index);
                        if (index < text.Length && text[index] == '['
                            && TryReadBracket(text, index, out _, out var afterDefault))
                        {
                            index = afterDefault;
                        }
                    }
                }
            }

            index = SkipWhitespace(text, index);
            if (index >= text.Length || text[index] != '{') continue;
            if (!TryReadGroup(text, index, out var body, out _)) continue;

            // Later wins, as TeX does.
            macros[name] = new MacroDefinition(name, arity, body, form);
        }

        // \DeclareMathOperator{\argmax}{arg\,max} — a macro by another name, and
        // one the corpus uses 83 times.
        foreach (Match match in DeclareMathOperator().Matches(text))
        {
            var name = match.Groups["name"].Value.Trim();
            if (!name.StartsWith('\\') || name.Length < 2) continue;
            macros[name] = new MacroDefinition(name, 0, match.Groups["body"].Value, "DeclareMathOperator");
        }

        return macros;
    }

    /// <summary>
    /// Whether the document defines this command itself.
    /// </summary>
    public static bool Defines(IReadOnlyDictionary<string, MacroDefinition> macros, string command) =>
        macros.ContainsKey("\\" + command.TrimStart('\\'));

    /// <summary>
    /// Remove TeX comments so a commented-out definition is not collected.
    /// </summary>
    /// <remarks>
    /// <c>%</c> comments to end of line unless escaped as <c>\%</c>. Getting
    /// this wrong in the lenient direction means reporting macros a document
    /// does not have, and the resolver would then treat a genuinely missing
    /// command as defined — a false all-clear, which is the one kind of wrong
    /// answer this whole plan exists to avoid.
    /// </remarks>
    internal static string StripComments(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);

        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\') { i++; continue; }   // escaped char, including \%
                if (line[i] == '%') { line = line[..i]; break; }
            }
            result.Append(line).Append('\n');
        }

        return result.ToString();
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    /// <summary>Read a brace group, honouring nesting and escapes.</summary>
    private static bool TryReadGroup(string text, int open, out string content, out int next)
    {
        content = "";
        next = open;
        if (open >= text.Length || text[open] != '{') return false;

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }       // \{ and \} are literals
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
            {
                content = text[(open + 1)..i];
                next = i + 1;
                return true;
            }
        }

        // Unbalanced. Better to collect nothing than to guess where it ended.
        return false;
    }

    private static bool TryReadBracket(string text, int open, out string content, out int next)
    {
        content = "";
        next = open;
        var close = text.IndexOf(']', open);
        if (open >= text.Length || text[open] != '[' || close < 0) return false;

        content = text[(open + 1)..close];
        next = close + 1;
        return true;
    }

    /// <summary>
    /// The four spellings that define a macro, plus their starred forms.
    /// </summary>
    /// <remarks>
    /// <c>\def</c> is included because measurement said so: it appears in
    /// <b>9.5%</b> of corpus posts, nearly as often as <c>\newcommand</c>. An
    /// earlier count put it at exactly 0.0% — a clean zero that turned out to be
    /// Postgres treating the backslash in a LIKE pattern as an escape. Dropping
    /// <c>\def</c> on that number would have missed 1,962 posts.
    /// </remarks>
    [GeneratedRegex(@"\\(?<form>newcommand|renewcommand|providecommand|def)\*?")]
    private static partial Regex DefinitionStart();

    /// <summary>
    /// A control sequence at exactly the scan position.
    /// </summary>
    /// <remarks>
    /// <c>\G</c>, not <c>^</c>. In .NET <c>^</c> anchors to the start of the
    /// input even when matching from an offset, so an unbraced
    /// <c>\newcommand\foo{…}</c> found nothing and the definition was dropped.
    /// <c>\G</c> anchors where the scan begins, which is what was meant.
    /// </remarks>
    [GeneratedRegex(@"\G(?:\\[a-zA-Z]+|\\.)")]
    private static partial Regex CommandName();

    [GeneratedRegex(@"\\DeclareMathOperator\*?\s*\{(?<name>[^}]*)\}\s*\{(?<body>[^}]*)\}")]
    private static partial Regex DeclareMathOperator();
}
