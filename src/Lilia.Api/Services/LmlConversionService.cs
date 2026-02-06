using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// Service for converting LML (Lilia Markup Language) to various output formats.
/// </summary>
public interface ILmlConversionService
{
    string ConvertToLatex(string lmlContent, LmlConversionOptions? options = null);
    string ConvertToHtml(string lmlContent, LmlConversionOptions? options = null);
    string ConvertToMarkdown(string lmlContent, LmlConversionOptions? options = null);
}

public class LmlConversionOptions
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? PaperSize { get; set; } = "a4paper";
    public string? FontSize { get; set; } = "11pt";
    public string? FontFamily { get; set; } = "charter";
    public bool IncludePreamble { get; set; } = true;
}

public partial class LmlConversionService : ILmlConversionService
{
    public string ConvertToLatex(string lmlContent, LmlConversionOptions? options = null)
    {
        options ??= new LmlConversionOptions();
        var lines = lmlContent.Split('\n');
        var output = new StringBuilder();
        var documentMeta = ParseDocumentMeta(lines);

        if (options.IncludePreamble)
        {
            // LaTeX preamble
            var paperSize = documentMeta.GetValueOrDefault("paperSize", options.PaperSize ?? "a4paper");
            var fontSize = documentMeta.GetValueOrDefault("fontSize", options.FontSize ?? "11pt");
            var fontFamily = documentMeta.GetValueOrDefault("fontFamily", options.FontFamily ?? "charter");

            output.AppendLine($"\\documentclass[{fontSize},{paperSize}]{{article}}");
            output.AppendLine("\\usepackage[utf8]{inputenc}");
            output.AppendLine("\\usepackage{amsmath,amssymb,amsthm}");
            output.AppendLine("\\usepackage{graphicx}");
            output.AppendLine("\\usepackage{listings}");
            output.AppendLine("\\usepackage{hyperref}");
            output.AppendLine("\\usepackage{soul}");
            output.AppendLine("\\usepackage{xcolor}");
            output.AppendLine("\\usepackage{tcolorbox}");
            output.AppendLine("\\usepackage{lettrine}");

            if (fontFamily == "charter")
                output.AppendLine("\\usepackage{charter}");
            else if (fontFamily == "times")
                output.AppendLine("\\usepackage{times}");

            output.AppendLine();
            output.AppendLine("\\theoremstyle{definition}");
            output.AppendLine("\\newtheorem{definition}{Definition}");
            output.AppendLine("\\newtheorem{theorem}{Theorem}");
            output.AppendLine("\\newtheorem{lemma}{Lemma}");
            output.AppendLine("\\newtheorem{proposition}{Proposition}");
            output.AppendLine("\\newtheorem{corollary}{Corollary}");
            output.AppendLine("\\theoremstyle{remark}");
            output.AppendLine("\\newtheorem{remark}{Remark}");
            output.AppendLine("\\newtheorem{example}{Example}");
            output.AppendLine();

            var title = options.Title ?? documentMeta.GetValueOrDefault("title", "");
            if (!string.IsNullOrEmpty(title))
                output.AppendLine($"\\title{{{EscapeLatex(title)}}}");

            var author = options.Author ?? documentMeta.GetValueOrDefault("author", "");
            if (!string.IsNullOrEmpty(author))
                output.AppendLine($"\\author{{{EscapeLatex(author)}}}");

            output.AppendLine("\\date{}");
            output.AppendLine();
            output.AppendLine("\\begin{document}");

            if (!string.IsNullOrEmpty(title))
                output.AppendLine("\\maketitle");

            output.AppendLine();
        }

        // Process content
        var i = SkipDocumentHeader(lines, 0);
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrEmpty(trimmed))
            {
                i++;
                continue;
            }

            // Raw LaTeX passthrough
            if (trimmed == "@latex")
            {
                i++;
                while (i < lines.Length && lines[i].Trim() != "@endlatex")
                {
                    output.AppendLine(lines[i]);
                    i++;
                }
                i++; // Skip @endlatex
                continue;
            }

