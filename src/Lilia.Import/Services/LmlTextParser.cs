using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Text LML parser (LML spec v1):
/// <code>
/// @blocktype[key=value, flag, title="quoted"]
///   indented body lines
/// </code>
/// Supports headings, paragraphs, equations, theorems, abstracts, tables,
/// code, lists, bibliography, and page breaks.
/// </summary>
public sealed class LmlTextParser : ILmlTextParser
{
    private static readonly Regex BlockStartRegex = new(
        @"^@(?<type>[a-zA-Z][\w-]*)(?<attrs>\[[^\]]*\])?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex InlineBlockStartRegex = new(
        @"^@(?<type>[a-zA-Z][\w-]*)(?<attrs>\[[^\]]*\])?\s+(?<rest>.+)$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "heading", "paragraph", "blockquote", "equation", "theorem",
        "abstract", "bibliography", "figure", "table", "code", "list",
        "toc", "pagebreak", "columnbreak", "document",
        // theorem environment aliases written as top-level markers
        "definition", "lemma", "proposition", "corollary", "remark",
        "example", "proof",
    };

    public bool LooksLikeTextLml(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var trimmed = source.TrimStart();
        // JSON Lilia export
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[')) return false;
        // Explicit block markers
        if (trimmed.StartsWith('@')) return true;
        // Markdown-style heading or block markers somewhere near the top
        using var reader = new StringReader(source);
        for (var i = 0; i < 40; i++)
        {
            var line = reader.ReadLine();
            if (line is null) break;
            var t = line.TrimStart();
            if (t.StartsWith('@') && t.Length > 1 && char.IsLetter(t[1])) return true;
            if (Regex.IsMatch(t, @"^#{1,6}\s+\S")) return true;
        }
        return false;
    }

    public LmlTextParseResult Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new LmlTextParseResult();
        }

        // Normalize newlines; keep original indentation semantics.
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<LmlParsedBlock>();
        var warnings = new List<string>();
        string? title = null;

