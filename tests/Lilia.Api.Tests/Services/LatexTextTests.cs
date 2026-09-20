using FluentAssertions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Turning typed text into LaTeX source.
///
/// <para>Seven copies of this existed, all escaping by chained <c>Replace</c>, and
/// all carrying the same defect: <c>\</c> became <c>\textbackslash{}</c> first,
/// and the later <c>{</c>/<c>}</c> replacements escaped the braces that step had
/// just inserted. These pin the property that makes the class of bug impossible —
/// escape output is never escaped again — and the small set of markup a table
/// cell is allowed to carry.</para>
/// </summary>
public class LatexTextTests
{
    [Fact]
    public void A_backslash_survives_as_a_backslash()
    {
        // The old chained version produced \textbackslash\{\}, which renders "\{}".
        LatexText.Escape("a\\b").Should().Be("a\\textbackslash{}b");
    }

    [Fact]
    public void Escape_output_is_never_escaped_again()
    {
        // Every escape that emits braces or a backslash must survive intact.
        LatexText.Escape("^").Should().Be("\\textasciicircum{}");
        LatexText.Escape("~").Should().Be("\\textasciitilde{}");
        LatexText.Escape("{}").Should().Be("\\{\\}");
    }

    [Theory]
    [InlineData("50%", "50\\%")]
    [InlineData("R&D", "R\\&D")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("#1", "\\#1")]
    [InlineData("$5", "\\$5")]
    [InlineData("plain text", "plain text")]
    public void Special_characters_are_escaped(string input, string expected)
    {
        LatexText.Escape(input).Should().Be(expected);
    }

    [Fact]
    public void Escape_handles_null_and_empty()
    {
        LatexText.Escape(null).Should().BeEmpty();
        LatexText.Escape("").Should().BeEmpty();
    }

    // ── cells ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_cell_keeps_bold_because_the_editor_offers_it()
    {
        // \textbf{…} is documented in the lilia-table skill, appears in the tool's
        // own sample table, and renders as bold in the client preview. Escaping it
        // server-side meant compiling a different document than the author saw.
        LatexText.EscapeCell("\\textbf{Ours}").Should().Be("\\textbf{Ours}");
    }

    [Fact]
    public void A_cell_keeps_inline_math()
    {
        LatexText.EscapeCell("$\\Delta$").Should().Be("$\\Delta$");
        LatexText.EscapeCell("$+2.7$").Should().Be("$+2.7$");
    }

    [Fact]
    public void Text_around_the_markup_is_still_escaped()
    {
        LatexText.EscapeCell("50% \\textbf{up}").Should().Be("50\\% \\textbf{up}");
    }

    [Fact]
    public void Content_inside_bold_is_still_user_text()
    {
        // The command is authored markup; what it wraps is not.
        LatexText.EscapeCell("\\textbf{100%}").Should().Be("\\textbf{100\\%}");
    }

    [Fact]
    public void Anything_outside_the_recognised_set_is_escaped()
    {
        // Only \textbf and $…$ are recognised — matching the client's renderer.
        // A wider set on the server alone would recreate the divergence.
        LatexText.EscapeCell("\\undefinedcmd{x}")
            .Should().Be("\\textbackslash{}undefinedcmd\\{x\\}");
    }

    [Fact]
    public void An_unclosed_construct_is_escaped_rather_than_trusted()
    {
        LatexText.EscapeCell("\\textbf{oops").Should().StartWith("\\textbackslash{}");
        LatexText.EscapeCell("$unclosed").Should().Be("\\$unclosed");
    }

    // ── IsWhollyBold — so the table renderer does not bold a header twice ──

    [Fact]
    public void A_cell_that_is_entirely_one_textbf_is_wholly_bold()
    {
        LatexText.IsWhollyBold("\\textbf{Ours}").Should().BeTrue();
        LatexText.IsWhollyBold("  \\textbf{Ours}  ").Should().BeTrue();
    }

    [Fact]
    public void Nested_braces_inside_the_command_still_count_as_wholly_bold()
    {
        LatexText.IsWhollyBold("\\textbf{a{b}c}").Should().BeTrue();
    }

    [Fact]
    public void Two_bold_runs_are_not_wholly_bold()
    {
        // Starts with the command and ends with a brace, so matching on the
        // first and last character would get this wrong.
        LatexText.IsWhollyBold("\\textbf{a} and \\textbf{b}").Should().BeFalse();
    }

    [Fact]
    public void Bold_followed_by_plain_text_is_not_wholly_bold()
    {
        LatexText.IsWhollyBold("\\textbf{a} b").Should().BeFalse();
    }

    [Fact]
    public void Plain_text_and_empty_cells_are_not_wholly_bold()
    {
        LatexText.IsWhollyBold("Ours").Should().BeFalse();
        LatexText.IsWhollyBold("").Should().BeFalse();
        LatexText.IsWhollyBold(null).Should().BeFalse();
    }

    [Fact]
    public void An_unclosed_textbf_is_not_wholly_bold()
    {
        LatexText.IsWhollyBold("\\textbf{oops").Should().BeFalse();
    }
}