            // Headings
            if (trimmed.StartsWith("####"))
                output.AppendLine($"\\paragraph{{{EscapeLatex(trimmed[4..].Trim())}}}");
            else if (trimmed.StartsWith("###"))
                output.AppendLine($"\\subsubsection{{{EscapeLatex(trimmed[3..].Trim())}}}");
            else if (trimmed.StartsWith("##"))
                output.AppendLine($"\\subsection{{{EscapeLatex(trimmed[2..].Trim())}}}");
            else if (trimmed.StartsWith("#"))
                output.AppendLine($"\\section{{{EscapeLatex(trimmed[1..].Trim())}}}");

            // Blocks
            else if (trimmed.StartsWith("@abstract"))
            {
                output.AppendLine("\\begin{abstract}");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.AppendLine(ConvertInlineToLatex(lines[i]));
                    i++;
                }
                output.AppendLine("\\end{abstract}");
                continue;
            }
            else if (trimmed.StartsWith("@equation"))
            {
                var match = EquationParamsRegex().Match(trimmed);
                var label = ExtractParam(match.Groups[1].Value, "label");
                var mode = ExtractParam(match.Groups[1].Value, "mode") ?? "display";

                i++;
                var eqLines = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        eqLines.Add(lines[i].Trim());
                    i++;
                }

                if (mode == "align")
                {
                    output.AppendLine("\\begin{align}");
                    if (!string.IsNullOrEmpty(label)) output.AppendLine($"\\label{{{label}}}");
                    output.AppendLine(string.Join("\n", eqLines));
                    output.AppendLine("\\end{align}");
                }
                else
                {
                    output.AppendLine("\\begin{equation}");
                    if (!string.IsNullOrEmpty(label)) output.AppendLine($"\\label{{{label}}}");
                    output.AppendLine(string.Join("\n", eqLines));
                    output.AppendLine("\\end{equation}");
                }
                continue;
            }
            else if (trimmed.StartsWith("@code"))
            {
                var lang = CodeLangRegex().Match(trimmed).Groups[1].Value;
                var langOption = !string.IsNullOrEmpty(lang) ? $"[language={lang}]" : "";
                output.AppendLine($"\\begin{{lstlisting}}{langOption}");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    output.AppendLine(lines[i]);
                    i++;
                }
                output.AppendLine("\\end{lstlisting}");
                continue;
            }
            else if (trimmed == "@table")
            {
                i++;
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].Contains('|'))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }
                output.AppendLine(ConvertTableToLatex(tableLines));
                continue;
            }
            else if (trimmed.StartsWith("@list"))
            {
                var ordered = trimmed.Contains("ordered");
                output.AppendLine(ordered ? "\\begin{enumerate}" : "\\begin{itemize}");
                i++;
                while (i < lines.Length && (lines[i].TrimStart().StartsWith("-") || ListItemRegex().IsMatch(lines[i].TrimStart())))
                {
                    var item = lines[i].TrimStart();
                    item = item.StartsWith("-") ? item[1..].Trim() : ListItemRegex().Replace(item, "").Trim();
                    output.AppendLine($"\\item {ConvertInlineToLatex(item)}");
                    i++;
                }
                output.AppendLine(ordered ? "\\end{enumerate}" : "\\end{itemize}");
                continue;
            }
            else if (trimmed.StartsWith("@alert"))
            {
                var type = AlertTypeRegex().Match(trimmed).Groups[1].Value;
                var title = char.ToUpper(type[0]) + type[1..];
                output.AppendLine($"\\begin{{tcolorbox}}[title={title}]");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.AppendLine(ConvertInlineToLatex(lines[i]));
                    i++;
                }
                output.AppendLine("\\end{tcolorbox}");
                continue;
            }
            else if (trimmed.StartsWith("@theorem") || trimmed.StartsWith("@definition") ||
                     trimmed.StartsWith("@lemma") || trimmed.StartsWith("@proof") ||
                     trimmed.StartsWith("@proposition") || trimmed.StartsWith("@corollary"))
            {
                var typeMatch = TheoremTypeRegex().Match(trimmed);
                var theoremType = typeMatch.Groups[1].Value;
                var paramsMatch = TheoremParamsRegex().Match(trimmed);
                var titleParam = ExtractParam(paramsMatch.Groups[1].Value, "title");
                var labelParam = ExtractParam(paramsMatch.Groups[1].Value, "label");

                if (theoremType == "proof")
                {
                    output.AppendLine("\\begin{proof}");
                }
                else
                {
                    var titleOpt = !string.IsNullOrEmpty(titleParam) ? $"[{EscapeLatex(titleParam)}]" : "";
                    output.AppendLine($"\\begin{{{theoremType}}}{titleOpt}");
                    if (!string.IsNullOrEmpty(labelParam))
                        output.AppendLine($"\\label{{{labelParam}}}");
                }

                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.AppendLine(ConvertInlineToLatex(lines[i]));
                    i++;
                }
                output.AppendLine($"\\end{{{theoremType}}}");
                continue;
            }
            else if (trimmed == "---")
            {
                output.AppendLine("\\hrulefill");
            }
            else if (trimmed == "@pagebreak")
            {
                output.AppendLine("\\newpage");
            }
            else if (trimmed == "@toc")
            {
                output.AppendLine("\\tableofcontents");
                output.AppendLine("\\newpage");
            }
            else if (trimmed.StartsWith("@lorem"))
            {
                var count = LoremCountRegex().Match(trimmed).Groups[2].Value;
                var n = int.TryParse(count, out var c) ? c : 3;
                output.AppendLine(GenerateLoremParagraphs(n));
            }
            else if (trimmed.StartsWith("@date"))
            {
                var format = DateFormatRegex().Match(trimmed).Groups[1].Value.ToLower();
                output.AppendLine(FormatDate(format));
            }
            else if (trimmed.StartsWith("@divider"))
            {
                output.AppendLine("\\begin{center}");
                output.AppendLine("\\rule{0.5\\textwidth}{0.5pt}");
                output.AppendLine("\\end{center}");
            }
            else if (trimmed.StartsWith("@center"))
            {
                output.AppendLine("\\begin{center}");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.AppendLine(ConvertInlineToLatex(lines[i]) + " \\\\");
                    i++;
                }
                output.AppendLine("\\end{center}");
                continue;
            }
            else if (trimmed.StartsWith("@dropcap"))
            {
                i++;
                var dcLines = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        dcLines.Add(lines[i].Trim());
                    i++;
                }
                var text = string.Join(" ", dcLines);
                if (text.Length > 0)
                {
                    output.AppendLine($"\\lettrine{{{text[0]}}}{{}} {ConvertInlineToLatex(text[1..])}");
                    output.AppendLine();
                }
                continue;
            }
            // Regular paragraph
            else if (!trimmed.StartsWith("@"))
            {
                output.AppendLine(ConvertInlineToLatex(line));
                output.AppendLine();
            }

            i++;
        }

        if (options.IncludePreamble)
        {
            output.AppendLine();
            output.AppendLine("\\end{document}");
        }

        return output.ToString();
    }

    public string ConvertToHtml(string lmlContent, LmlConversionOptions? options = null)
    {
        options ??= new LmlConversionOptions();
        var lines = lmlContent.Split('\n');
        var output = new StringBuilder();
        var documentMeta = ParseDocumentMeta(lines);

        var title = options.Title ?? documentMeta.GetValueOrDefault("title", "Untitled");

        if (options.IncludePreamble)
        {
            output.AppendLine("<!DOCTYPE html>");
            output.AppendLine("<html lang=\"en\">");
            output.AppendLine("<head>");
            output.AppendLine("  <meta charset=\"UTF-8\">");
            output.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            output.AppendLine($"  <title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
            output.AppendLine("  <link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/katex@0.16.9/dist/katex.min.css\">");
            output.AppendLine("  <style>");
            output.AppendLine("    body { font-family: Georgia, serif; max-width: 800px; margin: 40px auto; padding: 20px; line-height: 1.6; }");
            output.AppendLine("    h1, h2, h3 { margin-top: 1.5em; }");
            output.AppendLine("    pre { background: #f5f5f5; padding: 1rem; overflow-x: auto; }");
            output.AppendLine("    code { font-family: 'SF Mono', Consolas, monospace; }");
            output.AppendLine("    table { border-collapse: collapse; width: 100%; margin: 1rem 0; }");
            output.AppendLine("    th, td { border: 1px solid #ddd; padding: 0.5rem; text-align: left; }");
            output.AppendLine("    blockquote { border-left: 4px solid #ddd; margin: 1rem 0; padding-left: 1rem; color: #666; }");
            output.AppendLine("    .alert { padding: 1rem; border-radius: 4px; margin: 1rem 0; border-left: 4px solid; }");
            output.AppendLine("    .alert-info { background: #e3f2fd; border-color: #2196f3; }");
            output.AppendLine("    .alert-warning { background: #fff3e0; border-color: #ff9800; }");
            output.AppendLine("    .alert-danger { background: #ffebee; border-color: #f44336; }");
            output.AppendLine("    .theorem { background: #f5f5f5; padding: 1rem; margin: 1rem 0; border-left: 4px solid #9c27b0; }");
            output.AppendLine("    .equation { text-align: center; margin: 1.5rem 0; }");
            output.AppendLine("  </style>");
            output.AppendLine("</head>");
            output.AppendLine("<body>");
        }

        var i = SkipDocumentHeader(lines, 0);
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                i++;
                continue;
            }

            // Headings
            if (trimmed.StartsWith("#"))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                level = Math.Min(level, 6);
                var text = trimmed[level..].Trim();
                output.AppendLine($"<h{level}>{ConvertInlineToHtml(text)}</h{level}>");
            }
            // Code block
            else if (trimmed.StartsWith("@code"))
            {
                var lang = CodeLangRegex().Match(trimmed).Groups[1].Value;
                output.AppendLine($"<pre><code class=\"language-{lang}\">");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    output.AppendLine(System.Net.WebUtility.HtmlEncode(lines[i]));
                    i++;
                }
                output.AppendLine("</code></pre>");
                continue;
            }
            // Table
            else if (trimmed == "@table")
            {
                i++;
                var tableLines = new List<string>();
                while (i < lines.Length && lines[i].Contains('|'))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }
                output.AppendLine(ConvertTableToHtml(tableLines));
                continue;
            }
            // Alert
            else if (trimmed.StartsWith("@alert"))
            {
                var type = AlertTypeRegex().Match(trimmed).Groups[1].Value;
                output.AppendLine($"<div class=\"alert alert-{type}\">");
                output.AppendLine($"<strong>{char.ToUpper(type[0]) + type[1..]}:</strong> ");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.Append(ConvertInlineToHtml(lines[i]) + " ");
                    i++;
                }
                output.AppendLine("</div>");
                continue;
            }
            // Horizontal rule
            else if (trimmed == "---" || trimmed.StartsWith("@divider"))
            {
                output.AppendLine("<hr />");
            }
            // Regular paragraph
            else if (!trimmed.StartsWith("@"))
            {
                output.AppendLine($"<p>{ConvertInlineToHtml(line)}</p>");
            }

            i++;
        }

        if (options.IncludePreamble)
        {
            output.AppendLine("</body>");
            output.AppendLine("</html>");
        }

        return output.ToString();
    }

    public string ConvertToMarkdown(string lmlContent, LmlConversionOptions? options = null)
    {
        var lines = lmlContent.Split('\n');
        var output = new StringBuilder();

        var i = SkipDocumentHeader(lines, 0);
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                output.AppendLine();
                i++;
                continue;
            }

            // Headings pass through
            if (trimmed.StartsWith("#"))
            {
                output.AppendLine(line);
            }
            // Code block
            else if (trimmed.StartsWith("@code"))
            {
                var lang = CodeLangRegex().Match(trimmed).Groups[1].Value;
                output.AppendLine($"```{lang}");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    output.AppendLine(lines[i]);
                    i++;
                }
                output.AppendLine("```");
                continue;
            }
            // Equation
            else if (trimmed.StartsWith("@equation"))
            {
                output.AppendLine();
                output.AppendLine("$$");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.AppendLine(lines[i].Trim());
                    i++;
                }
                output.AppendLine("$$");
                output.AppendLine();
                continue;
            }
            // Table passes through
            else if (trimmed == "@table")
            {
                i++;
                while (i < lines.Length && lines[i].Contains('|'))
                {
                    output.AppendLine(lines[i]);
                    i++;
                }
                continue;
            }
            // List
            else if (trimmed.StartsWith("@list"))
            {
                i++;
                while (i < lines.Length && (lines[i].TrimStart().StartsWith("-") || ListItemRegex().IsMatch(lines[i].TrimStart())))
                {
                    output.AppendLine(lines[i]);
                    i++;
                }
                output.AppendLine();
                continue;
            }
            // Alert becomes blockquote
            else if (trimmed.StartsWith("@alert"))
            {
                var type = AlertTypeRegex().Match(trimmed).Groups[1].Value;
                output.AppendLine($"> **{char.ToUpper(type[0]) + type[1..]}**");
                output.AppendLine(">");
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("@") && !lines[i].TrimStart().StartsWith("#"))
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        output.AppendLine($"> {lines[i].Trim()}");
                    i++;
                }
                output.AppendLine();
                continue;
            }
            // HR
            else if (trimmed == "---" || trimmed.StartsWith("@divider") || trimmed == "@pagebreak")
            {
                output.AppendLine("---");
            }
            // TOC
            else if (trimmed == "@toc")
            {
                output.AppendLine("## Table of Contents");
                output.AppendLine();
            }
            // Regular content with inline conversion
            else if (!trimmed.StartsWith("@"))
            {
                output.AppendLine(ConvertInlineToMarkdown(line));
            }

            i++;
        }

        return output.ToString();
    }

    #region Helpers

    private static Dictionary<string, string> ParseDocumentMeta(string[] lines)
    {
        var meta = new Dictionary<string, string>();
        var inDocument = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == "@document")
            {
                inDocument = true;
                continue;
            }
            if (inDocument)
            {
                if (trimmed.StartsWith("#") || trimmed.StartsWith("@") || string.IsNullOrEmpty(trimmed))
                    break;
                var match = MetaLineRegex().Match(line);
                if (match.Success)
                {
                    meta[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
        }
        return meta;
    }

    private static int SkipDocumentHeader(string[] lines, int start)
    {
        var i = start;
        var inDocument = false;

        while (i < lines.Length)
        {
            var trimmed = lines[i].Trim();
            if (trimmed == "@document")
            {
                inDocument = true;
                i++;
                continue;
            }
            if (inDocument)
            {
                if (trimmed.StartsWith("#") || (trimmed.StartsWith("@") && trimmed != "@document"))
                    break;
                if (string.IsNullOrEmpty(trimmed))
                {
                    i++;
                    continue;
                }
                if (MetaLineRegex().IsMatch(lines[i]))
                {
                    i++;
                    continue;
                }
                break;
            }
            break;
        }
        return i;
    }

    private static string? ExtractParam(string paramsStr, string key)
    {
        var match = Regex.Match(paramsStr, $@"{key}:\s*([^,)]+)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ConvertInlineToLatex(string text)
    {
        var result = text;
        // Remove @raw wrapper, keep content
        result = Regex.Replace(result, @"@raw\(([^)]*)\)", "$1");
        // Inline directives
        result = Regex.Replace(result, @"@fn\(([^)]+)\)", @"\footnotemark[$1]");
        result = Regex.Replace(result, @"@(?:hl|highlight)\(([^)]+)\)", @"\hl{$1}");
        result = Regex.Replace(result, @"@(?:note|comment)\(([^)]+)\)", "% NOTE: $1");
        result = Regex.Replace(result, @"@(?:del|strike)\(([^)]+)\)", @"\st{$1}");
        result = Regex.Replace(result, @"@todo\(([^)]+)\)", @"\marginpar{\small\textbf{TODO:} $1}");
        result = Regex.Replace(result, @"@link\(([^,)]+),\s*([^)]+)\)", @"\href{$2}{$1}");
        result = Regex.Replace(result, @"@link\(([^)]+)\)", @"\url{$1}");
        result = Regex.Replace(result, @"@kbd\(([^)]+)\)", @"\texttt{\small[$1]}");
        result = Regex.Replace(result, @"@abbr\(([^,]+),\s*([^)]+)\)", "$1");
        result = Regex.Replace(result, @"@sub\(([^)]+)\)", @"\textsubscript{$1}");
        result = Regex.Replace(result, @"@sup\(([^)]+)\)", @"\textsuperscript{$1}");
        result = Regex.Replace(result, @"@sc\(([^)]+)\)", @"\textsc{$1}");
        result = Regex.Replace(result, @"@color\(([^,]+),\s*([^)]+)\)", @"\textcolor{$2}{$1}");
        result = Regex.Replace(result, @"@img\(([^,)]+),\s*([^)]+)\)", @"\includegraphics[height=1em]{$1}");
        result = Regex.Replace(result, @"@img\(([^)]+)\)", @"\includegraphics[height=1em]{$1}");
        // Markdown-style formatting
        result = Regex.Replace(result, @"\*\*(.+?)\*\*", @"\textbf{$1}");
        result = Regex.Replace(result, @"\*(.+?)\*", @"\textit{$1}");
        result = Regex.Replace(result, @"`(.+?)`", @"\texttt{$1}");
        return result;
    }

    private static string ConvertInlineToHtml(string text)
    {
        var result = System.Net.WebUtility.HtmlEncode(text);
        // Inline directives (note: already HTML-encoded, so @ becomes @)
        result = Regex.Replace(result, @"@fn\(([^)]+)\)", "<sup>[$1]</sup>");
        result = Regex.Replace(result, @"@(?:hl|highlight)\(([^)]+)\)", "<mark>$1</mark>");
        result = Regex.Replace(result, @"@(?:del|strike)\(([^)]+)\)", "<del>$1</del>");
        result = Regex.Replace(result, @"@todo\(([^)]+)\)", "<span style=\"background:#fff3cd;padding:2px 4px;\">TODO: $1</span>");
        result = Regex.Replace(result, @"@link\(([^,)]+),\s*([^)]+)\)", "<a href=\"$2\">$1</a>");
        result = Regex.Replace(result, @"@link\(([^)]+)\)", "<a href=\"$1\">$1</a>");
        result = Regex.Replace(result, @"@kbd\(([^)]+)\)", "<kbd>$1</kbd>");
        result = Regex.Replace(result, @"@sub\(([^)]+)\)", "<sub>$1</sub>");
        result = Regex.Replace(result, @"@sup\(([^)]+)\)", "<sup>$1</sup>");
        // Markdown-style formatting
        result = Regex.Replace(result, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        result = Regex.Replace(result, @"\*(.+?)\*", "<em>$1</em>");
        result = Regex.Replace(result, @"`(.+?)`", "<code>$1</code>");
        return result;
    }

    private static string ConvertInlineToMarkdown(string text)
    {
        var result = text;
        // Convert LML inline directives to Markdown equivalents
        result = Regex.Replace(result, @"@fn\(([^)]+)\)", "[^$1]");
        result = Regex.Replace(result, @"@(?:hl|highlight)\(([^)]+)\)", "==$1==");
        result = Regex.Replace(result, @"@(?:del|strike)\(([^)]+)\)", "~~$1~~");
        result = Regex.Replace(result, @"@todo\(([^)]+)\)", "**TODO:** $1");
        result = Regex.Replace(result, @"@link\(([^,)]+),\s*([^)]+)\)", "[$1]($2)");
        result = Regex.Replace(result, @"@link\(([^)]+)\)", "<$1>");
        result = Regex.Replace(result, @"@kbd\(([^)]+)\)", "`$1`");
        return result;
    }

    private static string ConvertTableToLatex(List<string> lines)
    {
        if (lines.Count < 2) return "";

        var sb = new StringBuilder();
        var headerCells = lines[0].Split('|').Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        var colSpec = string.Join(" | ", headerCells.Select(_ => "l"));

        sb.AppendLine("\\begin{table}[h]");
        sb.AppendLine("\\centering");
        sb.AppendLine($"\\begin{{tabular}}{{| {colSpec} |}}");
        sb.AppendLine("\\hline");
        sb.AppendLine(string.Join(" & ", headerCells.Select(EscapeLatex)) + " \\\\");
        sb.AppendLine("\\hline");

        for (var i = 2; i < lines.Count; i++)
        {
            var cells = lines[i].Split('|').Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => EscapeLatex(c.Trim()));
            sb.AppendLine(string.Join(" & ", cells) + " \\\\");
        }

        sb.AppendLine("\\hline");
        sb.AppendLine("\\end{tabular}");
        sb.AppendLine("\\end{table}");
        return sb.ToString();
    }

    private static string ConvertTableToHtml(List<string> lines)
    {
        if (lines.Count < 2) return "";

        var sb = new StringBuilder();
        sb.AppendLine("<table>");

        // Header
        var headerCells = lines[0].Split('|').Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim());
        sb.Append("<thead><tr>");
        foreach (var cell in headerCells)
            sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(cell)}</th>");
        sb.AppendLine("</tr></thead>");

        // Body
        sb.AppendLine("<tbody>");
        for (var i = 2; i < lines.Count; i++)
        {
            var cells = lines[i].Split('|').Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim());
            sb.Append("<tr>");
            foreach (var cell in cells)
                sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(cell)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");

        return sb.ToString();
    }

    private static string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("\\", "\\textbackslash{}")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("$", "\\$")
            .Replace("&", "\\&")
            .Replace("#", "\\#")
            .Replace("^", "\\textasciicircum{}")
            .Replace("_", "\\_")
            .Replace("~", "\\textasciitilde{}")
            .Replace("%", "\\%");
    }

    private static string GenerateLoremParagraphs(int count)
    {
        var lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";
        return string.Join("\n\n", Enumerable.Repeat(lorem, count));
    }

    private static string FormatDate(string format)
    {
        var now = DateTime.Now;
        return format switch
        {
            "iso" => now.ToString("yyyy-MM-dd"),
            "short" => now.ToString("MMM d, yyyy"),
            _ => now.ToString("MMMM d, yyyy")
        };
    }

    #endregion

    #region Regex

    [GeneratedRegex(@"@equation\(([^)]*)\)")]
    private static partial Regex EquationParamsRegex();

    [GeneratedRegex(@"@code\((\w+)\)")]
    private static partial Regex CodeLangRegex();

    [GeneratedRegex(@"^\d+\.")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"@alert\((\w+)\)")]
    private static partial Regex AlertTypeRegex();

    [GeneratedRegex(@"@(\w+)")]
    private static partial Regex TheoremTypeRegex();

    [GeneratedRegex(@"@\w+\(([^)]*)\)")]
    private static partial Regex TheoremParamsRegex();

    [GeneratedRegex(@"(paragraphs?|sentences?|words?):\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LoremCountRegex();

    [GeneratedRegex(@"\((\w+)\)")]
    private static partial Regex DateFormatRegex();

    [GeneratedRegex(@"^(\w+):\s*(.+)$")]
    private static partial Regex MetaLineRegex();

    #endregion
}
