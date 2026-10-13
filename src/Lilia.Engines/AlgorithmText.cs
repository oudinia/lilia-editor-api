using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// An algorithm block as readable pseudocode, for a format that is not LaTeX (the Word export):
/// <c>for i = 1 to n do</c> / <c>x ← x + i</c> / <c>end for</c>, indented. Reads the three shapes
/// algorithm content takes — typed lines ({kind, text}), the exporter's ({indent, keyword, text,
/// comment}), and a code string of algorithmic commands. Word printed the code string as typed
/// (<c>\STATE $x \gets 0$</c>, 13 Oct sweep, LTX-20) and dropped typed lines altogether.
/// </summary>
public static class AlgorithmText
{
    private static readonly Regex Command = new(
        @"\\(?<c>STATE|IF|ELSIF|ELSEIF|ELSE|ENDIF|FORALL|FOR|ENDFOR|WHILE|ENDWHILE|REPEAT|UNTIL|LOOP|ENDLOOP|RETURN|REQUIRE|ENSURE|PRINT|COMMENT)\b",
        RegexOptions.Compiled);

    public static string ForWord(JsonElement content)
    {
        var lines = new List<string>();
        var depth = 0;

        void Add(string kind, string text, string? comment = null)
        {
            text = Text(text);
            var k = kind.ToLowerInvariant();
            if (k is "endif" or "endfor" or "endwhile" or "endloop" or "until" or "else" or "elsif" or "elseif")
                depth = Math.Max(0, depth - 1);
            var line = k switch
            {
                "if" => $"if {text} then",
                "elsif" or "elseif" => $"else if {text} then",
                "else" => "else",
                "endif" => "end if",
                "for" => $"for {text} do",
                "forall" => $"for all {text} do",
                "endfor" => "end for",
                "while" => $"while {text} do",
                "endwhile" => "end while",
                "repeat" => "repeat",
                "until" => $"until {text}",
                "loop" => "loop",
                "endloop" => "end loop",
                "return" => $"return {text}".TrimEnd(),
                "require" => $"Require: {text}",
                "ensure" => $"Ensure: {text}",
                "print" => $"print {text}",
                "comment" => $"▷ {text}",
                _ => text,
            };
            if (!string.IsNullOrWhiteSpace(comment)) line += "  ▷ " + Text(comment);
            lines.Add(new string(' ', depth * 2) + line);
            if (k is "if" or "elsif" or "elseif" or "else" or "for" or "forall" or "while" or "repeat" or "loop") depth++;
        }

        if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("lines", out var ls)
            && ls.ValueKind == JsonValueKind.Array && ls.GetArrayLength() > 0)
        {
            foreach (var l in ls.EnumerateArray())
            {
                if (l.ValueKind == JsonValueKind.String) { FromCode(l.GetString() ?? "", Add); continue; }
                if (l.ValueKind != JsonValueKind.Object) continue;
                var text = Str(l, "text");
                if (Str(l, "kind") is { Length: > 0 } kind) { Add(kind, text); continue; }
                // The exporter's shape: an explicit indent and a keyword in words.
                var indent = l.TryGetProperty("indent", out var iv) && iv.ValueKind == JsonValueKind.Number ? iv.GetInt32() : 0;
                var keyword = Str(l, "keyword");
                var comment = Str(l, "comment");
                var line = (keyword.Length > 0 ? keyword + " " : "") + Text(text);
                if (comment.Length > 0) line += "  ▷ " + Text(comment);
                lines.Add(new string(' ', indent * 2) + line.Trim());
            }
        }
        else
        {
            var code = Str(content, "code");
            if (code.Length == 0) code = Str(content, "text");
            if (code.Length == 0) code = Str(content, "content");
            FromCode(code, Add);
        }
        return string.Join("\n", lines);
    }

    /// <summary>A code string: algorithmic commands split it into steps; plain lines are steps too.</summary>
    private static void FromCode(string code, Action<string, string, string?> add)
    {
        if (!Command.IsMatch(code))
        {
            foreach (var l in code.Replace("\r\n", "\n").Split('\n'))
                if (l.Trim().Length > 0) add("statement", l.Trim(), null);
            return;
        }
        var matches = Command.Matches(code);
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var end = i + 1 < matches.Count ? matches[i + 1].Index : code.Length;
            var rest = code[(m.Index + m.Length)..end].Trim();
            var kind = m.Groups["c"].Value;
            string arg = rest, tail = "";
            // Commands with a condition take it in braces: \IF{…}, \FOR{…}, \COMMENT{…}.
            if (rest.StartsWith('{') && ClosingBrace(rest) is var close and > 0)
            {
                arg = rest[1..close];
                tail = rest[(close + 1)..].Trim();
            }
            add(kind == "STATE" ? "statement" : kind, arg, tail.Length > 0 ? null : null);
            if (tail.Length > 0) add("statement", tail, null);
        }
    }

    private static string Text(string s) =>
        ProseLatex.PlainForWord(Regex.Replace(s ?? "", @"\\TO\b", " to ").Replace("\\gets", "\\leftarrow"))
            .Replace("  ", " ").Trim();

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int ClosingBrace(string s)
    {
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }
}
