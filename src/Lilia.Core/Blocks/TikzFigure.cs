using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Core.Blocks;

/// <summary>
/// A figure whose drawing is TikZ source rather than an image (TikZ figures, step 1, 7 Oct 2026).
///
/// <para>The figure block keeps everything a figure has (caption, label, placement, alignment)
/// and gains two fields:</para>
/// <code>
/// { kind: "image" | "tikz", source: "\begin{tikzpicture} … \end{tikzpicture}" }
/// </code>
/// <para><c>kind</c> absent means <c>"image"</c>, so every existing figure keeps its meaning.
/// <c>source</c> is the picture verbatim: the TikZ environment(s) and anything wrapping them
/// inside the figure (a <c>\resizebox</c>, two pictures side by side), without the figure's own
/// <c>\centering</c>, <c>\caption</c> and <c>\label</c>, which live in the block's usual fields.
/// A figure without a caption is a bare picture: it prints as it was written, without a float
/// or a number (the editor shows it inline). A picture that was not inside a figure is imported
/// that way, and carries <c>float: false</c> as well.</para>
///
/// <para>Placement: <c>placement</c> as for image figures (<see cref="BlockBreakAttributes.FloatSpecifier"/>);
/// absent is <c>[H]</c>, as the exporter prints image figures. The importer writes <c>"auto"</c>
/// (<c>[htbp]</c>) for a figure whose original left LaTeX to place it.</para>
///
/// <para>Every reader and writer of that shape goes through here: both LaTeX emitters, the
/// importer, the preamble extractor, the Typst routing and the SVG renderer.</para>
/// </summary>
public static class TikzFigure
{
    public const string Kind = "tikz";
    public const string ImageKind = "image";

    /// <summary>The environments that make a picture. A pgfplots <c>axis</c> lives inside a tikzpicture.</summary>
    public static readonly IReadOnlyList<string> Environments = new[] { "tikzpicture", "tikzcd" };

    // Bodies TeX does not read as code: a picture inside them is an example, not a figure.
    private static readonly HashSet<string> VerbatimEnvironments = new(StringComparer.Ordinal)
    {
        "verbatim", "verbatim*", "Verbatim", "Verbatim*", "lstlisting", "minted", "comment", "alltt",
    };

    // Preamble statements that configure TikZ and nothing else. They are kept with the document
    // (its custom preamble), emitted again on export, and given to each picture's own compile.
    private static readonly string[] SetupCommands =
    {
        "usetikzlibrary", "usepgfplotslibrary", "usegdlibrary", "pgfplotsset", "tikzset", "tikzcdset",
        "tikzstyle", "pgfdeclarelayer", "pgfsetlayers",
    };

    private static readonly Regex PgfplotsUse = new(
        @"\\begin\{(?:axis|semilogxaxis|semilogyaxis|loglogaxis|polaraxis|groupplot|ternaryaxis|smithchart)\}|\\addplot\b|\\pgfplotsset\b",
        RegexOptions.Compiled);

    private static readonly Regex UsePackage = new(@"\\(?:usepackage|RequirePackage)\s*(?:\[[^\]]*\])?\s*\{([^}]*)\}", RegexOptions.Compiled);

    // ── Reading block content ────────────────────────────────────────────