        // Skip optional @document header metadata
        var i = 0;
        if (i < lines.Length && lines[i].TrimStart().StartsWith("@document", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            while (i < lines.Length)
            {
                var meta = lines[i];
                if (string.IsNullOrWhiteSpace(meta)) { i++; break; }
                if (meta.TrimStart().StartsWith('@') || meta.TrimStart().StartsWith('#')) break;
                var m = Regex.Match(meta.Trim(), @"^(?<k>\w+)\s*:\s*(?<v>.+)$");
                if (m.Success && m.Groups["k"].Value.Equals("title", StringComparison.OrdinalIgnoreCase))
                    title = m.Groups["v"].Value.Trim();
                i++;
            }
        }

        while (i < lines.Length)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();

            // Skip blank lines and comments between blocks
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//"))
            {
                i++;
                continue;
            }

            // Markdown-style heading: # Title
            var mdHeading = Regex.Match(trimmed, @"^(#{1,6})\s+(.+)$");
            if (mdHeading.Success)
            {
                var level = mdHeading.Groups[1].Value.Length;
                var text = mdHeading.Groups[2].Value.Trim();
                // strip optional {#id}
                text = Regex.Replace(text, @"\s*\{#[\w-]+\}\s*$", "");
                blocks.Add(MakeHeading(text, level));
                if (title is null && level == 1) title = text;
                i++;
                continue;
            }

            // Horizontal rule
            if (trimmed is "---" or "***")
            {
                blocks.Add(new LmlParsedBlock { Type = "pageBreak", Content = new { } });
                i++;
                continue;
            }

            // @block[attrs] on its own line, body on following indented lines
            var start = BlockStartRegex.Match(trimmed);
            if (start.Success)
            {
                var type = start.Groups["type"].Value;
                var attrsRaw = start.Groups["attrs"].Success ? start.Groups["attrs"].Value : null;
                i++;
                var (body, next) = ReadIndentedBody(lines, i);
                i = next;
                AppendBlock(blocks, type, attrsRaw, body, warnings, ref title);
                continue;
            }

            // @block[attrs] rest-of-line as body start (inline form)
            var inline = InlineBlockStartRegex.Match(trimmed);
            if (inline.Success)
            {
                var type = inline.Groups["type"].Value;
                var attrsRaw = inline.Groups["attrs"].Success ? inline.Groups["attrs"].Value : null;
                var first = inline.Groups["rest"].Value;
                i++;
                var (body, next) = ReadIndentedBody(lines, i);
                i = next;
                var fullBody = string.IsNullOrEmpty(body) ? first : first + "\n" + body;
                AppendBlock(blocks, type, attrsRaw, fullBody, warnings, ref title);
                continue;
            }

            // Bare @type without attrs and without body markers — treat remaining
            // non-@ lines as a paragraph (or continue until next @).
            if (trimmed.StartsWith('@') && trimmed.Length > 1 && char.IsLetter(trimmed[1]))
            {
                // Unknown one-liner like @toc
                var bareType = trimmed[1..].Split('[', ' ', '\t')[0];
                if (bareType.Equals("toc", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(new LmlParsedBlock { Type = "tableofcontents", Content = new { } });
                    i++;
                    continue;
                }
                if (bareType.Equals("pagebreak", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(new LmlParsedBlock { Type = "pageBreak", Content = new { } });
                    i++;
                    continue;
                }
            }

            // Plain paragraph: accumulate until blank or next @block
            var paraLines = new List<string> { trimmed };
            i++;
            while (i < lines.Length)
            {
                var nextLine = lines[i];
                var nextTrim = nextLine.Trim();
                if (string.IsNullOrWhiteSpace(nextTrim)) break;
                if (nextTrim.StartsWith('@') && nextTrim.Length > 1 && char.IsLetter(nextTrim[1])) break;
                if (Regex.IsMatch(nextTrim, @"^#{1,6}\s+")) break;
                paraLines.Add(nextTrim);
                i++;
            }
            blocks.Add(new LmlParsedBlock
            {
                Type = "paragraph",
                Content = new { text = string.Join("\n", paraLines) },
            });
        }

        return new LmlTextParseResult
        {
            Title = title,
            Blocks = blocks,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Read consecutive body lines: prefer 2+ space indent; also accept
    /// continuation lines that are not a new top-level @block.
    /// </summary>
    private static (string Body, int NextIndex) ReadIndentedBody(string[] lines, int start)
    {
        var sb = new StringBuilder();
        var i = start;
        var sawAny = false;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Blank line: keep if already collecting (paragraph spacing inside block)
            if (string.IsNullOrWhiteSpace(line))
            {
                // Peek ahead: if next non-empty is a new block, stop; else keep blank.
                var j = i + 1;
                while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
                if (j >= lines.Length) { i = j; break; }
                var peek = lines[j];
                if (IsTopLevelBlockStart(peek))
                {
                    // Leave the blank line for the outer loop; stop body.
                    break;
                }
                if (sawAny)
                {
                    sb.AppendLine();
                    i++;
                    continue;
                }
                // Leading blanks before any content — skip
                i++;
                continue;
            }

            // Indented content (spec: 2 spaces)
            if (line.StartsWith("  ") || line.StartsWith("\t"))
            {
                var content = line.StartsWith("\t") ? line[1..] : line[2..];
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(content.TrimEnd());
                sawAny = true;
                i++;
                continue;
            }

            // Non-indented: only continue if not a new block marker
            if (IsTopLevelBlockStart(line))
                break;

            // Allow unindented body for robustness (AI often omits indent)
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line.TrimEnd());
            sawAny = true;
            i++;
        }

        return (sb.ToString().TrimEnd(), i);
    }

    private static bool IsTopLevelBlockStart(string line)
    {
        var t = line.TrimStart();
        if (string.IsNullOrEmpty(t)) return false;
        if (t.StartsWith("//")) return false;
        if (Regex.IsMatch(t, @"^#{1,6}\s+\S")) return true;
        if (t is "---" or "***") return true;
        if (t.StartsWith('@') && t.Length > 1 && char.IsLetter(t[1]))
        {
            // Nested markers inside bibliography body: @cite[...] is NOT a top-level block
            if (t.StartsWith("@cite", StringComparison.OrdinalIgnoreCase)) return false;
            if (t.StartsWith("@ref", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        return false;
    }

    private static void AppendBlock(
        List<LmlParsedBlock> blocks,
        string rawType,
        string? attrsRaw,
        string body,
        List<string> warnings,
        ref string? title)
    {
        var type = rawType.ToLowerInvariant();
        var attrs = ParseAttributes(attrsRaw);

        switch (type)
        {
            case "document":
                // Already handled at top; ignore residual
                return;

            case "heading":
            {
                var level = 1;
                if (attrs.TryGetValue("level", out var lv) && int.TryParse(lv, out var parsed))
                    level = Math.Clamp(parsed, 1, 6);
                var text = body.Trim();
                // Support "@heading[level=1] Title" where title was inline only
                if (string.IsNullOrEmpty(text) && attrs.TryGetValue("_positional0", out var pos))
                    text = pos;
                blocks.Add(MakeHeading(text, level, attrs.GetValueOrDefault("id") ?? attrs.GetValueOrDefault("label")));
                if (title is null && level == 1 && !string.IsNullOrWhiteSpace(text))
                    title = text;
                return;
            }

            case "paragraph":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "paragraph",
                    Content = new { text = body.Trim() },
                });
                return;

            case "blockquote":
            case "quote":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "blockquote",
                    Content = new
                    {
                        text = body.Trim(),
                        attribution = attrs.GetValueOrDefault("attribution") ?? "",
                    },
                });
                return;

            case "abstract":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "abstract",
                    Content = new { text = body.Trim() },
                });
                return;

