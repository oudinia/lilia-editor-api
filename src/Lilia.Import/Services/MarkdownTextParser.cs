using System.Text;
using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Pasted Markdown, or plain text (Markdown with no marks), as import elements, for the same
/// element → block mapping as LaTeX. Lilia's own inline marks are Markdown's (<c>**bold**</c>,
/// <c>*it*</c>, <c>`code`</c>, <c>~~strike~~</c>, <c>$math$</c>), so a paragraph's text stays as
/// written except where the two differ: <c>__x__</c> is bold here and underline in Lilia, a link is
/// <c>\href</c> in Lilia, and ChatGPT-style <c>\(x\)</c> is <c>$x$</c>.
///
/// <para>10 Nov 2026, paste → LaTeX phase 1 (lilia-launch-bot #60). Before this, Markdown pasted as
/// Lilia text lost its lists (one paragraph) and its <c>$$…$$</c> (text).</para>
/// </summary>
public static class MarkdownTextParser
{
    private static readonly Regex Fence = new(@"^\s{0,3}(`{3,}|~{3,})\s*([\w+#.-]*)", RegexOptions.Compiled);
    private static readonly Regex Atx = new(@"^\s{0,3}(#{1,6})\s+(.*?)(?:\s+#+)?\s*$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"^\s*!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)\s*$", RegexOptions.Compiled);

