using FluentAssertions;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tier 2 of the Typst export defence: every canonical user-content
/// block type must have a case branch in
/// <c>TypstExportService.RenderBlock</c>.
///
/// Mirror of <c>ExportHandlerCoverageTests</c> for the LaTeX side.
/// Pre-fix, adding a new block type to <c>BlockTypes</c> didn't fail
/// any test if the Typst exporter silently fell back to the
/// "[Unsupported block type]" comment marker. This gate catches
/// missing handlers the day a new block type lands.
///
/// Verticals (slide, inv-*, frontMatter/backMatter, etc.) are skipped
/// — those are out-of-scope for the general Typst exporter.
/// </summary>
public class TypstHandlerCoverageTests
{
    /// <summary>
    /// Block types we require the Typst exporter to handle.
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

    private static readonly string TypstExportPath = ResolveSourcePath(
        "src/Lilia.Api/Services/TypstExportService.cs");

    public static IEnumerable<object[]> RequiredBlockTypes() =>
        RequiredCoverage.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(RequiredBlockTypes))]
    public void Typst_exporter_has_case_for_block_type(string blockType)
    {
        var src = File.ReadAllText(TypstExportPath);
        // Match `"<type>"` followed by either `=>` directly or `or "..."`
        // (combined cases like `"tableOfContents" or "toc" =>`).
        var pattern = $"\"{blockType}\"\\s*(=>|or\\s+\")";
        src.Should().MatchRegex(pattern,
            $"TypstExportService.RenderBlock should have a case for '{blockType}' — " +
            "without it the block silently falls to the \"[Unsupported]\" comment " +
            "marker. See TypstHandlerCoverageTests for the reflection check.");
    }

    private static string ResolveSourcePath(string relative)
    {
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