            case "equation":
            {
                var mode = attrs.GetValueOrDefault("mode") ?? "display";
                var label = attrs.GetValueOrDefault("label") ?? "";
                blocks.Add(new LmlParsedBlock
                {
                    Type = "equation",
                    Content = new
                    {
                        latex = body.Trim(),
                        equationMode = string.Equals(mode, "inline", StringComparison.OrdinalIgnoreCase) ? "inline" : "display",
                        label,
                        numbered = !string.IsNullOrEmpty(label),
                    },
                });
                return;
            }

            case "theorem":
            case "definition":
            case "lemma":
            case "proposition":
            case "corollary":
            case "remark":
            case "example":
            case "proof":
            {
                // @theorem[theorem, title="…", label="…"]
                // or @proof / @remark as type alias
                string theoremType;
                if (type == "theorem")
                {
                    theoremType = attrs.GetValueOrDefault("_positional0")
                        ?? attrs.GetValueOrDefault("type")
                        ?? "theorem";
                }
                else
                {
                    theoremType = type;
                }
                theoremType = theoremType.ToLowerInvariant();
                blocks.Add(new LmlParsedBlock
                {
                    Type = "theorem",
                    Content = new
                    {
                        text = body.Trim(),
                        theoremType,
                        title = attrs.GetValueOrDefault("title") ?? "",
                        label = attrs.GetValueOrDefault("label") ?? "",
                    },
                });
                return;
            }

            case "table":
            {
                var (headers, rows) = ParseMarkdownTable(body);
                blocks.Add(new LmlParsedBlock
                {
                    Type = "table",
                    Content = new
                    {
                        caption = attrs.GetValueOrDefault("caption") ?? "",
                        label = attrs.GetValueOrDefault("label") ?? "",
                        headers,
                        rows,
                    },
                });
                return;
            }

