using FluentAssertions;
using Lilia.Core.Blocks;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Alignment bodies stored without a mode that matches them.
///
/// <para>Found by parsing the 180 equation blocks in the database during the
/// P3.4 spike. Four of them hold an alignment body — <c>a &amp;= b \\ c &amp;= d</c>
/// — with <c>mode</c> unset. An absent mode reads as <c>display</c>, so the
/// emitter put them in <c>\begin{equation}</c>, and pdflatex answers:</para>
///
/// <code>
/// ! Misplaced alignment tab character &amp;.
/// !  ==&gt; Fatal error occurred, no output PDF file produced!
/// </code>
///
/// <para>Verified against the real compiler, not inferred. It is not a
/// blemish on one equation: the export produces <b>no PDF at all</b>.</para>
///
/// <para>The distinction that makes this safe is top-level versus nested. An
/// <c>&amp;</c> inside <c>pmatrix</c> or <c>cases</c> is ordinary and correct
/// — one of the four blocks is exactly that, a matrix that must be left
/// alone. Confusing the two would corrupt working documents in the course of
/// fixing broken ones.</para>
/// </summary>
public class EquationAlignmentTests
{
    // ── The bodies that were failing ──────────────────────────────────

    [Fact]
    public void An_alignment_body_is_recognised()
    {
        // Verbatim from the corpus.
        EquationContent.HasTopLevelAlignment(@"a^2 + 2ab + b^2 &= 2ab + c^2 \\ a^2 + b^2 &= c^2.")
            .Should().BeTrue();
    }

    [Fact]
    public void A_multi_line_alignment_body_is_recognised()
    {
        EquationContent.HasTopLevelAlignment(
            @"t' &= \gamma\!\left(t - \frac{v x}{c^2}\right), \\ x' &= \gamma(x - v t), \\ y' &= y")
            .Should().BeTrue();
    }

    // ── What must not be touched ──────────────────────────────────────

    [Fact]
    public void A_matrix_keeps_its_own_alignment_tabs()
    {
        // The fourth corpus block. Its tabs belong to pmatrix and are already
        // correct inside \begin{equation}; promoting it to align would change
        // a document that renders perfectly well.
        EquationContent.HasTopLevelAlignment(
            @"h_{\mu\nu}^{\mathrm{TT}} = \begin{pmatrix} 0 & 0 \\ 0 & h_+ \end{pmatrix}")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(@"\begin{cases} x & x > 0 \\ -x & x \le 0 \end{cases}")]
    [InlineData(@"\begin{array}{cc} a & b \\ c & d \end{array}")]
    [InlineData(@"\begin{bmatrix} 1 & 0 \\ 0 & 1 \end{bmatrix}")]
    public void Tabs_inside_any_environment_belong_to_it(string source)
    {
        EquationContent.HasTopLevelAlignment(source).Should().BeFalse();
    }

    [Fact]
    public void An_escaped_ampersand_is_a_literal_not_a_tab()
    {
        // \& prints an ampersand. Reading it as alignment would promote an
        // equation that was never broken.
        EquationContent.HasTopLevelAlignment(@"P(A \& B) = 0.5").Should().BeFalse();
    }

    [Fact]
    public void A_row_break_does_not_swallow_the_following_tab()
    {
        // `\\` is two characters, and the escape handling has to consume both.
        // If it consumed only the first, the second backslash would be read as
        // escaping the `&` and the alignment would be missed.
        EquationContent.HasTopLevelAlignment(@"a &= 1 \\ &= 2").Should().BeTrue();
    }

    [Fact]
    public void A_tab_after_a_closed_environment_still_counts()
    {
        // Depth has to come back down. Otherwise anything following a matrix
        // is treated as nested forever, and a genuinely broken equation is
        // reported as fine.
        EquationContent.HasTopLevelAlignment(
            @"\begin{pmatrix} 1 & 2 \end{pmatrix} &= M").Should().BeTrue();
    }

    [Fact]
    public void Nested_environments_unwind_correctly()
    {
        EquationContent.HasTopLevelAlignment(
            @"\begin{aligned} \begin{pmatrix} 1 & 2 \end{pmatrix} & = M \end{aligned}")
            .Should().BeFalse();
    }

    // ── Ordinary equations ────────────────────────────────────────────

    [Theory]
    [InlineData(@"E = mc^2")]
    [InlineData(@"\frac{1}{2}")]
    [InlineData(@"\int_0^1 x \, dx")]
    public void An_equation_with_no_tabs_is_left_alone(string source)
    {
        EquationContent.HasTopLevelAlignment(source).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_is_claimed_about_an_empty_equation(string? source)
    {
        EquationContent.HasTopLevelAlignment(source).Should().BeFalse();
    }

    [Fact]
    public void An_unclosed_environment_does_not_run_off_the_end()
    {
        // Malformed input should answer, not crash — the emitter calls this on
        // whatever the user typed.
        EquationContent.HasTopLevelAlignment(@"\begin{pmatrix} 1 & 2").Should().BeFalse();
    }

    [Fact]
    public void A_trailing_backslash_does_not_read_past_the_string()
    {
        EquationContent.HasTopLevelAlignment(@"a = b \").Should().BeFalse();
    }
}
