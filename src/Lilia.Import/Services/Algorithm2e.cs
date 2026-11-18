using System.Text;
using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// An algorithm written with algorithm2e (<c>\KwIn{…}</c>, <c>\For{…}{…}</c>, <c>\eIf{…}{…}{…}</c>, <c>\tcp{…}</c>,
/// statements ended by <c>\;</c>), read into the algorithm block's typed lines, which Lilia prints with its own
/// algorithm + algorithmic (launch-bot #138, 18 Nov 2026). The body was read as algorithmic: <c>\If</c> and <c>\For</c>
/// were half-converted and <c>\KwTo</c>, <c>\tcp</c>, <c>\nl</c>, <c>\;</c> stayed, so the PDF stopped at "Undefined
/// control sequence". Converting is safer than keeping algorithm2e: it can't be loaded beside Lilia's algorithm and
/// algorithmic, which every other algorithm block needs. A body this can't read (unbalanced braces) gives null, and the
/// algorithmic reading stands.
/// </summary>
public static class Algorithm2e
{
    /// <summary>algorithm2e's own commands; one of them (outside an algorithmic environment) marks the body as algorithm2e.</summary>
    private static readonly Regex Marker = new(
        @"\\(?:;|Kw[A-Z][A-Za-z]*|eIf|uIf|lIf|uElseIf|uElse|lElse|ElseIf|ForEach|ForAll|lFor|lForEach|lWhile|lRepeat|tcp\*?|tcc\*?|nl(?![A-Za-z@])|BlankLine|SetAlgo[A-Za-z]*|DontPrintSemicolon|SetKw[A-Za-z]*|LinesNumbered)"
        + @"|\\(?:If|For|While)\s*\{(?:[^{}]|\{[^{}]*\})*\}\s*\{",
        RegexOptions.Compiled);

    public static bool Is(string body) =>
        !Regex.IsMatch(body, @"\\begin\{algorithmic\}") && Marker.IsMatch(body);

    /// <summary>Whether the algorithm numbers its lines (\LinesNumbered, or \nl on some of them).</summary>
    public static bool NumbersLines(string body) => Regex.IsMatch(body, @"\\(?:LinesNumbered|nl)(?![A-Za-z@])");

