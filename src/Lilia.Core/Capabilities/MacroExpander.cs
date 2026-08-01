namespace Lilia.Core.Capabilities;

/// <summary>
/// Rewrites a document's own macros into the maths they stand for, so a parser
/// that knows only standard LaTeX can read an equation that does not use only
/// standard LaTeX.
/// </summary>
/// <remarks>
/// <para><b>Why expansion rather than a smarter parser.</b> 26.2% of real
/// documents containing maths define a macro, and the names carry no fixed
/// meaning: <c>\R</c> is <c>\mathbb{R}</c> in one document and the number 8 in
/// another. No parser can be taught that. Expanding first turns a
/// document-specific problem into ordinary LaTeX, which the existing parser
/// already handles for the whole corpus.</para>
///
/// <para><b>Expansion is bounded, and that is not a detail.</b>
/// <c>\newcommand{\a}{\a}</c> is legal to write and loops forever when
/// expanded; so does a pair that reference each other. TeX itself dies on this
/// with a capacity error. A depth limit turns an authoring mistake into a
/// partially-expanded equation instead of a hung request, and the caller is
/// told it happened rather than being handed a quietly wrong result.</para>
/// </remarks>
public static class MacroExpander
{
    /// <summary>
    /// How many times the whole source may be rewritten.
    /// </summary>
    /// <remarks>
    /// Macros defined in terms of other macros are normal and rarely nest more
    /// than two or three deep; a self-referential one would never terminate.
    /// Eight is far above real usage and far below anything slow.
    /// </remarks>
    private const int MaxDepth = 8;

    /// <param name="Source">The expanded maths.</param>
    /// <param name="Expanded">How many macro uses were rewritten.</param>
    /// <param name="HitDepthLimit">
    /// True when expansion stopped early. The result is usable but incomplete,
    /// and saying so is the difference between a known gap and a silent one.
    /// </param>
    public sealed record Result(string Source, int Expanded, bool HitDepthLimit);

    public static Result Expand(string? source, IReadOnlyDictionary<string, MacroDefinition> macros)
    {
        if (string.IsNullOrEmpty(source) || macros.Count == 0)
            return new Result(source ?? "", 0, false);

        var current = source;
        var total = 0;

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var (next, count) = ExpandOnce(current, macros);
            total += count;

            // Nothing left that this document defines.
            if (count == 0) return new Result(next, total, false);

            current = next;
        }

        // Still finding macros after MaxDepth passes: almost certainly a cycle.
        return new Result(current, total, true);
    }

    private static (string Source, int Count) ExpandOnce(
        string source, IReadOnlyDictionary<string, MacroDefinition> macros)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var count = 0;
        var i = 0;

        while (i < source.Length)
        {
            if (source[i] != '\\')
            {
                result.Append(source[i++]);
                continue;
            }

            // Read the control sequence: \name for letters, or \x for a single
            // non-letter, which is how TeX lexes them.
            var start = i;
            i++;
            if (i < source.Length && char.IsAsciiLetter(source[i]))
            {
                while (i < source.Length && char.IsAsciiLetter(source[i])) i++;
            }
            else if (i < source.Length)
            {
                i++;
            }

            var name = source[start..i];

            if (!macros.TryGetValue(name, out var macro))
            {
                // Not ours. Emit verbatim — an unknown command is the parser's
                // business, and rewriting it here would hide it.
                result.Append(name);
                continue;
            }

            var arguments = new List<string>(macro.Arity);
            var cursor = i;
            var gotAll = true;

            for (var a = 0; a < macro.Arity; a++)
            {
                cursor = SkipWhitespace(source, cursor);
                if (cursor < source.Length && source[cursor] == '{')
                {
                    if (!TryReadGroup(source, cursor, out var argument, out cursor)) { gotAll = false; break; }
                    arguments.Add(argument);
                }
                else if (cursor < source.Length)
                {
                    // A single token is a legal argument: \frac12 means
                    // \frac{1}{2}, and people write macros that way too.
                    arguments.Add(source[cursor].ToString());
                    cursor++;
                }
                else
                {
                    gotAll = false;
                    break;
                }
            }

            if (!gotAll)
            {
                // Called with too few arguments — malformed. Leave it alone so
                // the parser reports it, rather than expanding a half-applied
                // macro into something that looks deliberate.
                result.Append(name);
                continue;
            }

            result.Append(Substitute(macro.Body, arguments));
            i = cursor;
            count++;
        }

        return (result.ToString(), count);
    }

    /// <summary>Replace <c>#1</c>…<c>#9</c> with the supplied arguments.</summary>
    /// <remarks>
    /// <para><b>The token boundary matters, and losing it corrupts silently.</b>
    /// A body of <c>\lvert#1\rvert</c> with the argument <c>x+1</c> must not
    /// become <c>\lvertx+1\rvert</c>: TeX ends a control word at the first
    /// non-letter, so that is no longer <c>\lvert</c> followed by <c>x</c> — it
    /// is a single undefined control sequence named <c>\lvertx</c>. The
    /// equation would then fail to parse, or worse, parse as something nobody
    /// wrote.</para>
    ///
    /// <para>A space is inserted only where TeX needs one: a control word
    /// immediately followed by an argument that starts with a letter. TeX
    /// discards that space when reading the control word, so nothing is
    /// changed except the boundary being preserved.</para>
    /// </remarks>
    private static string Substitute(string body, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) return body;

        var result = new System.Text.StringBuilder(body.Length);

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '#' && i + 1 < body.Length && char.IsAsciiDigit(body[i + 1]))
            {
                var index = body[i + 1] - '1';
                if (index >= 0 && index < arguments.Count)
                {
                    var argument = arguments[index];

                    if (argument.Length > 0 && char.IsAsciiLetter(argument[0]) && EndsWithControlWord(result))
                    {
                        result.Append(' ');
                    }

                    result.Append(argument);
                    i++;
                    continue;
                }
            }

            result.Append(body[i]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Whether the text so far ends in a control word — a backslash followed by
    /// one or more letters, which TeX would extend into whatever comes next.
    /// </summary>
    private static bool EndsWithControlWord(System.Text.StringBuilder text)
    {
        var i = text.Length - 1;
        if (i < 0 || !char.IsAsciiLetter(text[i])) return false;

        while (i >= 0 && char.IsAsciiLetter(text[i])) i--;

        return i >= 0 && text[i] == '\\';
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static bool TryReadGroup(string text, int open, out string content, out int next)
    {
        content = "";
        next = open;
        if (open >= text.Length || text[open] != '{') return false;

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
            {
                content = text[(open + 1)..i];
                next = i + 1;
                return true;
            }
        }

        return false;
    }
}
