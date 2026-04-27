using System.Reflection;
using FluentAssertions;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tier 2 of the export-side defence-in-depth strategy: every canonical
/// user-content block type must have a case branch in both
/// <c>LaTeXExportService.RenderBlock</c> and
/// <c>DocxExportService.ConvertBlockSync</c>.
///
/// Mirror of <c>EnvironmentHandlerCoverageTests</c> on the import side.
/// Pre-fix, adding a new block type to <c>BlockTypes</c> didn't fail
/// any test if the exporters silently fell back to plain text. This
/// gate catches "added block type, forgot exporter case" the day the
/// new constant lands.
///
/// Verticals (slide, inv-*, frontMatter/backMatter, etc.) are skipped
/// per <see cref="VerticalTypes"/> — those exporters live in their own
/// pipelines and aren't expected on the general LaTeX/DOCX paths.
/// </summary>
public class ExportHandlerCoverageTests
{
    /// <summary>
    /// Block types we require the general LaTeX + DOCX exporters to
    /// handle. Anything in <see cref="BlockTypes"/> NOT in this set is
    /// allowed to fall through (vertical-specific or alias).
    /// </summary>
    private static readonly HashSet<string> RequiredCoverage = new(StringComparer.OrdinalIgnoreCase)
    {
        BlockTypes.Paragraph,
        BlockTypes.Heading,
        BlockTypes.Equation,
        BlockTypes.Figure,
        BlockTypes.Table,
        BlockTypes.Code,
        BlockTypes.List,
        BlockTypes.Blockquote,
        BlockTypes.Theorem,
        BlockTypes.Abstract,
        BlockTypes.Bibliography,
        BlockTypes.TableOfContents,
        BlockTypes.PageBreak,
    };

    private static readonly string LatexExportPath = ResolveSourcePath(
        "src/Lilia.Api/Services/LaTeXExportService.cs");
    private static readonly string DocxExportPath = ResolveSourcePath(
        "src/Lilia.Import/Services/DocxExportService.cs");

    public static IEnumerable<object[]> RequiredBlockTypes() =>
        RequiredCoverage.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(RequiredBlockTypes))]
    public void Latex_exporter_has_case_for_block_type(string blockType)
    {
        var src = File.ReadAllText(LatexExportPath);
        // Match `"<type>" => …` in the RenderBlock switch.
        var pattern = $"\"{blockType}\"\\s*=>";
        src.Should().MatchRegex(pattern,
            $"LaTeXExportService.RenderBlock should have a case for '{blockType}' — " +
            "without it the block silently falls to the empty default and " +
            "exports as no content. See ExportHandlerCoverageTests for the " +
            "reflection check.");
    }

    [Theory]
    [MemberData(nameof(RequiredBlockTypes))]
    public void Docx_exporter_has_case_for_block_type(string blockType)
    {
        var src = File.ReadAllText(DocxExportPath);
        // DocxExportService lowercases via .ToLowerInvariant() then
        // matches; check the lowercased form.
        var lower = blockType.ToLowerInvariant();
        var pattern = $"\"{lower}\"\\s*=>";
        src.Should().MatchRegex(pattern,
            $"DocxExportService.ConvertBlockSync should have a case for '{lower}' " +
            "(BlockType is lowercased before the switch). Without it the block " +
            "falls to ConvertParagraph and exports as plain text — silent " +
            "fallback that loses structure.");
    }

    private static string ResolveSourcePath(string relative)
    {
        // Walk up from the test bin dir until we hit the repo root
        // (the directory that contains src/).
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        throw new FileNotFoundException(
            $"Could not locate {relative} from test bin dir {AppContext.BaseDirectory}");
    }
}
