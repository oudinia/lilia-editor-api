using System.Text.RegularExpressions;

namespace Lilia.Import.Services;

/// <summary>
/// What pasted text is, for an import with format AUTO: LATEX, LML (Lilia's <c>@heading</c> text
/// or its JSON) or MARKDOWN, which also covers plain text. 10 Nov 2026, paste → LaTeX phase 1
/// (lilia-launch-bot #60): the person no longer has to choose.
/// </summary>
public static class PasteFormat
{
    private static readonly Regex LatexDocument = new(@"\\documentclass\b|\\begin\{document\}", RegexOptions.Compiled);
    private static readonly Regex LatexStructure = new(@"\\(?:part|chapter|(?:sub)*section|paragraph|begin|end|item|label|caption|maketitle|usepackage|includegraphics)\b", RegexOptions.Compiled);
    private static readonly Regex LmlBlock = new(@"^\s*@[a-zA-Z]+\b", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownLine = new(@"^\s{0,3}(?:#{1,6}\s+\S|[-*+]\s+\S|\d{1,9}[.)]\s+\S|>\s?|```|~~~|\|.*\|\s*$)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MarkdownInline = new(@"\*\*\S.*?\*\*|(?<!!)\[[^\]]+\]\([^)\s]+\)|`[^`\n]+`", RegexOptions.Compiled);

    public static string Detect(string? text)
    {
        var t = (text ?? "").TrimStart();
        if (t.Length == 0) return "MARKDOWN";
        if (t.StartsWith('{') || t.StartsWith('[') && t.TrimEnd().EndsWith(']') && t.Contains("\"type\"")) return "LML";
        if (LatexDocument.IsMatch(t)) return "LATEX";
        if (t.StartsWith('@') || LmlBlock.Matches(t).Count >= 2) return "LML";
        var latex = LatexStructure.Matches(t).Count;
        var markdown = MarkdownLine.Matches(t).Count + MarkdownInline.Matches(t).Count;
        return latex > 0 && latex >= markdown ? "LATEX" : "MARKDOWN";
    }
}
