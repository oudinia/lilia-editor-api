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

    // The document sweep (D-013): "\item {About work}" kept its braces in the list item.
    [Theory]
    [InlineData(@"{About work}", "About work")]
    [InlineData(@"Text {Certifying Institution Name} here", "Text Certifying Institution Name here")]
    [InlineData(@"{}", "")]
    public void A_bare_group_of_plain_text_loses_its_braces(string latex, string expected)
        => LatexParser.DeclarationGroups(latex).Should().Be(expected);

    [Theory]
    [InlineData(@"{\item x}")]          // \it must not eat \item
    [InlineData(@"\textbf{x}")]         // a command's argument
    [InlineData(@"x^{2} and a_{i}")]     // scripts keep their groups
    [InlineData(@"\{literal\}")]        // escaped braces are text
    [InlineData(@"$\{x\}$")]            // maths untouched (and set aside before this runs)
    public void Other_groups_are_left_alone(string latex)
        => LatexParser.DeclarationGroups(latex).Should().Be(latex);

    [Fact]
    public async Task A_list_item_written_as_a_group_imports_without_braces()
    {
        var parsed = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n\\begin{itemize}\n\\item {About work}\n\\item Plain item\n\\end{itemize}\n\\end{document}\n");
        var items = parsed.Elements.OfType<ImportListItem>().Select(i => i.Text).ToList();
        items.Should().Contain("About work").And.Contain("Plain item");
        items.Should().NotContain(i => i.Contains('{') || i.Contains('}'));
    }

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