    public static bool IsTikz(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && content.TryGetProperty("kind", out var k)
        && k.ValueKind == JsonValueKind.String
        && string.Equals(k.GetString(), Kind, StringComparison.Ordinal);

    public static string Source(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && content.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? ""
            : "";

    /// <summary>
    /// A figure floats (environment, number, caption) when it has a caption and is not marked
    /// <c>float: false</c>; without a caption it is a bare picture. The editor reads it the same way.
    /// </summary>
    public static bool IsFloating(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && !(content.TryGetProperty("float", out var f) && f.ValueKind == JsonValueKind.False)
        && content.TryGetProperty("caption", out var c) && c.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(c.GetString());

    /// <summary><c>[H]</c> when the block names no placement (as image figures print), else its placement's specifier.</summary>
    public static string FloatSpecifier(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && content.TryGetProperty("placement", out var p) && p.ValueKind == JsonValueKind.String
        && !string.IsNullOrEmpty(p.GetString())
            ? BlockBreakAttributes.FloatSpecifier(content)
            : "[H]";

    // ── Emitting LaTeX ───────────────────────────────────────────────────

    /// <summary>
    /// The figure as LaTeX. The source goes out byte for byte. A floating figure is wrapped in
    /// <paramref name="environment"/> with its placement, alignment, caption and label; a bare
    /// picture is the source alone. No trailing newline.
    /// </summary>
    /// <param name="captionLatex">The caption already escaped the way the caller escapes captions, or empty.</param>
    /// <param name="labelLatex">The label key as the caller writes labels, or empty.</param>
    public static string ToLatex(JsonElement content, string captionLatex, string labelLatex, string environment = "figure")
    {
        var floatSpecifier = FloatSpecifier(content);
        var source = Source(content);
        if (!IsFloating(content)) return source;

        var position = content.TryGetProperty("position", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var align = position switch
        {
            "left" => @"\raggedright",
            "right" => @"\raggedleft",
            _ => @"\centering",
        };

        var sb = new StringBuilder();
        sb.Append(@"\begin{").Append(environment).Append('}').Append(floatSpecifier).Append('\n');
        sb.Append(align).Append('\n');
        sb.Append(source).Append('\n');
        if (!string.IsNullOrEmpty(captionLatex))
            sb.Append(@"\caption{").Append(captionLatex).Append("}\n");
        if (!string.IsNullOrEmpty(labelLatex))
            sb.Append(@"\label{").Append(labelLatex).Append("}\n");
        sb.Append(@"\end{").Append(environment).Append('}');
        return sb.ToString();
    }

    /// <summary>The packages a picture needs: tikz always, pgfplots for an axis, tikz-cd for a tikzcd.</summary>
    public static IReadOnlyList<string> RequiredPackages(string? source)
    {
        var list = new List<string> { "tikz" };
        if (string.IsNullOrEmpty(source)) return list;
        if (PgfplotsUse.IsMatch(source)) list.Add("pgfplots");
        if (source.Contains(@"\begin{tikzcd}", StringComparison.Ordinal)) list.Add("tikz-cd");
        return list;
    }

    /// <summary>
    /// <c>\usepackage</c> lines for the TikZ packages the document's figures need and its own
    /// packages do not already load. Empty when the document has no TikZ figure, so TikZ is
    /// never added to every preamble. Written after the shared packages (xcolor with its options
    /// is loaded by then) and before the custom preamble, whose <c>\usetikzlibrary</c> lines need
    /// TikZ loaded.
    /// </summary>
    public static string PackageLines(IEnumerable<(string Type, JsonElement Content)> blocks, string? latexPackagesJson, string? customPreamble)
    {
        var needed = new List<string>();
        foreach (var (type, content) in blocks)
        {
            if (type is not ("figure" or "image") || !IsTikz(content)) continue;
            foreach (var pkg in RequiredPackages(Source(content)))
                if (!needed.Contains(pkg)) needed.Add(pkg);
        }
        if (needed.Count == 0) return "";

        var loaded = DeclaredPackages(latexPackagesJson, customPreamble);
        var missing = needed.Where(p => !loaded.Contains(p)).ToList();
        if (missing.Count == 0) return "";

        var sb = new StringBuilder();
        sb.Append("% TikZ figures\n");
        foreach (var pkg in missing) sb.Append(@"\usepackage{").Append(pkg).Append("}\n");
        // A document written in Lilia gets the current pgfplots behaviour rather than the
        // backwards-compatibility mode (and its warning). An imported one keeps its own setting.
        if (missing.Contains("pgfplots") && !(customPreamble ?? "").Contains("compat", StringComparison.Ordinal))
            sb.Append(@"\pgfplotsset{compat=1.18}").Append('\n');
        return sb.ToString();
    }

    /// <summary>Package names the document loads itself: its imported packages and its custom preamble's.</summary>
    public static HashSet<string> DeclaredPackages(string? latexPackagesJson, string? customPreamble)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(latexPackagesJson))
        {
            try
            {
                using var json = JsonDocument.Parse(latexPackagesJson);
                if (json.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var pkg in json.RootElement.EnumerateArray())
                        if (pkg.ValueKind == JsonValueKind.Object && pkg.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                            names.Add(name.Trim());
            }
            catch (JsonException) { /* unreadable: nothing declared */ }
        }
        if (!string.IsNullOrEmpty(customPreamble))
            foreach (Match m in UsePackage.Matches(customPreamble))
                foreach (var n in m.Groups[1].Value.Split(','))
                    if (n.Trim().Length > 0) names.Add(n.Trim());
        // pgfplots and tikz-cd load tikz.
        if (names.Contains("pgfplots") || names.Contains("tikz-cd")) names.Add("tikz");
        return names;
    }

    // ── Reading LaTeX source ─────────────────────────────────────────────

    /// <summary>
    /// Where each picture is in <paramref name="text"/>: a top-level <c>tikzpicture</c> or
    /// <c>tikzcd</c> environment, nested pictures included in their parent's span. Pictures in a
    /// comment or inside a verbatim-like environment (a listing showing TikZ code) are not pictures.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> FindEnvironments(string? text)
    {
        var spans = new List<(int, int)>();
        if (string.IsNullOrEmpty(text) || !text.Contains(@"\begin{", StringComparison.Ordinal)) return spans;
        Scan(text, onBegin: (name, start, headerEnd) =>
        {
            if (!Environments.Contains(name)) return null;
            var end = FindEnd(text, name, headerEnd);
            if (end < 0) return null;
            spans.Add((start, end - start));
            return end;
        });
        return spans;
    }

    /// <summary>
    /// The TikZ setup statements in <paramref name="text"/> (<c>\usetikzlibrary{…}</c>,
    /// <c>\tikzset{…}</c>, <c>\pgfplotsset{…}</c>, <c>\tikzstyle{…}=[…]</c> …), outside comments,
    /// verbatim and the pictures themselves (a <c>\tikzset</c> inside a picture is local to it).
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> SetupStatements(string? text)
    {
        var spans = new List<(int, int)>();
        if (string.IsNullOrEmpty(text)) return spans;
        Scan(text,
            onBegin: (name, start, headerEnd) =>
            {
                if (!Environments.Contains(name)) return null;
                var end = FindEnd(text, name, headerEnd);
                return end < 0 ? null : end;
            },
            onCommand: (name, start, nameEnd) =>
            {
                if (!SetupCommands.Contains(name)) return null;
                var end = ReadArguments(text, name, nameEnd);
                if (end < 0) return null;
                spans.Add((start, end - start));
                return end;
            });
        return spans;
    }

    /// <summary>True when a custom preamble holds nothing but TikZ setup (or nothing at all).</summary>
    public static bool IsSetupOnly(string? preamble)
    {
        if (string.IsNullOrWhiteSpace(preamble)) return true;
        var spans = SetupStatements(preamble);
        var sb = new StringBuilder();
        var last = 0;
        foreach (var (s, l) in spans)
        {
            sb.Append(preamble, last, s - last);
            last = s + l;
        }
        sb.Append(preamble, last, preamble.Length - last);
        var rest = Regex.Replace(sb.ToString(), @"(?<!\\)%[^\n]*", "");
        return string.IsNullOrWhiteSpace(rest);
    }

    /// <summary>The setup statements of <paramref name="text"/>, one per line, in source order.</summary>
    public static string SetupLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return string.Join("\n", SetupStatements(text).Select(s => text.Substring(s.Start, s.Length).Trim()));
    }

    /// <summary>
    /// Removes <paramref name="indent"/> from the start of every line after the first, when they
    /// all carry it: a picture indented inside its figure is stored at the left margin, with its
    /// own relative indentation untouched. Anything else is returned as it is.
    /// </summary>
    public static string Dedent(string text, string indent)
    {
        if (string.IsNullOrEmpty(indent) || !text.Contains('\n')) return text;
        var lines = text.Split('\n');
        for (var i = 1; i < lines.Length; i++)
            if (lines[i].Trim().Length > 0 && !lines[i].StartsWith(indent, StringComparison.Ordinal))
                return text;
        for (var i = 1; i < lines.Length; i++)
            lines[i] = lines[i].StartsWith(indent, StringComparison.Ordinal) ? lines[i][indent.Length..] : lines[i].TrimStart(' ', '\t');
        return string.Join("\n", lines);
    }

    // ── Scanner ──────────────────────────────────────────────────────────

    /// <summary>
    /// Walks LaTeX outside comments. <paramref name="onBegin"/> sees each <c>\begin{name}</c>
    /// (verbatim bodies are skipped first) and <paramref name="onCommand"/> each other control
    /// word; either returns where to resume, or null to carry on after it.
    /// </summary>
    private static void Scan(string text,
        Func<string, int, int, int?> onBegin,
        Func<string, int, int, int?>? onCommand = null)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '%')
            {
                var nl = text.IndexOf('\n', i);
                i = nl < 0 ? text.Length : nl + 1;
                continue;
            }
            if (c != '\\') { i++; continue; }

            if (string.CompareOrdinal(text, i, @"\begin{", 0, 7) == 0)
            {
                var close = text.IndexOf('}', i + 7);
                if (close < 0) return;
                var name = text.Substring(i + 7, close - i - 7);
                if (VerbatimEnvironments.Contains(name))
                {
                    var endTag = @"\end{" + name + "}";
                    var e = text.IndexOf(endTag, close + 1, StringComparison.Ordinal);
                    i = e < 0 ? text.Length : e + endTag.Length;
                    continue;
                }
                var resume = onBegin(name, i, close + 1);
                i = resume ?? close + 1;
                continue;
            }

            // A control word: \name. A control symbol (\%, \\, \{) is two characters.
            var j = i + 1;
            while (j < text.Length && char.IsAsciiLetter(text[j])) j++;
            if (j == i + 1) { i = Math.Min(text.Length, i + 2); continue; }
            if (onCommand is not null)
            {
                var resume = onCommand(text.Substring(i + 1, j - i - 1), i, j);
                if (resume is { } r) { i = r; continue; }
            }
            i = j;
        }
    }