    // Setup with no argument, dropped.
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "SetAlgoLined", "SetAlgoNoLine", "SetAlgoVlined", "SetAlgoNoEnd", "SetAlgoShortEnd", "SetAlgoLongEnd",
        "DontPrintSemicolon", "PrintSemicolon", "LinesNumbered", "LinesNotNumbered", "LinesNumberedHidden", "nl",
        "BlankLine", "Indm", "Indp", "Indmm", "Indpp", "ShowLn", "RestyleAlgo", "centering",
    };

    // Setup with one or more brace arguments, dropped with them (the count is the arguments).
    private static readonly Dictionary<string, int> DroppedWithArgs = new(StringComparer.Ordinal)
    {
        ["SetKwSty"] = 1, ["SetFuncSty"] = 1, ["SetArgSty"] = 1, ["SetDataSty"] = 1, ["SetCommentSty"] = 1,
        ["SetProcNameSty"] = 1, ["SetProcArgSty"] = 1, ["SetAlCapFnt"] = 1, ["SetAlCapNameFnt"] = 1, ["SetAlCapSty"] = 1,
        ["SetNlSty"] = 3, ["SetNlSkip"] = 1, ["IncMargin"] = 1, ["DecMargin"] = 1, ["SetInd"] = 2, ["SetVlineSkip"] = 1,
        ["SetAlgorithmName"] = 3, ["SetAlgoCaptionSeparator"] = 1, ["ResetInOut"] = 1, ["SetKwBlock"] = 3,
        ["SetKwRepeat"] = 3, ["SetKwIF"] = 8, ["SetKwFor"] = 4, ["SetKwSwitch"] = 9, ["SetStartEndCondition"] = 3,
        ["SetAlgoSkip"] = 1, ["SetAlgoInsideSkip"] = 1, ["SetAlgoRefName"] = 1, ["SetAlgoProcName"] = 2,
        ["SetAlgoFuncName"] = 2, ["SetAlgoCaptionLayout"] = 1, ["label"] = 1, ["caption"] = 1,
    };

    private sealed class Ctx
    {
        public readonly Dictionary<string, string> Words = new(StringComparer.Ordinal);     // \SetKw{\Cmd}{word}: \textbf{word}
        public readonly Dictionary<string, string> Functions = new(StringComparer.Ordinal); // \SetKwFunction{\F}{F}: \textsc{F}(args)
        public readonly Dictionary<string, string> Data = new(StringComparer.Ordinal);      // \SetKwData{\D}{d}: \textit{d}
        public readonly Dictionary<string, string> Inputs = new(StringComparer.Ordinal);    // \SetKwInOut{\In}{Input}
        public readonly Dictionary<string, (string Open, string End)> Programs = new(StringComparer.Ordinal); // \SetKwProg
    }

    private sealed class Unreadable : Exception { }

    /// <summary>The typed lines of an algorithm2e body, or null when it can't be read.</summary>
    public static List<ImportAlgorithmLine>? Lines(string body)
    {
        try
        {
            var src = Regex.Replace(body.Replace("\r\n", "\n"), @"(?<!\\)%.*$", "", RegexOptions.Multiline);
            var ctx = new Ctx();
            var lines = new List<ImportAlgorithmLine>();
            Walk(src, ctx, lines);
            return lines.Count > 0 ? lines : null;
        }
        catch (Unreadable)
        {
            return null;
        }
    }

    private static void Walk(string s, Ctx ctx, List<ImportAlgorithmLine> lines)
    {
        var text = new StringBuilder();
        var kind = "statement";

        void Flush(string? comment = null)
        {
            var t = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
            text.Clear();
            if (t.Length == 0 && comment is null && kind == "statement") return;
            if (t.Length == 0 && comment is not null && kind == "statement") Add(lines, ctx, "comment", comment);
            else lines.Add(new ImportAlgorithmLine { Kind = kind, Text = t, Comment = comment is null ? "" : Inline(comment, ctx) });
            kind = "statement";
        }

        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '$')
            {
                // Maths verbatim: \; inside it is a space, not the end of a statement.
                var dbl = i + 1 < s.Length && s[i + 1] == '$';
                var close = dbl ? s.IndexOf("$$", i + 2, StringComparison.Ordinal) : NextDollar(s, i + 1);
                if (close < 0) throw new Unreadable();
                var end = close + (dbl ? 2 : 1);
                text.Append(InMaths(s[i..end], ctx));
                i = end;
                continue;
            }
            if (c == '{')
            {
                var close = Close(s, i);
                text.Append(s, i, close - i + 1);
                i = close + 1;
                continue;
            }
            if (c == '}') throw new Unreadable();
            if (c != '\\') { text.Append(c); i++; continue; }
            if (i + 1 >= s.Length) { i++; continue; }
            var n = s[i + 1];
            if (n == ';') { Flush(); i += 2; continue; }
            if (n == '\\') { Flush(); i += 2; continue; }
            if (n == '(')
            {
                var close = s.IndexOf(@"\)", i + 2, StringComparison.Ordinal);
                if (close < 0) throw new Unreadable();
                text.Append(s, i, close + 2 - i);
                i = close + 2;
                continue;
            }
            if (!char.IsAsciiLetter(n)) { text.Append(s, i, 2); i += 2; continue; }

            var j = i + 1;
            while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
            var name = s[(i + 1)..j];
            var star = j < s.Length && s[j] == '*';
            if (star && name is "tcp" or "tcc") j++;

            // Definitions.
            if (name is "SetKw" or "SetKwFunction" or "SetKwData" or "SetKwInOut" or "SetKwInput" or "SetKwHangingKw")
            {
                var (cmd, _) = Arg(s, ref j);
                var (word, _) = Arg(s, ref j);
                var key = cmd.Trim().TrimStart('\\');
                switch (name)
                {
                    case "SetKwFunction": ctx.Functions[key] = word; break;
                    case "SetKwData": ctx.Data[key] = word; break;
                    case "SetKwInOut" or "SetKwInput": ctx.Inputs[key] = word; break;
                    default: ctx.Words[key] = word; break;
                }
                i = j;
                continue;
            }
            if (name == "SetKwProg")
            {
                var (cmd, _) = Arg(s, ref j);
                var (open, _) = Arg(s, ref j);
                Arg(s, ref j);
                var (end, _) = Arg(s, ref j);
                ctx.Programs[cmd.Trim().TrimStart('\\')] = (open, end);
                i = j;
                continue;
            }
            if (Dropped.Contains(name)) { i = SkipOptional(s, j); continue; }
            if (DroppedWithArgs.TryGetValue(name, out var count))
            {
                j = SkipOptional(s, j);
                for (var k = 0; k < count; k++) Arg(s, ref j);
                i = j;
                continue;
            }

            switch (name)
            {
                case "KwIn" or "KwData":
                    Flush(); Add(lines, ctx, "require", Arg(s, ref j).Text); break;
                case "KwOut" or "KwResult":
                    Flush(); Add(lines, ctx, "ensure", Arg(s, ref j).Text); break;
                case "If" or "lIf" or "uIf":
                {
                    Flush();
                    var cond = Arg(s, ref j).Text;
                    Add(lines, ctx, "if", cond);
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    if (name != "uIf") Add(lines, ctx, "endif", "");
                    break;
                }
                case "ElseIf" or "uElseIf" or "lElseIf":
                {
                    Flush();
                    Reopen(lines);
                    Add(lines, ctx, "elsif", Arg(s, ref j).Text);
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    if (name != "uElseIf") Add(lines, ctx, "endif", "");
                    break;
                }
                case "Else" or "uElse" or "lElse":
                {
                    Flush();
                    Reopen(lines);
                    Add(lines, ctx, "else", "");
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    if (name != "uElse") Add(lines, ctx, "endif", "");
                    break;
                }
                case "eIf" or "leIf":
                {
                    Flush();
                    Add(lines, ctx, "if", Arg(s, ref j).Text);
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    Add(lines, ctx, "else", "");
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    Add(lines, ctx, "endif", "");
                    break;
                }
                case "For" or "lFor" or "ForEach" or "lForEach" or "ForAll" or "lForAll":
                {
                    Flush();
                    Add(lines, ctx, "for", (name.Contains("Each") ? "each " : name.Contains("All") ? "all " : "") + Arg(s, ref j).Text);
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    Add(lines, ctx, "endfor", "");
                    break;
                }
                case "While" or "lWhile":
                {
                    Flush();
                    Add(lines, ctx, "while", Arg(s, ref j).Text);
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    Add(lines, ctx, "endwhile", "");
                    break;
                }
                case "Repeat" or "lRepeat":
                {
                    Flush();
                    var until = Arg(s, ref j).Text;
                    Add(lines, ctx, "repeat", "");
                    Walk(Arg(s, ref j).Text, ctx, lines);
                    Add(lines, ctx, "until", until);
                    break;
                }
                case "Return" or "KwRet":
                {
                    Flush();
                    var k = j;
                    while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                    if (k < s.Length && s[k] == '{') Add(lines, ctx, "return", Arg(s, ref j).Text);
                    else kind = "return"; // \KwRet $x$\; : the rest of the statement
                    break;
                }
                case "tcp" or "tcc":
                {
                    j = SkipOptional(s, j);
                    var comment = Arg(s, ref j).Text;
                    if (Regex.Replace(text.ToString(), @"\s+", "").Length > 0 || kind != "statement") Flush(comment);
                    else Add(lines, ctx, "comment", comment);
                    break;
                }
                case "KwTo": text.Append(" to "); break;
                case "KwDownTo" or "KwDownto": text.Append(" down to "); break;
                case "KwFrom": text.Append(" from "); break;
                case "KwTrue": text.Append(@"\textbf{true}"); break;
                case "KwFalse": text.Append(@"\textbf{false}"); break;
                case "FuncSty" or "ProcNameSty": text.Append(@"\textsc{").Append(Arg(s, ref j).Text).Append('}'); break;
                case "KwSty": text.Append(@"\textbf{").Append(Arg(s, ref j).Text).Append('}'); break;
                case "DataSty": text.Append(@"\textit{").Append(Arg(s, ref j).Text).Append('}'); break;
                case "ArgSty" or "CommentSty" or "ProcArgSty": text.Append(Arg(s, ref j).Text); break;
                default:
                    if (ctx.Functions.TryGetValue(name, out var fn))
                    {
                        var k = j;
                        while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                        text.Append(@"\textsc{").Append(fn).Append('}');
                        if (k < s.Length && s[k] == '{') text.Append('(').Append(Arg(s, ref j).Text).Append(')');
                    }
                    else if (ctx.Data.TryGetValue(name, out var data)) text.Append(@"\textit{").Append(data).Append('}');
                    else if (ctx.Words.TryGetValue(name, out var word)) text.Append(@"\textbf{").Append(word).Append("} ");
                    else if (ctx.Inputs.TryGetValue(name, out var label))
                    {
                        Flush();
                        Add(lines, ctx, "statement", $@"\textbf{{{label}:}} " + Arg(s, ref j).Text);
                    }
                    else if (ctx.Programs.TryGetValue(name, out var prog))
                    {
                        Flush();
                        Add(lines, ctx, "statement", $@"\textbf{{{prog.Open}}} " + Arg(s, ref j).Text);
                        Walk(Arg(s, ref j).Text, ctx, lines);
                        if (prog.End.Trim().Length > 0) Add(lines, ctx, "statement", $@"\textbf{{{prog.End}}}");
                    }
                    else if (name.StartsWith("Kw", StringComparison.Ordinal) && name.Length > 2)
                        text.Append(@"\textbf{").Append(name[2..]).Append("} "); // an algorithm2e keyword not met before
                    else
                    {
                        text.Append(s, i, j - i); // ordinary LaTeX (\gets, \textbf, \leftarrow …)
                        i = j;
                        continue;
                    }
                    break;
            }
            i = j;
        }
        Flush();
    }

    private static void Add(List<ImportAlgorithmLine> lines, Ctx ctx, string kind, string text) =>
        lines.Add(new ImportAlgorithmLine { Kind = kind, Text = Inline(text, ctx) });

    /// <summary>A condition, an argument or a comment with algorithm2e's inline commands read (\KwTo, \FuncSty, \F{…}).</summary>
    private static string Inline(string text, Ctx ctx)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var tmp = new List<ImportAlgorithmLine>();
        Walk(text, ctx, tmp);
        return string.Join(" ", tmp.Select(l => l.Text)).Trim();
    }

    /// <summary>Maths as written, but with the algorithm's own names (\SetKwFunction …) and \KwTo read: Lilia doesn't define them.</summary>
    private static string InMaths(string math, Ctx ctx)
    {
        if (ctx.Functions.Count + ctx.Data.Count + ctx.Words.Count == 0 && !math.Contains(@"\Kw", StringComparison.Ordinal)) return math;
        return Regex.Replace(math, @"\\([A-Za-z]+)(?![A-Za-z@])(\s*\{(?:[^{}]|\{[^{}]*\})*\})?", m =>
        {
            var name = m.Groups[1].Value;
            var arg = m.Groups[2].Success ? m.Groups[2].Value.Trim()[1..^1] : null;
            if (ctx.Functions.TryGetValue(name, out var fn)) return $@"\textsc{{{fn}}}" + (arg is null ? "" : $"({arg})");
            var tail = m.Groups[2].Success ? m.Groups[2].Value : "";
            if (ctx.Data.TryGetValue(name, out var d)) return $@"\textit{{{d}}}" + tail;
            if (ctx.Words.TryGetValue(name, out var w)) return $@"\textbf{{{w}}}" + tail;
            return name switch
            {
                "KwTo" => @"\text{ to }" + tail,
                "KwTrue" => @"\textbf{true}" + tail,
                "KwFalse" => @"\textbf{false}" + tail,
                _ => m.Value,
            };
        });
    }

    /// <summary>An else after an if that was closed (\lIf{…}{…}\lElse{…}, \If{…}{…}\Else{…}): the if goes on.</summary>
    private static void Reopen(List<ImportAlgorithmLine> lines)
    {
        if (lines.Count > 0 && lines[^1].Kind == "endif") lines.RemoveAt(lines.Count - 1);
    }

    /// <summary>The next brace argument (spaces and an optional [..] before it skipped); j moves past it.</summary>
    private static (string Text, int End) Arg(string s, ref int j)
    {
        var k = SkipOptional(s, j);
        while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
        if (k >= s.Length || s[k] != '{') throw new Unreadable();
        var close = Close(s, k);
        j = close + 1;
        // A trailing \; after a block's argument belongs to nothing.
        return (s[(k + 1)..close], close);
    }

    private static int SkipOptional(string s, int j)
    {
        var k = j;
        while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
        if (k < s.Length && s[k] == '[')
        {
            var close = s.IndexOf(']', k);
            if (close < 0) throw new Unreadable();
            return close + 1;
        }
        return j;
    }

    private static int Close(string s, int open)
    {
        var depth = 0;
        for (var k = open; k < s.Length; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '{') depth++;
            else if (s[k] == '}' && --depth == 0) return k;
        }
        throw new Unreadable();
    }

    private static int NextDollar(string s, int from)
    {
        for (var k = from; k < s.Length; k++)
        {
            if (s[k] == '\\') { k++; continue; }
            if (s[k] == '$') return k;
        }
        return -1;
    }
}