            case "code":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "code",
                    Content = new
                    {
                        code = body.TrimEnd(),
                        language = attrs.GetValueOrDefault("lang")
                            ?? attrs.GetValueOrDefault("language")
                            ?? attrs.GetValueOrDefault("_positional0")
                            ?? "",
                        caption = attrs.GetValueOrDefault("caption") ?? "",
                        label = attrs.GetValueOrDefault("label") ?? "",
                    },
                });
                return;

            case "list":
            {
                var ordered = attrs.ContainsKey("ordered")
                    && !string.Equals(attrs["ordered"], "false", StringComparison.OrdinalIgnoreCase);
                var items = new List<string>();
                foreach (var line in body.Split('\n'))
                {
                    var m = Regex.Match(line.Trim(), @"^([-*]|\d+\.)\s+(.+)$");
                    if (m.Success) items.Add(m.Groups[2].Value.Trim());
                    else if (!string.IsNullOrWhiteSpace(line)) items.Add(line.Trim());
                }
                blocks.Add(new LmlParsedBlock
                {
                    Type = "list",
                    Content = new { items, ordered },
                });
                return;
            }

            case "figure":
            case "image":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "figure",
                    Content = new
                    {
                        src = attrs.GetValueOrDefault("src") ?? "",
                        alt = attrs.GetValueOrDefault("alt") ?? "",
                        caption = string.IsNullOrWhiteSpace(body) ? (attrs.GetValueOrDefault("caption") ?? "") : body.Trim(),
                        label = attrs.GetValueOrDefault("label") ?? "",
                        width = attrs.TryGetValue("width", out var w) && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var wd)
                            ? wd
                            : (double?)null,
                    },
                });
                return;

            case "bibliography":
            {
                // Body is free-text cites for the bibliography block content.
                // Prefer keeping as a single bibliography block with text field.
                blocks.Add(new LmlParsedBlock
                {
                    Type = "bibliography",
                    Content = new { text = body.Trim() },
                });
                return;
            }

            case "toc":
            case "tableofcontents":
                blocks.Add(new LmlParsedBlock { Type = "tableofcontents", Content = new { } });
                return;

            case "pagebreak":
            case "page_break":
            case "divider":
                blocks.Add(new LmlParsedBlock { Type = "pageBreak", Content = new { } });
                return;

            case "columnbreak":
                blocks.Add(new LmlParsedBlock { Type = "columnBreak", Content = new { } });
                return;

            default:
                warnings.Add($"Unknown block type @{rawType}; imported as paragraph.");
                var fallback = string.IsNullOrWhiteSpace(body)
                    ? $"@{rawType}"
                    : $"@{rawType}\n{body.Trim()}";
                blocks.Add(new LmlParsedBlock
                {
                    Type = "paragraph",
                    Content = new { text = fallback },
                });
                return;
        }
    }

    private static LmlParsedBlock MakeHeading(string text, int level, string? id = null)
    {
        level = Math.Clamp(level, 1, 6);
        if (string.IsNullOrEmpty(id))
        {
            return new LmlParsedBlock
            {
                Type = "heading",
                Content = new { text, level },
            };
        }
        return new LmlParsedBlock
        {
            Type = "heading",
            Content = new { text, level, label = id },
        };
    }

    /// <summary>
    /// Parse <c>[a=b, flag, title="x, y", theorem]</c> into a dictionary.
    /// Positional (unnamed) values are stored as <c>_positional0</c>, <c>_positional1</c>, …
    /// </summary>
    internal static Dictionary<string, string> ParseAttributes(string? attrsRaw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrsRaw)) return result;

        var inner = attrsRaw.Trim();
        if (inner.StartsWith('[')) inner = inner[1..];
        if (inner.EndsWith(']')) inner = inner[..^1];
        inner = inner.Trim();
        if (inner.Length == 0) return result;

        var positional = 0;
        var i = 0;
        while (i < inner.Length)
        {
            // skip whitespace and commas
            while (i < inner.Length && (char.IsWhiteSpace(inner[i]) || inner[i] == ',')) i++;
            if (i >= inner.Length) break;

            // Read token until = or , (respecting quotes)
            var start = i;
            string key;
            if (inner[i] == '"' || inner[i] == '\'')
            {
                // quoted positional
                var q = inner[i];
                i++;
                var sb = new StringBuilder();
                while (i < inner.Length && inner[i] != q)
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length) { i++; sb.Append(inner[i]); }
                    else sb.Append(inner[i]);
                    i++;
                }
                if (i < inner.Length) i++; // closing quote
                result[$"_positional{positional++}"] = sb.ToString();
                continue;
            }

            while (i < inner.Length && inner[i] != '=' && inner[i] != ',' && !char.IsWhiteSpace(inner[i]))
                i++;
            key = inner[start..i].Trim();

            // skip spaces
            while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;

            if (i < inner.Length && inner[i] == '=')
            {
                i++; // =
                while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
                string value;
                if (i < inner.Length && (inner[i] == '"' || inner[i] == '\''))
                {
                    var q = inner[i];
                    i++;
                    var sb = new StringBuilder();
                    while (i < inner.Length && inner[i] != q)
                    {
                        if (inner[i] == '\\' && i + 1 < inner.Length) { i++; sb.Append(inner[i]); }
                        else sb.Append(inner[i]);
                        i++;
                    }
                    if (i < inner.Length) i++;
                    value = sb.ToString();
                }
                else
                {
                    var vStart = i;
                    while (i < inner.Length && inner[i] != ',') i++;
                    value = inner[vStart..i].Trim();
                }
                if (!string.IsNullOrEmpty(key))
                    result[key] = value;
            }
            else
            {
                // flag or positional identifier
                if (!string.IsNullOrEmpty(key))
                {
                    // treat as positional if it looks like a type name / free token;
                    // also store as boolean flag key=true
                    result[$"_positional{positional++}"] = key;
                    result[key] = "true";
                }
            }
        }

        return result;
    }

    private static (string[] Headers, string[][] Rows) ParseMarkdownTable(string body)
    {
        var tableLines = body.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('|'))
            .ToList();

        if (tableLines.Count == 0)
            return (Array.Empty<string>(), Array.Empty<string[]>());

        static string[] SplitCells(string line) =>
            line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

        var allRows = new List<string[]>();
        foreach (var line in tableLines)
        {
            var cells = SplitCells(line);
            // separator row |---|---|
            if (cells.All(c => Regex.IsMatch(c, @"^:?-+:?$")))
                continue;
            allRows.Add(cells);
        }

        if (allRows.Count == 0)
            return (Array.Empty<string>(), Array.Empty<string[]>());

        var headers = allRows[0];
        var data = allRows.Skip(1).ToArray();
        return (headers, data);
    }
}
