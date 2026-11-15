using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Typst.TypstHarness;

namespace Lilia.Api.Tests.Features.Export.Typst;

/// <summary>
/// The launch bot's #113 and #114 retests (15 Nov 2026), through the writer the preview really uses
/// (TypstExportService; an earlier fix changed an older writer the preview doesn't call):
/// - with Typst chosen on the preview, a tblr table in a raw-LaTeX block printed its source as text;
/// - a Photo (or any CV block) was dropped from a Typst PDF without a word, and Automatic chose Typst for it.
/// </summary>
public class TypstLatexOnlyBlocksTests
{
    private const string Tblr = @"\begin{tblr}{colspec={|l|c|r|}, hlines} Item & Count & Price \\ Apples & 3 & 1.20 \\ \end{tblr}";

    private static string Build(params Lilia.Core.Entities.Block[] blocks) =>
        new TypstExportService().BuildTypstDocument(Doc(), [.. blocks], null, new TypstExportOptions());

    [Fact]
    public void A_raw_LaTeX_block_is_a_note_not_its_source()
    {
        var typst = Build(Block("embed", new { engine = "latex", code = Tblr }));

        typst.Should().NotContain("tblr").And.NotContain(@"\begin");
        typst.Should().Contain(TypstExportService.RawLatexNote);
    }

    [Fact]
    public void A_Typst_embed_is_written_as_is()
        => Build(Block("embed", new { engine = "typst", code = "#table(columns: 2)[a][b]" })).Should().Contain("#table(columns: 2)[a][b]");

    [Theory]
    [InlineData("photo")]
    [InlineData("personalInfo")]
    [InlineData("cvSection")]
    [InlineData("cvEntry")]
    public void A_CV_block_is_a_note_not_dropped(string type)
        => Build(Block(type, new { src = "https://x/a.png", name = "Ada", title = "Education", role = "Analyst" }))
            .Should().Contain(TypstExportService.CvBlockNote);

    [Theory]
    [InlineData("photo")]
    [InlineData("personalInfo")]
    [InlineData("cvSection")]
    [InlineData("cvEntry")]
    public void Automatic_sends_a_document_with_CV_blocks_to_LaTeX(string type)
        => ContentRouting.WhyLatex([(type, JsonSerializer.SerializeToElement(new { src = "https://x/a.png" })),
                                    ("paragraph", JsonSerializer.SerializeToElement(new { text = "Plain text." }))])
            .Should().Be("CV blocks");

    [Fact]
    public void Plain_prose_stays_on_Typst()
        => ContentRouting.WhyLatex([("paragraph", JsonSerializer.SerializeToElement(new { text = "Plain text." }))]).Should().BeNull();
}
