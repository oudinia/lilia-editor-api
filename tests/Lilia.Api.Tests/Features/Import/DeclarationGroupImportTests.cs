using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep (D-067, 16 Nov 2026): {\small small text.} imported as "{ small text.}", the declaration stripped
/// and its braces left. Grouped declarations now become the editor's marks, or plain text when there is no mark.
/// </summary>
public class DeclarationGroupImportTests
{
    [Theory]
    [InlineData(@"{\small small text.}", "small text.")]
    [InlineData(@"{\Huge Huge.}", "Huge.")]
    [InlineData(@"{\bf bold} and {\bfseries also bold}", "**bold** and **also bold**")]
    [InlineData(@"{\em stressed} and {\it slanted}", "*stressed* and *slanted*")]
    [InlineData(@"{\sc Small Caps}", "^^Small Caps^^")]
    [InlineData(@"{\small {\bf nested}}", "**nested**")]
    [InlineData(@"{\ttfamily mono}", "mono")]
    [InlineData(@"\textbf{\Large Git Cheatsheet}", @"\textbf{Git Cheatsheet}")]   // a command's argument keeps its braces
    [InlineData(@"\href{u}{\bf link}", @"\href{u}{**link**}")]
    public void A_declaration_group_becomes_its_mark_or_its_text(string latex, string expected)
        => LatexParser.DeclarationGroups(latex).Should().Be(expected);

    [Theory]
    [InlineData(@"{\item x}")]          // \it must not eat \item
    [InlineData(@"{x}")]                // a plain group is not a declaration
    [InlineData(@"$\{x\}$")]            // maths untouched (and set aside before this runs)
    public void Other_groups_are_left_alone(string latex)
        => LatexParser.DeclarationGroups(latex).Should().Be(latex);

    [Fact]
    public async Task The_sweep_fixture_line_imports_without_braces()
    {
        var tex = "\\documentclass{article}\n\\begin{document}\nNormal sized text. {\\small small text.} {\\large large text.} {\\Large Larger.} {\\Huge Huge.}\n\\end{document}\n";
        var parsed = await new LatexParser().ParseTextAsync(tex);
        var text = string.Join(" ", parsed.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        text.Should().Contain("small text.").And.Contain("Huge.");
        text.Should().NotContain("{").And.NotContain("}");
    }
}
