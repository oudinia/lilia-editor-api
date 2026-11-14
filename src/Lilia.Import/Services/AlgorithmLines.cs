using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// The editor's algorithm lines, <c>{ indent, keyword, text, comment }</c>, from the text of an LML <c>@algorithm</c>.
/// The LML reader kept that text only as <c>code</c>, which the editor doesn't read, so an imported or connector-written
/// algorithm opened as an empty block (the launch bot's #52 retest, 11 Nov 2026). Two shapes are read:
/// algorithmic commands (<c>\STATE x \gets 0</c>, <c>\IF{…}</c> … <c>\ENDIF</c>), nested by their openers; and plain
/// lines, nested by their indentation (two spaces a level), a leading keyword the editor knows, and a comment after
/// <c>//</c> or <c>▷</c>. The LML writer writes the plain shape.
/// </summary>
public static class AlgorithmLines
{
    private static readonly Regex Command = new(
        @"\\(REQUIRE|ENSURE|STATE|RETURN|PRINT|COMMENT|IF|ELSIF|ELSEIF|ELSE|ENDIF|FORALL|FOR|ENDFOR|WHILE|ENDWHILE|REPEAT|UNTIL|LOOP|ENDLOOP)\b",
        RegexOptions.Compiled);

    // The editor's keywords (AlgorithmNodeView), the two-word one first.
    private static readonly string[] Keywords =
    [
        "else if", "if", "else", "while", "foreach", "for", "repeat", "until", "do", "end", "return", "break",
        "continue", "function", "procedure", "input", "output",
    ];

    public static List<object> FromLml(string body)
    {
        body = (body ?? "").Replace("\r\n", "\n");
        return Command.IsMatch(body) ? FromCommands(body) : FromPlain(body);
    }

    private static List<object> FromCommands(string body)
    {
        var lines = new List<Line>();
        var indent = 0;
        var matches = Command.Matches(body);
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var kind = m.Groups[1].Value.ToLowerInvariant() switch { "elseif" => "elsif", "forall" => "for", var k => k };
            var end = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            var arg = body[(m.Index + m.Length)..end].Trim();
            if (arg.StartsWith('{') && arg.EndsWith('}')) arg = arg[1..^1].Trim();

            if (kind is "endif" or "endfor" or "endwhile" or "endloop" or "until" or "else" or "elsif")
                indent = Math.Max(0, indent - 1);
            var keyword = kind switch
            {
                "if" => "if", "elsif" => "else if", "else" => "else", "for" => "for", "while" => "while",
                "repeat" => "repeat", "until" => "until", "loop" => "do", "return" => "return",
                "require" => "input", "ensure" => "output",
                "endif" or "endfor" or "endwhile" or "endloop" => "end",
                _ => "",
            };
            var isComment = kind == "comment";
            var sameLine = lines.Count > 0
                && !body.AsSpan(matches[i - 1].Index, m.Index - matches[i - 1].Index).Contains('\n');
            // \STATE \RETURN x is one line: the empty \STATE gives way.
            if (sameLine && lines[^1] is { Keyword: "", Text: "", Comment: "" })
            {
                lines.RemoveAt(lines.Count - 1);
                sameLine = false;
            }
            // A \COMMENT on the same source line as the command before it is that line's comment.
            if (isComment && sameLine && lines[^1].Comment.Length == 0)
            {
                lines[^1] = lines[^1] with { Comment = arg };
                continue;
            }
            lines.Add(new Line(indent, keyword, isComment ? "" : arg, isComment ? arg : ""));
            if (kind is "if" or "elsif" or "else" or "for" or "while" or "repeat" or "loop") indent++;
        }
        return lines.Select(l => (object)new { indent = l.Indent, keyword = l.Keyword, text = l.Text, comment = l.Comment }).ToList();
    }

    private sealed record Line(int Indent, string Keyword, string Text, string Comment);

    private static List<object> FromPlain(string body)
    {
        var raw = body.Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (raw.Count == 0) return [];
        static int Lead(string l) => l.Replace("\t", "  ").Length - l.Replace("\t", "  ").TrimStart().Length;
        var common = raw.Min(Lead);

        var lines = new List<object>();
        foreach (var l in raw)
        {
            var indent = (Lead(l) - common) / 2;
            var text = l.Trim();
            var comment = "";
            var cut = text.IndexOf("//", StringComparison.Ordinal);
            if (cut < 0) cut = text.IndexOf('▷');
            if (cut >= 0)
            {
                comment = text[(cut + (text[cut] == '▷' ? 1 : 2))..].Trim();
                text = text[..cut].TrimEnd();
            }
            var keyword = "";
            foreach (var k in Keywords)
            {
                if (text.Equals(k, StringComparison.OrdinalIgnoreCase)
                    || (text.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase)))
                {
                    keyword = k;
                    text = text[k.Length..].TrimStart();
                    break;
                }
            }
            lines.Add(new { indent, keyword, text, comment });
        }
        return lines;
    }
}
