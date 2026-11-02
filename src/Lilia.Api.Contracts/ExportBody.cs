using Lilia.Core.Entities;

namespace Lilia.Api.Services;

/// <summary>
/// Which blocks a document's body prints (moved from LaTeXExportService, 2 Nov 2026, so the Library
/// slice can use it from its own project; LaTeXExportService forwards to it).
/// </summary>
public static class ExportBody
{
    /// <summary>
    /// The blocks the body prints, in order: no title, abstract or bibliography block (the
    /// preamble and \maketitle carry those), and no leading heading that repeats the title.
    /// The theme line numbers Index pins by these, and the editor's section chips
    /// (Lilia.Engines.Themes.ThemeSections) count the same headings.
    /// </summary>
    public static List<Block> BodyBlocks(string? title, IEnumerable<Block> orderedBlocks, bool abstractInBody = false)
    {
        var all = orderedBlocks.ToList();
        var lead = abstractInBody ? null : LeadAbstract(all);
        return StripDuplicateTitleHeading(title,
            all.Where(b => !ReferenceEquals(b, lead) && b.Type != "bibliography" && b.Type != "title").ToList());
    }

    /// <summary>
    /// The abstract printed after \maketitle: the first one. Any other stays in the body where the
    /// author put it — a second abstract (a translation, say) was dropped from every export
    /// while the check printed it (MCP-39).
    /// </summary>
    public static Block? LeadAbstract(IEnumerable<Block> blocks) => blocks.FirstOrDefault(b => b.Type == "abstract");

    private static List<Block> StripDuplicateTitleHeading(string? title, List<Block> blocks)
    {
        if (string.IsNullOrWhiteSpace(title) || blocks.Count == 0) return blocks;
        var canon = NormalizeTitleForCompare(title);
        // Look for the FIRST heading; non-heading content blocks before
        // it short-circuit (their presence means the title heading
        // isn't the leading element).
        Block? leadingHeading = null;
        foreach (var b in blocks)
        {
            if (string.Equals(b.Type, "heading", StringComparison.OrdinalIgnoreCase))
            {
                leadingHeading = b;
                break;
            }
            if (IsTitleStripContentBlock(b.Type)) return blocks;
        }
        if (leadingHeading == null) return blocks;
        try
        {
            var text = leadingHeading.Content.RootElement.TryGetProperty("text", out var t)
                ? t.GetString() ?? "" : "";
            if (NormalizeTitleForCompare(text) == canon)
                return blocks.Where(b => b.Id != leadingHeading.Id).ToList();
        }
        catch { /* malformed — keep block */ }
        return blocks;
    }

    private static string NormalizeTitleForCompare(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool IsTitleStripContentBlock(string type) => type?.ToLowerInvariant() switch
    {
        "paragraph" or "equation" or "figure" or "table" or "code"
            or "list" or "blockquote" or "theorem" or "abstract"
            or "bibliography" => true,
        _ => false,
    };
}