    /// <summary>The index just past the <c>\end{name}</c> that closes the environment, or -1.</summary>
    private static int FindEnd(string text, string name, int from)
    {
        var begin = @"\begin{" + name + "}";
        var end = @"\end{" + name + "}";
        var depth = 1;
        var i = from;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '%')
            {
                var nl = text.IndexOf('\n', i);
                i = nl < 0 ? text.Length : nl + 1;
                continue;
            }
            if (c != '\\') { i++; continue; }
            if (string.CompareOrdinal(text, i, begin, 0, begin.Length) == 0) { depth++; i += begin.Length; continue; }
            if (string.CompareOrdinal(text, i, end, 0, end.Length) == 0)
            {
                depth--;
                i += end.Length;
                if (depth == 0) return i;
                continue;
            }
            i += 2;
        }
        return -1;
    }

    /// <summary>
    /// The index past a setup command's arguments: one brace group (two for
    /// <c>\pgfdeclarelayer</c>-like forms are not needed), or <c>{name}=[…]</c> / <c>+=[…]</c> for
    /// <c>\tikzstyle</c>. -1 when the arguments are not there.
    /// </summary>
    private static int ReadArguments(string text, string name, int i)
    {
        i = SkipSpaces(text, i);
        var end = ReadGroup(text, i, '{', '}');
        if (end < 0) return -1;
        if (name != "tikzstyle") return end;
        var k = SkipSpaces(text, end);
        if (k < text.Length && text[k] == '+') k++;
        if (k >= text.Length || text[k] != '=') return end;
        k = SkipSpaces(text, k + 1);
        var bracket = ReadGroup(text, k, '[', ']');
        return bracket < 0 ? end : bracket;
    }

    private static int SkipSpaces(string text, int i)
    {
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t' || text[i] == '\n' || text[i] == '\r')) i++;
        return i;
    }

    /// <summary>Past a balanced group opening at <paramref name="i"/>; braces inside a bracket group are balanced too.</summary>
    private static int ReadGroup(string text, int i, char open, char close)
    {
        if (i >= text.Length || text[i] != open) return -1;
        var depth = 0;
        var braces = 0;
        for (var k = i; k < text.Length; k++)
        {
            var c = text[k];
            if (c == '\\') { k++; continue; }
            if (c == '%') { var nl = text.IndexOf('\n', k); if (nl < 0) return -1; k = nl; continue; }
            if (open != '{')
            {
                if (c == '{') braces++;
                else if (c == '}') braces--;
                if (braces > 0) continue;
            }
            if (c == open) depth++;
            else if (c == close && --depth == 0) return k + 1;
        }
        return -1;
    }
}
