using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// The editor's algorithm lines, <c>{ indent, keyword, text, comment }</c>, as algorithmic LaTeX. The emitter read only
/// the LaTeX importer's shape (<c>{ kind, text }</c>), so every line the editor saved came out as a plain <c>\STATE</c>:
/// an algorithm written in Lilia lost its <c>\FOR</c> … <c>\ENDFOR</c> in the PDF, and its "end" rows printed as empty
/// steps (the launch bot's #64, 11 Nov 2026). Nesting comes from the indentation: a row at or left of an open block's
/// level closes it, with or without an "end" row, and anything still open is closed at the end, so the output always
/// balances.
/// </summary>
public static class AlgorithmLatex
{
    private enum Block { If, For, While, Repeat, Loop }

    /// <summary>True when the lines are the editor's shape (no <c>kind</c> on any of them).</summary>
    public static bool IsEditorShape(JsonElement lines) =>
        lines.ValueKind == JsonValueKind.Array && lines.GetArrayLength() > 0
        && lines.EnumerateArray().All(l => l.ValueKind == JsonValueKind.Object && !l.TryGetProperty("kind", out _));

    public static IEnumerable<string> FromEditorLines(JsonElement lines)
    {
        var open = new Stack<(Block Kind, int Indent)>();
        var output = new List<string>();

        void CloseTop() => output.Add(open.Pop().Kind switch
        {
            Block.If => @"\ENDIF",
            Block.For => @"\ENDFOR",
            Block.While => @"\ENDWHILE",
            Block.Loop => @"\ENDLOOP",
            _ => @"\UNTIL{true}", // a repeat with no until row
        });
        void CloseFrom(int indent, bool inclusive)
        {
            while (open.Count > 0 && (open.Peek().Indent > indent || (inclusive && open.Peek().Indent == indent))) CloseTop();
        }

        foreach (var line in lines.EnumerateArray())
        {
            string S(string name) => line.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
            var indent = line.TryGetProperty("indent", out var iv) && iv.ValueKind == JsonValueKind.Number ? Math.Max(0, iv.GetInt32()) : 0;
            var keyword = S("keyword").ToLowerInvariant();
            var text = S("text");
            var comment = S("comment");
            var note = comment.Length > 0 ? $@" \COMMENT{{{comment}}}" : "";

            switch (keyword)
            {
                case "end":
                    CloseFrom(indent, inclusive: false);
                    if (open.Count > 0 && open.Peek().Indent == indent) CloseTop();
                    continue;
                case "else" or "else if":
                    CloseFrom(indent, inclusive: false);
                    if (open.Count > 0 && open.Peek().Indent == indent && open.Peek().Kind == Block.If)
                        output.Add(keyword == "else" ? @"\ELSE" + note : $@"\ELSIF{{{text}}}" + note);
                    else
                        output.Add($@"\STATE \textbf{{{keyword}}} {text}".TrimEnd() + note);
                    continue;
                case "until":
                    CloseFrom(indent, inclusive: false);
                    if (open.Count > 0 && open.Peek().Indent == indent && open.Peek().Kind == Block.Repeat)
                    {
                        open.Pop();
                        output.Add($@"\UNTIL{{{text}}}" + note);
                    }
                    else output.Add($@"\STATE \textbf{{until}} {text}".TrimEnd() + note);
                    continue;
            }

            // Any other row ends the blocks at its level or deeper.
            CloseFrom(indent, inclusive: true);
            switch (keyword)
            {
                case "if": output.Add($@"\IF{{{text}}}" + note); open.Push((Block.If, indent)); break;
                case "for": output.Add($@"\FOR{{{text}}}" + note); open.Push((Block.For, indent)); break;
                case "foreach": output.Add($@"\FORALL{{{text}}}" + note); open.Push((Block.For, indent)); break;
                case "while": output.Add($@"\WHILE{{{text}}}" + note); open.Push((Block.While, indent)); break;
                case "repeat": output.Add(@"\REPEAT" + note); open.Push((Block.Repeat, indent)); break;
                case "do": output.Add(@"\LOOP" + note); open.Push((Block.Loop, indent)); break;
                case "return": output.Add((text.Length > 0 ? $@"\RETURN {text}" : @"\RETURN") + note); break;
                case "input": output.Add($@"\REQUIRE {text}".TrimEnd() + note); break;
                case "output": output.Add($@"\ENSURE {text}".TrimEnd() + note); break;
                case "break" or "continue" or "function" or "procedure":
                    output.Add($@"\STATE \textbf{{{keyword}}} {text}".TrimEnd() + note); break;
                default:
                    // A row with nothing in it (the editor's new empty row) prints nothing.
                    if (text.Length > 0) output.Add($@"\STATE {text}" + note);
                    else if (comment.Length > 0) output.Add($@"\STATE \COMMENT{{{comment}}}");
                    break;
            }
        }
        while (open.Count > 0) CloseTop();
        // algorithmic needs at least one step: an algorithm of empty rows printed one before.
        if (output.Count == 0) output.Add(@"\STATE");
        return output;
    }
}
