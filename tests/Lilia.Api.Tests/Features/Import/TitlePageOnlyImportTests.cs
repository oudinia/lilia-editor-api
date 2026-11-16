using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep's chunk 7 (a TeX Stack Exchange deck, 16 Nov 2026): a whole document whose body held only
/// \titlepage imported as one paragraph holding the entire source, \documentclass and preamble included, and the PDF
/// stopped ("Can be used only in preamble"). It imports as its title page now.
/// </summary>
public class TitlePageOnlyImportTests
{
    private static Task<PastedBlocks> Parse(string tex) => PastedText.ParseAsync(tex, "LATEX", new LatexParser(), new LmlTextParser());

    [Theory]
    [InlineData("beamer", @"\begin{frame}\titlepage\end{frame}")]
    [InlineData("article", @"\maketitle")]
    public async Task A_document_that_is_only_its_title_page_imports_as_a_title_block(string cls, string body)
    {
        var pasted = await Parse($"\\documentclass{{{cls}}}\n\\title{{There Is No Largest Prime}}\n\\author{{Euclid}}\n\\begin{{document}}\n{body}\n\\end{{document}}");
        pasted.Fallback.Should().BeNull();
        pasted.Blocks.Should().ContainSingle().Which.Type.Should().Be("title");
        JsonSerializer.Serialize(pasted.Blocks[0].Content).Should().Contain("There Is No Largest Prime").And.Contain("Euclid");
        pasted.Blocks.Should().NotContain(b => JsonSerializer.Serialize(b.Content).Contains("documentclass"));
    }

    [Fact]
    public async Task A_whole_document_with_an_empty_body_imports_as_nothing()
    {
        var pasted = await Parse("\\documentclass{article}\n\\begin{document}\n\\end{document}");
        pasted.Blocks.Should().BeEmpty();
        pasted.Fallback.Should().BeNull();
    }

    [Fact]
    public void A_commented_out_class_line_is_not_a_whole_document() =>
        PastedText.IsWholeDocument("% \\documentclass{article}\n% \\begin{document}\nJust text.").Should().BeFalse();
}