    public static List<ImportElement> Parse(string markdown)
    {
        var elements = new List<ImportElement>();
        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var order = 0;
        var listGroup = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            elements.Add(new ImportParagraph { Order = order++, Text = Inline(JoinLines(paragraph)) });
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // ```lang … ``` (or ~~~)
            if (Fence.Match(line) is { Success: true } fence)
            {
                FlushParagraph();
                var marker = fence.Groups[1].Value;
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal); i++)
                    code.Add(lines[i]);
                var lang = fence.Groups[2].Value;
                if (lang is "math" or "latex-math")
                    elements.Add(Equation(string.Join("\n", code), order++));
                else
                    elements.Add(new ImportCodeBlock
                    {
                        Order = order++,
                        Text = string.Join("\n", code),
                        Language = lang.Length > 0 ? lang : null,
                        DetectionReason = CodeBlockDetectionReason.StyleName,
                    });
                continue;
            }

            // $$ … $$ and \[ … \], on one line or several.
            if (trimmed.StartsWith("$$") || trimmed.StartsWith(@"\["))
            {
                var close = trimmed.StartsWith("$$") ? "$$" : @"\]";
                var body = trimmed[2..];
                var math = new StringBuilder();
                if (body.EndsWith(close, StringComparison.Ordinal) && body.Length >= close.Length)
                    math.Append(body[..^close.Length]);
                else
                {
                    math.Append(body);
                    for (i++; i < lines.Length; i++)
                    {
                        var l = lines[i].TrimEnd();
                        if (l.EndsWith(close, StringComparison.Ordinal)) { math.Append('\n').Append(l[..^close.Length]); break; }
                        math.Append('\n').Append(l);
                    }
                }
                FlushParagraph();
                elements.Add(Equation(math.ToString(), order++));
                continue;
            }

            if (Atx.Match(line) is { Success: true } atx)
            {
                FlushParagraph();
                elements.Add(new ImportHeading { Order = order++, Level = atx.Groups[1].Length, Text = Inline(atx.Groups[2].Value) });
                continue;
            }

            // Setext: a line of = or - under a paragraph line makes it a heading.
            if (paragraph.Count > 0 && Regex.IsMatch(trimmed, @"^(=+|-+)$"))
            {
                var text = Inline(JoinLines(paragraph));
                paragraph.Clear();
                elements.Add(new ImportHeading { Order = order++, Level = trimmed[0] == '=' ? 1 : 2, Text = text });
                continue;
            }

            // --- *** ___ on their own: a thematic break, which a paper doesn't print.
            if (Rule.IsMatch(line))
            {
                FlushParagraph();
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('>'); i++)
                    quote.Add(Regex.Replace(lines[i].Trim(), @"^>\s?", ""));
                i--;
                elements.Add(new ImportBlockquote { Order = order++, Text = Inline(JoinLines(quote)) });
                continue;
            }

            // | a | b |  over  |---|---|
            if (line.Contains('|') && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushParagraph();
                var rows = new List<List<ImportTableCell>> { Cells(line) };
                for (i += 2; i < lines.Length && lines[i].Contains('|') && lines[i].Trim().Length > 0; i++)
                    rows.Add(Cells(lines[i]));
                i--;
                var width = rows.Max(r => r.Count);
                foreach (var r in rows)
                    while (r.Count < width) r.Add(new ImportTableCell());
                elements.Add(new ImportTable { Order = order++, Rows = rows, HasHeaderRow = true });
                continue;
            }

            if (Image.Match(line) is { Success: true } image)
            {
                // A picture on the web can't be \includegraphics'd: keep it as a link, so the LaTeX compiles.
                FlushParagraph();
                var alt = image.Groups[1].Value.Trim();
                elements.Add(new ImportParagraph
                {
                    Order = order++,
                    Text = $@"\href{{{image.Groups[2].Value}}}{{{(alt.Length > 0 ? Inline(alt) : "image")}}}",
                });
                continue;
            }

            // A list: items until a line that isn't an item or its continuation. A blank line between
            // items keeps the list; a deeper item is a child of the one before.
            if (paragraph.Count == 0 && ListItem.Match(line) is { Success: true } first)
            {
                var group = ++listGroup;
                var ordered = char.IsDigit(first.Groups[2].Value[0]);
                var baseIndent = first.Groups[1].Value.Length;
                var top = new List<ImportListItem>();
                var stack = new List<(int Indent, ImportListItem Item)>();
                var rawOf = new Dictionary<ImportListItem, string>();
                for (; i < lines.Length; i++)
                {
                    var l = lines[i];
                    if (l.Trim().Length == 0)
                    {
                        if (i + 1 < lines.Length && ListItem.Match(lines[i + 1]) is { Success: true } nx
                            && nx.Groups[1].Value.Length >= baseIndent
                            && (nx.Groups[1].Value.Length > baseIndent || char.IsDigit(nx.Groups[2].Value[0]) == ordered))
                            continue;
                        break;
                    }
                    var m = ListItem.Match(l);
                    if (!m.Success)
                    {
                        // An indented line continues the item; anything else ends the list.
                        if (stack.Count > 0 && l.Length - l.TrimStart().Length > baseIndent)
                        {
                            var last = stack[^1].Item;
                            rawOf[last] += " " + l.Trim();
                            last.Text = Inline(rawOf[last]);
                            continue;
                        }
                        break;
                    }
                    var indent = m.Groups[1].Value.Length;
                    if (indent < baseIndent) break;
                    var isOrdered = char.IsDigit(m.Groups[2].Value[0]);
                    if (indent == baseIndent && isOrdered != ordered) break;
                    var item = new ImportListItem
                    {
                        Order = order++,
                        ListGroup = group,
                        IsNumbered = isOrdered,
                        ListMarker = m.Groups[2].Value,
                        Text = Inline(m.Groups[3].Value),
                    };
                    rawOf[item] = m.Groups[3].Value;
                    while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
                    item.Level = stack.Count;
                    if (stack.Count == 0) top.Add(item);
                    else stack[^1].Item.Children.Add(item);
                    stack.Add((indent, item));
                }
                i--;
                elements.AddRange(top);
                continue;
            }

            paragraph.Add(line);
        }
        FlushParagraph();
        // A text starting at ## printed "0.1" (#79): the shallowest heading becomes a section.
        return HeadingLevels.Promote(elements);
    }

    private static ImportEquation Equation(string latex, int order) => new()
    {
        Order = order,
        LatexContent = latex.Trim(),
        ConversionSucceeded = true,
        IsInline = false,
    };

    private static List<ImportTableCell> Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith(@"\|")) t = t[..^1];
        return Regex.Split(t, @"(?<!\\)\|").Select(c => new ImportTableCell { Text = Inline(c.Trim().Replace(@"\|", "|")) }).ToList();
    }

    /// <summary>A paragraph's lines as one: a line break inside a paragraph is a space.</summary>
    private static string JoinLines(IEnumerable<string> lines) =>
        string.Join(" ", lines.Select(l => l.Trim().TrimEnd('\\').TrimEnd()));

    private static readonly Regex Protected = new(@"(`+)(.+?)\1|\$\$.+?\$\$|(?<![\\$])\$(?!\s)[^$\n]+?(?<![\s\\])\$|\\\((.+?)\\\)", RegexOptions.Compiled);

    /// <summary>Markdown's inline marks in Lilia's form; code and maths are left exactly as written.</summary>
    public static string Inline(string text)
    {
        var sb = new StringBuilder();
        var at = 0;
        foreach (Match m in Protected.Matches(text))
        {
            sb.Append(Marks(text[at..m.Index]));
            // \(x\) is $x$; code and $…$ as they are.
            sb.Append(m.Groups[3].Success ? "$" + m.Groups[3].Value.Trim() + "$" : m.Value);
            at = m.Index + m.Length;
        }
        sb.Append(Marks(text[at..]));
        return sb.ToString();
    }

    private static string Marks(string s)
    {
        if (s.Length == 0) return s;
        // Links first, so an underscore in a URL is not italics.
        // A link's # and % are LaTeX's own characters: unescaped, a page's anchor (…/extended-syntax/#tables) broke the
        // compile, in a heading above all (the launch bot's #64 row 5, 11 Nov 2026). hyperref prints \# and \% as # and %.
        s = Regex.Replace(s, @"(?<!!)\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", m => $@"\href{{{UrlArg(m.Groups[2].Value)}}}{{{m.Groups[1].Value}}}");
        s = Regex.Replace(s, @"<(https?://[^>\s]+)>", m => $@"\url{{{UrlArg(m.Groups[1].Value)}}}");
        s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)\s]+)\)", m => $@"\href{{{m.Groups[2].Value}}}{{{(m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "image")}}}");
        var links = new List<string>();
        s = Regex.Replace(s, @"\\(?:href|url)\{[^{}]*\}", m => { links.Add(m.Value); return $"\u0001{links.Count - 1}\u0001"; });
        // __bold__ is bold in Markdown (underline in Lilia); _it_ is italics.
        s = Regex.Replace(s, @"(?<![\w\\])__(?=\S)(.+?)(?<=\S)__(?!\w)", "**$1**");
        s = Regex.Replace(s, @"(?<![\w\\_])_(?=[^\s_])(.+?)(?<=[^\s_\\])_(?![\w_])", "*$1*");
        // Markdown's backslash escapes: \* is a star, \_ an underscore, \# a hash.
        s = Regex.Replace(s, @"\\([*_\[\]#+!>`|])", "$1");
        return Regex.Replace(s, "\u0001(\\d+)\u0001", m => links[int.Parse(m.Groups[1].Value)]);
    }

    /// <summary>A URL as a \href or \url argument: # and % escaped (once), the rest as written.</summary>
    internal static string UrlArg(string url) => Regex.Replace(url, @"(?<!\\)([#%])", @"\$1");
}
