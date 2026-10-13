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
/// code, lists, bibliography, page breaks, and CV blocks
/// (<c>personalInfo</c> / <c>cvSection</c> / <c>cvEntry</c>).
/// </summary>
public sealed class LmlTextParser : ILmlTextParser
{
    // MCP-59: an attribute group is "[" … "]" where a quoted value may hold "]" — a theorem titled
    // "Completeness of $C[0,1]$" ended its header at the first "]" and became a paragraph of raw text.
    private static readonly Regex BlockStartRegex = new(
        @"^@(?<type>[a-zA-Z][\w-]*)(?<attrs>(?:\[(?:""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^\]""'])*\])+)?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex InlineBlockStartRegex = new(
        @"^@(?<type>[a-zA-Z][\w-]*)(?<attrs>(?:\[(?:""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^\]""'])*\])+)?\s+(?<rest>.+)$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "algorithm", "footnote", "callout", "embed",
        "heading", "paragraph", "blockquote", "equation", "theorem",
        "abstract", "bibliography", "figure", "table", "code", "list",
        "toc", "pagebreak", "columnbreak", "document",
        // theorem environment aliases written as top-level markers
        "definition", "lemma", "proposition", "corollary", "remark",
        "example", "proof",
        // CV / résumé
        "personalInfo", "personalinfo", "personal-info", "personal_info",
        "cvSection", "cvsection", "cv-section", "cv_section",
        "cvEntry", "cventry", "cv-entry", "cv_entry",
        "photo",
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
        var titleFromHeading = false;

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
                // An optional {#id} is the heading's label (MCP-25: sec:md has a colon, which the
                // old pattern didn't take, so it printed in the title and refs to it dangled).
                var anchor = Regex.Match(text, @"\s*\{#([^\s{}]+)\}\s*$");
                if (anchor.Success) text = text[..anchor.Index];
                blocks.Add(MakeHeading(text, level, anchor.Success ? anchor.Groups[1].Value : null));
                if (title is null && level == 1) { title = text; titleFromHeading = true; }
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
                { var before = title; AppendBlock(blocks, type, attrsRaw, body, warnings, ref title); if (before is null && title is not null) titleFromHeading = true; }
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
                { var before = title; AppendBlock(blocks, type, attrsRaw, fullBody, warnings, ref title); if (before is null && title is not null) titleFromHeading = true; }
                continue;
            }

