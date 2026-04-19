using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface ITypstRenderService
{
    string RenderBlockToTypst(Block block);
    Task<string> RenderToTypstAsync(Guid documentId);
    Task<byte[]> CompileTypstToPdfAsync(string typstSource, int timeoutSeconds = 10);
    bool IsAvailable { get; }
}

public class TypstRenderService : ITypstRenderService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<TypstRenderService> _logger;
    private readonly Lazy<bool> _typstAvailable;

    public bool IsAvailable => _typstAvailable.Value;

    public TypstRenderService(LiliaDbContext context, ILogger<TypstRenderService> logger)
    {
        _context = context;
        _logger = logger;
        _typstAvailable = new Lazy<bool>(() => CheckTypstAvailable());
    }

    private static bool CheckTypstAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "typst",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> RenderToTypstAsync(Guid documentId)
    {
        var doc = await _context.Documents.FindAsync(documentId)
            ?? throw new InvalidOperationException($"Document {documentId} not found");

        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var sb = new StringBuilder();

        // Document metadata
        sb.AppendLine("#set document(");
        sb.AppendLine($"  title: \"{EscapeTypst(doc.Title ?? "Untitled")}\",");
        sb.AppendLine(")");
        sb.AppendLine();

        // Page setup
        sb.AppendLine("#set page(paper: \"a4\", margin: 2.5cm)");
        sb.AppendLine("#set text(size: 11pt)");
        sb.AppendLine("#set par(justify: true)");
        sb.AppendLine();

        // Render each block
        foreach (var block in blocks)
        {
            var typst = RenderBlockToTypst(block);
            if (!string.IsNullOrWhiteSpace(typst))
            {
                sb.AppendLine(typst);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public string RenderBlockToTypst(Block block)
    {
        try
        {
            var content = block.Content.RootElement;

            return block.Type.ToLowerInvariant() switch
            {
                "heading" or "header" => RenderHeadingToTypst(content),
                "paragraph" => RenderParagraphToTypst(content),
                "equation" => RenderEquationToTypst(content),
                "figure" or "image" => RenderFigureToTypst(content),
                "table" => RenderTableToTypst(content),
                "code" => RenderCodeToTypst(content),
                "list" => RenderListToTypst(content),
                "blockquote" or "quote" => RenderBlockquoteToTypst(content),
                "theorem" => RenderTheoremToTypst(content),
                "abstract" => RenderAbstractToTypst(content),
                "tableofcontents" => "#outline()",
                "columnbreak" => "#colbreak()",
                "pagebreak" or "divider" => "#pagebreak()",
                "bibliography" => "// Bibliography handled separately",
                "embed" => RenderEmbedToTypst(content),
                "algorithm" => RenderAlgorithmToTypst(content),
                "callout" => RenderCalloutToTypst(content),
                "footnote" => RenderFootnoteToTypst(content),
                "slide" => RenderSlideToTypst(content),
                _ => $"// Unknown block type: {block.Type}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render block {BlockId} to Typst", block.Id);
            return $"// Error rendering block: {block.Id}";
        }
    }

    public async Task<byte[]> CompileTypstToPdfAsync(string typstSource, int timeoutSeconds = 10)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("Typst binary is not available on this system");

        var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-typst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            var inputPath = Path.Combine(tmpDir, "input.typ");
            var outputPath = Path.Combine(tmpDir, "output.pdf");
            await File.WriteAllTextAsync(inputPath, typstSource);

            var (exitCode, _, stderr) = await RunProcessAsync(
                "typst",
                $"compile {inputPath} {outputPath}",
                tmpDir,
                timeoutSeconds);

            if (exitCode != 0)
                throw new InvalidOperationException($"Typst compilation failed: {(stderr.Length > 500 ? stderr[..500] : stderr)}");

            if (!File.Exists(outputPath))
                throw new InvalidOperationException("Typst PDF was not generated");

            return await File.ReadAllBytesAsync(outputPath);
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    // --- Block rendering methods ---

    private static string RenderHeadingToTypst(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var level = content.TryGetProperty("level", out var l) ? l.GetInt32() : 1;

        var prefix = new string('=', Math.Clamp(level, 1, 6));
        return $"{prefix} {ProcessTypstText(text)}";
    }

    private static string RenderParagraphToTypst(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return ProcessTypstText(text);
    }

    private static string RenderEquationToTypst(JsonElement content)
    {
        var latex = content.TryGetProperty("latex", out var l) ? l.GetString() ?? "" : "";
        var displayMode = content.TryGetProperty("displayMode", out var d) && d.GetBoolean();

        // Strip MathLive placeholder artifacts
        latex = latex.Replace("\\placeholder{}", "").Replace("\\placeholder", "");

        if (string.IsNullOrWhiteSpace(latex))
            return "// Empty equation";

        if (displayMode)
            return $"$ {latex} $";
        return $"${latex}$";
    }

    private static string RenderFigureToTypst(JsonElement content)
    {
        var src = content.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var c) ? c.GetString() ?? "" : "";

        var sb = new StringBuilder();
        sb.AppendLine("#figure(");
        sb.AppendLine($"  image(\"{EscapeTypst(src)}\"),");
        if (!string.IsNullOrEmpty(caption))
            sb.AppendLine($"  caption: [{ProcessTypstText(caption)}],");
        sb.Append(')');
        return sb.ToString();
    }

    private static string RenderTableToTypst(JsonElement content)
    {
        var sb = new StringBuilder();

        var hasHeaders = content.TryGetProperty("headers", out var headers)
            && headers.ValueKind == JsonValueKind.Array
            && headers.GetArrayLength() > 0;

        if (content.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            var rowList = rows.EnumerateArray().ToList();
            var colCount = hasHeaders
                ? headers.GetArrayLength()
                : rowList.Count > 0 && rowList[0].ValueKind == JsonValueKind.Array
                    ? rowList[0].GetArrayLength()
                    : 1;

            sb.AppendLine($"#table(");
            sb.AppendLine($"  columns: {colCount},");

            // Header row
            if (hasHeaders)
            {
                foreach (var h in headers.EnumerateArray())
                {
                    var cellText = GetCellText(h);
                    sb.AppendLine($"  [*{EscapeTypst(cellText)}*],");
                }
            }

            // Data rows
            foreach (var row in rowList)
            {
                if (row.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cell in row.EnumerateArray())
                    {
                        var cellText = GetCellText(cell);
                        sb.AppendLine($"  [{EscapeTypst(cellText)}],");
                    }
                }
            }

            sb.Append(')');
        }
        else
        {
            sb.Append("#table(columns: 1, [Empty table])");
        }

        return sb.ToString();
    }

    private static string RenderCodeToTypst(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var language = content.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";

        var langAttr = !string.IsNullOrEmpty(language) ? language : "";
        return $"```{langAttr}\n{code}\n```";
    }

    private static string RenderListToTypst(JsonElement content)
    {
        var isOrdered = false;
        if (content.TryGetProperty("listType", out var lt))
            isOrdered = lt.GetString() == "ordered";
        else if (content.TryGetProperty("ordered", out var ord))
            isOrdered = ord.ValueKind == JsonValueKind.True;

        var marker = isOrdered ? "+" : "-";
        var sb = new StringBuilder();

        if (content.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                RenderListItemToTypst(item, marker, sb, 0);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void RenderListItemToTypst(JsonElement item, string marker, StringBuilder sb, int depth)
    {
        string itemText;

        if (item.ValueKind == JsonValueKind.String)
        {
            itemText = item.GetString() ?? "";
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                itemText = textProp.GetString() ?? "";
            else
                itemText = "";
        }
        else
        {
            itemText = "";
        }

        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}{marker} {ProcessTypstText(itemText)}");

        // Nested children
        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                RenderListItemToTypst(child, marker, sb, depth + 1);
            }
        }
    }

    private static string RenderBlockquoteToTypst(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"#quote(block: true)[{ProcessTypstText(text)}]";
    }

    private static string RenderTheoremToTypst(JsonElement content)
    {
        var theoremType = content.TryGetProperty("theoremType", out var tt) ? tt.GetString() ?? "theorem" : "theorem";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";

        var displayType = char.ToUpper(theoremType[0]) + theoremType[1..];
        var headerText = !string.IsNullOrEmpty(title)
            ? $"*{EscapeTypst(displayType)}* ({EscapeTypst(title)})"
            : $"*{EscapeTypst(displayType)}*";

        return $"#block(\n  width: 100%,\n  inset: 8pt,\n  stroke: 0.5pt + luma(180),\n  [\n    {headerText}. {ProcessTypstText(text)}\n  ]\n)";
    }

    private static string RenderAbstractToTypst(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"#align(center)[#emph[Abstract]]\n\n{ProcessTypstText(text)}";
    }

    private static string RenderEmbedToTypst(JsonElement content)
    {
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        return $"// Embed block\n{code}";
    }

    private static string RenderAlgorithmToTypst(JsonElement content)
    {
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var code = content.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        var caption = content.TryGetProperty("caption", out var cap) ? cap.GetString() ?? "" : "";

        var displayCaption = !string.IsNullOrEmpty(caption) ? caption : title;
        var sb = new StringBuilder();
        sb.AppendLine("#figure(");
        sb.AppendLine("  kind: \"algorithm\",");
        if (!string.IsNullOrEmpty(displayCaption))
            sb.AppendLine($"  caption: [{EscapeTypst(displayCaption)}],");
        sb.AppendLine("  block(");
        sb.AppendLine("    width: 100%,");
        sb.AppendLine("    inset: 8pt,");
        sb.AppendLine("    stroke: 0.5pt + luma(180),");
        sb.AppendLine($"    [```\n{code}\n```]");
        sb.AppendLine("  )");
        sb.Append(')');
        return sb.ToString();
    }

    private static string RenderCalloutToTypst(JsonElement content)
    {
        var variant = content.TryGetProperty("variant", out var v) ? v.GetString() ?? "note" : "note";
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = content.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";

        var displayTitle = !string.IsNullOrEmpty(title) ? title : char.ToUpper(variant[0]) + variant[1..];

        return $"#block(\n  width: 100%,\n  inset: 8pt,\n  fill: luma(240),\n  stroke: 0.5pt + luma(180),\n  [\n    *{EscapeTypst(displayTitle)}*\n\n    {ProcessTypstText(text)}\n  ]\n)";
    }

    private static string RenderFootnoteToTypst(JsonElement content)
    {
        var text = content.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return $"#footnote[{ProcessTypstText(text)}]";
    }

    private static string RenderSlideToTypst(JsonElement content)
    {
        // Typst doesn't have a built-in Beamer analogue but Polylux /
        // Touying are the common community packages. For v1 we emit a
        // pagebreak + styled heading + content, which renders as a
        // presentable slide in the Typst preview. Full Polylux output
        // is a follow-on (requires importing the package at doc-level).
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var body = content.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#pagebreak(weak: true)");
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append("= ").AppendLine(EscapeTypst(title));
        if (!string.IsNullOrWhiteSpace(body))
            sb.AppendLine(ProcessTypstText(body));
        return sb.ToString();
    }

    // --- Helpers ---

    private static string GetCellText(JsonElement cell)
    {
        if (cell.ValueKind == JsonValueKind.String)
            return cell.GetString() ?? "";
        if (cell.ValueKind == JsonValueKind.Object && cell.TryGetProperty("text", out var t))
            return t.GetString() ?? "";
        return "";
    }

    /// <summary>
    /// Process inline formatting in text for Typst output.
    /// Converts markdown-style formatting to Typst markup.
    /// </summary>
    private static string ProcessTypstText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Strip LaTeX artefacts BEFORE markdown processing — raw
        // `\section{X}` / `\rule[...]{...}{...}` / `%% comment` lines
        // leak into Typst as unbalanced delimiters and kill compilation.
        var result = StripLatexArtefacts(text);

        // Protect math regions first
        var mathRegions = new List<string>();
        result = Regex.Replace(result, @"\$\$(.+?)\$\$", m =>
        {
            mathRegions.Add($"$ {m.Groups[1].Value} $");
            return $"\x00MATH{mathRegions.Count - 1}\x00";
        });
        result = Regex.Replace(result, @"(?<!\$)\$(?!\$)(.+?)(?<!\$)\$(?!\$)", m =>
        {
            mathRegions.Add($"${m.Groups[1].Value}$");
            return $"\x00MATH{mathRegions.Count - 1}\x00";
        });

        // Convert bold: **text** or __text__ to *text*
        result = Regex.Replace(result, @"\*\*(.+?)\*\*", "*$1*");
        result = Regex.Replace(result, @"__(.+?)__", "*$1*");

        // Convert italic: *text* or _text_ to _text_ (Typst uses _)
        // Be careful not to double-convert bold markers
        result = Regex.Replace(result, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "_$1_");

        // Convert inline code: `text` to #raw("text")
        result = Regex.Replace(result, @"`([^`]+)`", "```$1```");

        // Restore math regions
        for (var i = 0; i < mathRegions.Count; i++)
        {
            result = result.Replace($"\x00MATH{i}\x00", mathRegions[i]);
        }

        return result;
    }

    /// <summary>
    /// Remove / translate LaTeX residue that would otherwise leak into Typst
    /// source and break compilation. Documents imported from DOCX or LaTeX
    /// often carry raw commands the parser didn't translate (sectioning
    /// commands, \rule, %% comments, etc.). We handle the common cases and
    /// drop the rest so Typst at least gets valid syntax.
    /// </summary>
    internal static string StripLatexArtefacts(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // LaTeX comments at start of line — `%` (single) and `%%` — Typst uses `//`.
        text = Regex.Replace(text, @"(?m)^\s*%+[^\n]*", "");

        // Sectioning commands → Typst heading markers.
        text = Regex.Replace(text, @"\\section\*?\s*\{([^}]*)\}",       "= $1");
        text = Regex.Replace(text, @"\\subsection\*?\s*\{([^}]*)\}",    "== $1");
        text = Regex.Replace(text, @"\\subsubsection\*?\s*\{([^}]*)\}", "=== $1");
        text = Regex.Replace(text, @"\\paragraph\s*\{([^}]*)\}",        "==== $1");

        // Known inline text wrappers — keep the argument, drop the command.
        text = Regex.Replace(text, @"\\(?:textbf|textit|emph|texttt|textsc|textrm|textsf|underline)\{([^{}]*)\}", "$1");

        // Layout / spacing / rule macros we don't emulate — nuke the whole call.
        text = Regex.Replace(text, @"\\(?:rule|vspace|hspace|vskip|hskip|noindent|par|newline|newpage|clearpage|cleardoublepage|linebreak|pagebreak)\b(?:\[[^\]]*\])?(?:\{[^}]*\})*", "");

        // Generic `\cmd{arg}` fallback — strip command name, keep arg. This
        // runs last so the specific rules above can translate what they
        // understand first.
        var prev = "";
        var guard = 0;
        while (prev != text && guard++ < 8)
        {
            prev = text;
            text = Regex.Replace(text, @"\\[a-zA-Z]+\*?\s*(?:\[[^\]]*\])?\s*\{([^{}]*)\}", "$1");
        }

        // Bare LaTeX tokens with no argument (e.g. `\LaTeX`, `\TeX`, `\centering`).
        text = Regex.Replace(text, @"\\LaTeX\{?\}?", "LaTeX");
        text = Regex.Replace(text, @"\\TeX\{?\}?", "TeX");
        // Anything remaining like `\centering`, `\small`, etc. — drop silently.
        text = Regex.Replace(text, @"\\[a-zA-Z]+\*?\b", "");

        // Literal backslashes from `\\ ` line breaks or leftover escapes —
        // Typst will choke on them. Keep `\n` literal newlines.
        text = text.Replace("\\\\", "\n");
        text = text.Replace("\\", "");

        // Collapse blank runs introduced by deletions.
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text;
    }

    /// <summary>
    /// Escape special Typst characters in plain text.
    /// </summary>
    internal static string EscapeTypst(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Typst special characters that need escaping with backslash
        return text
            .Replace("\\", "\\\\")
            .Replace("#", "\\#")
            .Replace("@", "\\@")
            .Replace("<", "\\<")
            .Replace(">", "\\>")
            .Replace("\"", "\\\"");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string command, string arguments, string workingDir, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException($"Typst process timed out after {timeoutSeconds}s");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}
