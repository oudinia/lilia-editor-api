using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// A Markdown file's YAML front matter, as pandoc reads it: the title, author and date for the title block, and
/// <c>header-includes</c> (raw LaTeX for the preamble). The rest is the body.
///
/// <para>The launch bot's TeX Stack Exchange run (14 Nov 2026, #106): the front matter was read as text, and its
/// closing <c>---</c> under that text is Markdown's underlined-heading mark, so the whole block became one heading
/// ("title: … author: Me date: … header-includes: - …") and the isodate line never reached the preamble.</para>
///
/// <para>Only the YAML a front matter uses is read: <c>key: value</c> (quoted or not), a list under a key
/// (<c>- item</c>), and a block scalar (<c>key: |</c>). Anything else is left out of the metadata, never put in
/// the body.</para>
/// </summary>
public static class MarkdownFrontMatter
{
    public sealed record Result(string Body, string? Title, string? Author, string? Date, IReadOnlyList<string> HeaderIncludes)
    {
        public bool Found { get; init; }
    }

    private static readonly Regex KeyLine = new(@"^([A-Za-z][\w-]*)\s*:\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex ItemLine = new(@"^\s+-\s+(.*)$|^-\s+(.*)$", RegexOptions.Compiled);

    public static Result Read(string? markdown)
    {
        var text = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
        var lines = text.Split('\n');
        var first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0) first++;
        if (first >= lines.Length || lines[first].TrimEnd() != "---") return new Result(text, null, null, null, []);

        var close = -1;
        for (var i = first + 1; i < lines.Length; i++)
            if (lines[i].TrimEnd() is "---" or "...") { close = i; break; }
        // No closing line, or a rule followed by text that isn't YAML: not front matter.
        if (close < 0 || !lines.Skip(first + 1).Take(close - first - 1).Any(l => KeyLine.IsMatch(l))) return new Result(text, null, null, null, []);

        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        var block = false;   // inside "key: |" or "key: >"
        for (var i = first + 1; i < close; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (block && (line.StartsWith(' ') || line.StartsWith('\t')))
            {
                values[key!].Add(line.Trim());
                continue;
            }
            block = false;
            if (ItemLine.Match(line) is { Success: true } item && key is not null)
            {
                values[key].Add(Unquote(item.Groups[1].Success ? item.Groups[1].Value : item.Groups[2].Value));
                continue;
            }
            if (KeyLine.Match(line) is { Success: true } kv)
            {
                key = kv.Groups[1].Value;
                values[key] = [];
                var value = kv.Groups[2].Value.Trim();
                if (value is "|" or ">" or "|-" or ">-") block = true;
                else if (value.Length > 0) values[key].Add(Unquote(value));
            }
        }

        string? One(string name) => values.TryGetValue(name, out var v) && v.Count > 0 ? string.Join(" and ", v) : null;
        var includes = values.TryGetValue("header-includes", out var h)
            ? h.Select(StripRawLatexFence).Where(l => l.Length > 0).ToList()
            : [];
        var body = string.Join("\n", lines.Skip(close + 1));
        return new Result(body, One("title"), One("author"), One("date"), includes) { Found = true };
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\''))) v = v[1..^1];
        return v.Replace("\\\"", "\"");
    }

    // pandoc's raw-LaTeX marks around an include: ```{=latex} … ``` or `…`{=latex}.
    private static string StripRawLatexFence(string v)
    {
        v = v.Trim();
        if (v.StartsWith("```")) return "";
        var inline = Regex.Match(v, @"^`(.*)`\{=latex\}$");
        return inline.Success ? inline.Groups[1].Value.Trim() : v;
    }
}