            // A header that starts like @type[ but can't be read: say so rather than silently making
            // a paragraph of raw markup (MCP-59).
            if (Regex.IsMatch(trimmed, @"^@[a-zA-Z][\w-]*\[") )
                warnings.Add($"Line {i + 1}: couldn't read the attributes of \"{trimmed[..Math.Min(trimmed.Length, 60)]}\" (an unclosed [ or quote?); it was kept as text.");

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
                // Soft-wrap plain multi-line prose the same way as @paragraph bodies.
                Content = new { text = NormalizeProse(string.Join("\n", paraLines)) },
            });
        }

        return new LmlTextParseResult
        {
            Title = title,
            TitleFromHeading = titleFromHeading,
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
                // Soft-wrap newlines from LML indent bodies become spaces (not <br>).
                var text = NormalizeProse(body);
                // Support "@heading[level=1] Title" where title was inline only
                if (string.IsNullOrEmpty(text) && attrs.TryGetValue("_positional0", out var pos))
                    text = NormalizeProse(pos);
                var heading = MakeHeading(text, level, attrs.GetValueOrDefault("id") ?? attrs.GetValueOrDefault("label"));
                // MCP-64: numbered=false (an unnumbered heading) was dropped; JSON took it, LML didn't.
                if (attrs.TryGetValue("numbered", out var num) && bool.TryParse(num, out var isNumbered) && !isNumbered)
                    heading = new LmlParsedBlock
                    {
                        Type = heading.Type,
                        Content = MergeContent(heading.Content, new Dictionary<string, object?> { ["numbered"] = false }),
                    };
                blocks.Add(heading);
                if (title is null && level == 1 && !string.IsNullOrWhiteSpace(text))
                    title = text;
                return;
            }

            case "paragraph":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "paragraph",
                    Content = new { text = NormalizeProse(body) },
                });
                return;

            case "blockquote":
            case "quote":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "blockquote",
                    Content = string.Equals(attrs.GetValueOrDefault("variant"), "verse", StringComparison.OrdinalIgnoreCase)
                        ? new
                        {
                            // MCP-10: a poem keeps its lines.
                            text = KeepLines(body),
                            attribution = attrs.GetValueOrDefault("attribution") ?? "",
                            variant = "verse",
                        }
                        : (object)new
                        {
                            text = NormalizeProse(body),
                            attribution = attrs.GetValueOrDefault("attribution") ?? "",
                            variant = attrs.GetValueOrDefault("variant"),
                        },
                });
                return;

            case "abstract":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "abstract",
                    Content = new { text = NormalizeProse(body) },
                });
                return;

            case "equation":
            {
                var mode = attrs.GetValueOrDefault("mode") ?? "display";
                var label = attrs.GetValueOrDefault("label") ?? "";
                // Soft-wrap LML lines in equation bodies; keep intentional \\ line breaks.
                blocks.Add(new LmlParsedBlock
                {
                    Type = "equation",
                    Content = new
                    {
                        latex = KeepLines(body),
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
                    // kind= as well as type= and the flag form (MCP-30: kind=claim was ignored).
                    theoremType = attrs.GetValueOrDefault("_positional0")
                        ?? attrs.GetValueOrDefault("type")
                        ?? attrs.GetValueOrDefault("kind")
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
                        text = NormalizeProse(body),
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

            // @title[title=…][author=…][date=…]
            //
            // Absent from the directive table, so it fell through to the
            // default and became a PARAGRAPH containing its own raw markup:
            //
            //   {"type":"paragraph","content":{"text":
            //     "@title[title=A Paper][author=El Yadini Salma][date=\\today]"}}
            //
            // This parser backs Ask Lilia's apply_lml, the tool it uses to
            // rewrite a whole document — so asking Lilia to restructure a paper
            // replaced its title block with a line of visible markup, losing the
            // title, the author and the date as structure (2026-09-10). The LML
            // exporter has always written this directive; nothing read it.
            case "title":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "title",
                    Content = new
                    {
                        title = attrs.GetValueOrDefault("title")
                                ?? (string.IsNullOrWhiteSpace(body) ? "" : body.Trim()),
                        author = attrs.GetValueOrDefault("author") ?? "",
                        date = attrs.GetValueOrDefault("date") ?? "",
                    },
                    Depth = 0,
                });
                return;

            case "figure":
            case "image":
                // A TikZ figure: the body is the picture's source (TikzFigure). Written by
                // the LML exporter with kind=tikz, and by Ask Lilia's figure skill as a
                // tikzpicture under @figure, which used to land in the caption.
                if (string.Equals(attrs.GetValueOrDefault("kind"), "tikz", StringComparison.OrdinalIgnoreCase)
                    || Lilia.Core.Blocks.TikzFigure.FindEnvironments(body).Count > 0)
                {
                    var tikz = new Dictionary<string, object?>
                    {
                        ["kind"] = Lilia.Core.Blocks.TikzFigure.Kind,
                        ["source"] = body.Trim('\n', '\r'),
                        ["caption"] = attrs.GetValueOrDefault("caption") ?? "",
                        ["label"] = attrs.GetValueOrDefault("label") ?? "",
                    };
                    if (attrs.GetValueOrDefault("position") is { Length: > 0 } pos) tikz["position"] = pos;
                    if (string.Equals(attrs.GetValueOrDefault("float"), "false", StringComparison.OrdinalIgnoreCase)) tikz["float"] = false;
                    blocks.Add(new LmlParsedBlock { Type = "figure", Content = tikz });
                    return;
                }
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

            // MCP-49: four types the editor and emitter support that LML couldn't write (they became
            // raw paragraphs: an @algorithm broke the compile, the others printed their own markup).
            case "algorithm":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "algorithm",
                    Content = new
                    {
                        title = attrs.GetValueOrDefault("title") ?? "",
                        caption = attrs.GetValueOrDefault("caption") ?? "",
                        label = attrs.GetValueOrDefault("label") ?? attrs.GetValueOrDefault("id") ?? "",
                        lineNumbers = attrs.TryGetValue("lineNumbers", out var ln) && bool.TryParse(ln, out var lnOn) && lnOn,
                        code = KeepLines(body),
                    },
                });
                return;

            case "footnote":
                blocks.Add(new LmlParsedBlock { Type = "footnote", Content = new { text = NormalizeProse(body) } });
                return;

            case "callout":
                blocks.Add(new LmlParsedBlock
                {
                    Type = "callout",
                    Content = new
                    {
                        text = NormalizeProse(body),
                        title = attrs.GetValueOrDefault("title") ?? "",
                        variant = attrs.GetValueOrDefault("variant") ?? attrs.GetValueOrDefault("_positional0") ?? "info",
                    },
                });
                return;

            case "embed":
                blocks.Add(new LmlParsedBlock { Type = "embed", Content = new { code = KeepLines(body) } });
                return;

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

            // ── CV / résumé ─────────────────────────────────────────────
            // Normalize aliases (personal-info, personal_info, personalinfo → personalInfo).
            case "personalinfo":
            case "personal-info":
            case "personal_info":
            {
                var name = attrs.GetValueOrDefault("name") ?? attrs.GetValueOrDefault("_positional0") ?? "";
                var email = attrs.GetValueOrDefault("email") ?? "";
                var phone = attrs.GetValueOrDefault("phone")
                    ?? attrs.GetValueOrDefault("phones")
                    ?? "";
                var location = attrs.GetValueOrDefault("location") ?? "";
                var homepage = attrs.GetValueOrDefault("homepage")
                    ?? attrs.GetValueOrDefault("url")
                    ?? attrs.GetValueOrDefault("website")
                    ?? "";
                // Body = professional headline (one line); optional headline= attr wins.
                var headline = attrs.GetValueOrDefault("headline")
                    ?? (string.IsNullOrWhiteSpace(body) ? "" : NormalizeProse(body));
                var extra = attrs.GetValueOrDefault("extra") ?? "";
                object[] phones = string.IsNullOrWhiteSpace(phone)
                    ? Array.Empty<object>()
                    : new object[] { new { number = phone.Trim() } };
                blocks.Add(new LmlParsedBlock
                {
                    Type = "personalInfo",
                    Content = new
                    {
                        name,
                        headline,
                        email,
                        phones,
                        homepage,
                        location,
                        socials = Array.Empty<object>(),
                        extra,
                    },
                });
                if (title is null && !string.IsNullOrWhiteSpace(name))
                    title = name.Trim();
                return;
            }

            case "cvsection":
            case "cv-section":
            case "cv_section":
            {
                var sectionTitle = attrs.GetValueOrDefault("title")
                    ?? attrs.GetValueOrDefault("_positional0")
                    ?? NormalizeProse(body);
                blocks.Add(new LmlParsedBlock
                {
                    Type = "cvSection",
                    Content = new { title = sectionTitle },
                });
                return;
            }

            case "cventry":
            case "cv-entry":
            case "cv_entry":
            {
                var period = attrs.GetValueOrDefault("period")
                    ?? attrs.GetValueOrDefault("date")
                    ?? attrs.GetValueOrDefault("dates")
                    ?? "";
                var role = attrs.GetValueOrDefault("role")
                    ?? attrs.GetValueOrDefault("title")
                    ?? attrs.GetValueOrDefault("position")
                    ?? attrs.GetValueOrDefault("_positional0")
                    ?? "";
                var org = attrs.GetValueOrDefault("org")
                    ?? attrs.GetValueOrDefault("organization")
                    ?? attrs.GetValueOrDefault("company")
                    ?? attrs.GetValueOrDefault("institution")
                    ?? "";
                var location = attrs.GetValueOrDefault("location") ?? "";
                var highlight = attrs.GetValueOrDefault("highlight") ?? "";
                var description = string.IsNullOrWhiteSpace(body) ? "" : NormalizeProse(body);
                blocks.Add(new LmlParsedBlock
                {
                    Type = "cvEntry",
                    Content = new
                    {
                        period,
                        role,
                        org,
                        location,
                        highlight,
                        description,
                        tech = Array.Empty<string>(),
                    },
                });
                return;
            }

            case "photo":
            {
                blocks.Add(new LmlParsedBlock
                {
                    Type = "photo",
                    Content = new
                    {
                        src = attrs.GetValueOrDefault("src") ?? "",
                        alt = attrs.GetValueOrDefault("alt") ?? "",
                        shape = attrs.GetValueOrDefault("shape") ?? "square",
                        size = attrs.TryGetValue("size", out var sz) && int.TryParse(sz, out var sizeN) ? sizeN : 64,
                        position = attrs.GetValueOrDefault("position") ?? "right",
                        border = attrs.TryGetValue("border", out var br) && int.TryParse(br, out var borderN) ? borderN : 0,
                    },
                });
                return;
            }

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

    /// <summary>An anonymous content object with extra fields, as a dictionary.</summary>
    private static Dictionary<string, object?> MergeContent(object content, Dictionary<string, object?> extra)
    {
        var merged = System.Text.Json.JsonSerializer.SerializeToElement(content).EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        foreach (var (k, v) in extra) merged[k] = v;
        return merged;
    }

    private static LmlParsedBlock MakeHeading(string text, int level, string? id = null)
    {
        level = Math.Clamp(level, 1, 6);
        text = NormalizeProse(text);
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
    /// Collapse soft line wraps from LML indented bodies into spaces.
    /// AI-generated LML is usually one phrase per line (~80 cols); those
    /// newlines are NOT paragraph breaks. Double newlines become a single
    /// space too for single-block prose (abstract/paragraph/theorem) — the
    /// editor treats each block as one flow; hard multi-paragraph needs
    /// separate blocks. Code/tables keep raw newlines elsewhere.
    /// </summary>
    public static string NormalizeProse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var s = body.Replace("\r\n", "\n").Replace('\r', '\n');
        // Soft wrap: any newline (with surrounding indent spaces) → single space
        s = Regex.Replace(s, @"[ \t]*\n[ \t]*", " ");
        // Collapse runs of spaces/tabs
        s = Regex.Replace(s, @"[ \t]{2,}", " ");
        return s.Trim();
    }

    /// <summary>
    /// A body whose line breaks mean something (MCP-10, 12 Oct): equation LaTeX, where a <c>%</c>
    /// comment runs to the end of its line, and verse. Collapsing them lost everything after a
    /// <c>%</c> and every verse line break. Only the LML indentation goes: the common leading
    /// indent, trailing spaces, and blank lines at either end.
    /// </summary>
    public static string KeepLines(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        var indent = lines.Where(l => l.Length > 0).Select(l => l.Length - l.TrimStart(' ', '\t').Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    /// <summary>
    /// Parse <c>[a=b, flag, title="x, y", theorem]</c> into a dictionary.
    /// Positional (unnamed) values are stored as <c>_positional0</c>, <c>_positional1</c>, …
    /// </summary>
    internal static Dictionary<string, string> ParseAttributes(string? attrsRaw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrsRaw)) return result;

        var raw = attrsRaw.Trim();

        // "[a=1][b=2]" — the form the LML exporter has always written. Only a
        // single group was ever accepted, so a directive with more than one
        // attribute group matched neither block regex, fell through to the
        // default, and became a PARAGRAPH containing its own raw markup. That
        // is what happened to every @title[title=…][author=…][date=…]
        // (2026-09-10), and this parser backs Ask Lilia's apply_lml, so asking
        // Lilia to rewrite a paper turned its title block into a line of
        // visible syntax.
        //
        // Groups are parsed one at a time: a "]" bounds a value, so a comma
        // inside one group cannot leak into the next.
        var groups = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        char? quote = null;     // inside "…" or '…', brackets are text (MCP-59)
        var escaped = false;
        foreach (var ch in raw)
        {
            if (quote is not null)
            {
                current.Append(ch);
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == quote) quote = null;
                continue;
            }
            if ((ch == '"' || ch == '\'') && depth > 0) { quote = ch; current.Append(ch); continue; }
            if (ch == '[' && depth++ == 0) continue;
            if (ch == ']' && --depth == 0) { groups.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        if (current.Length > 0) groups.Add(current.ToString());   // unterminated: take what there is

        if (groups.Count > 1)
        {
            foreach (var group in groups)
            {
                foreach (var (k, v) in ParseAttributes("[" + group + "]"))
                    result[k] = v;
            }
            return result;
        }

        var inner = groups.Count == 1 ? groups[0].Trim() : "";
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
                    // MCP-13: a backslash escapes only the quote or itself; "$\\alpha$" keeps its \\alpha.
                    if (inner[i] == '\\' && i + 1 < inner.Length && (inner[i + 1] == q || inner[i + 1] == '\\')) { i++; sb.Append(inner[i]); }
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
                        // MCP-13: a backslash escapes only the quote or itself; "$\\alpha$" keeps its \\alpha.
                        if (inner[i] == '\\' && i + 1 < inner.Length && (inner[i + 1] == q || inner[i + 1] == '\\')) { i++; sb.Append(inner[i]); }
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

    /// <summary>
    /// The cells of one pipe-table row (MCP-12, 12 Oct: every "|" split a cell, so $\|x\|$ became
    /// three cells and the table grew columns). A "|" is a separator only outside $…$ maths and
    /// `code`, and not when written as \|, which stands for a literal bar.
    /// </summary>
    internal static string[] SplitTableCells(string line)
    {
        var row = line.Trim();
        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|') && !row.EndsWith("\\|")) row = row[..^1];
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool inMath = false, inCode = false;
        for (var i = 0; i < row.Length; i++)
        {
            var c = row[i];
            if (c == '\\' && i + 1 < row.Length && row[i + 1] == '|' && !inMath && !inCode)
            {
                cell.Append('|'); i++;          // \| outside maths: a literal bar
                continue;
            }
            if (c == '\\' && i + 1 < row.Length) { cell.Append(c).Append(row[i + 1]); i++; continue; }
            if (c == '`' && !inMath) inCode = !inCode;
            else if (c == '$' && !inCode) inMath = !inMath;
            if (c == '|' && !inMath && !inCode)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }
            cell.Append(c);
        }
        cells.Add(cell.ToString().Trim());
        return cells.ToArray();
    }

    private static (string[] Headers, string[][] Rows) ParseMarkdownTable(string body)
    {
        var tableLines = body.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('|'))
            .ToList();

        if (tableLines.Count == 0)
            return (Array.Empty<string>(), Array.Empty<string[]>());

        static string[] SplitCells(string line) => SplitTableCells(line);

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
