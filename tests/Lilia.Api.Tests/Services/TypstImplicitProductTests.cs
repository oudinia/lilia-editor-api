using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Implicit multiplication in translated math.
///
/// <para>LaTeX and Typst disagree about <c>mc</c>. LaTeX means m times c; Typst
/// reads one identifier and fails with <c>unknown variable: mc</c>. The
/// translator emitted the LaTeX spelling unchanged, so every equation with two
/// adjacent variables failed to compile and fell back to pdflatex.</para>
///
/// <para>This is the measured top cause, not a guess: of 34
/// <c>typst-compile-failed</c> events in <c>import_telemetry_events</c>, 26 were
/// this single error. The other 8 were a missing Typst binary. Not one was Typst
/// being unable to express something — while the gap catalogue rated
/// <c>math.two-letter-identifier</c> as severity <c>info</c>.</para>
/// </summary>
public class TypstImplicitProductTests
{
    private static string Split(string s) => TypstExportService.SplitImplicitProducts(s);

    // ── The bug that was actually happening ───────────────────────────

    [Fact]
    public void The_most_famous_equation_there_is_now_compiles()
    {
        // Verbatim from the telemetry: "error: unknown variable: mc", 26 times.
        Split("E = mc^2").Should().Be("E = m c^2");
    }

    [Theory]
    [InlineData("ab", "a b")]
    [InlineData("xyz", "x y z")]
    [InlineData("2ab + 3cd", "2a b + 3c d")]
    [InlineData("F = ma", "F = m a")]
    public void Adjacent_variables_become_a_product(string latex, string expected)
    {
        Split(latex).Should().Be(expected);
    }

    [Fact]
    public void A_single_letter_is_left_alone()
    {
        // Nothing to separate — and touching it would add spurious spacing.
        Split("E = m c^2").Should().Be("E = m c^2");
    }

    // ── What must survive untouched ───────────────────────────────────

    [Theory]
    [InlineData("alpha + beta")]
    [InlineData("Gamma(x)")]
    [InlineData("sin x + cos x")]
    [InlineData("log n")]
    public void Identifiers_the_translator_emits_are_not_split(string typst)
    {
        // These are produced a few lines earlier in the same method by
        // stripping LaTeX backslashes. Splitting them would break every
        // equation the translator currently gets right.
        Split(typst).Should().Be(typst);
    }

    [Theory]
    [InlineData("frac(1, 2)")]
    [InlineData("sqrt(x)")]
    [InlineData("root(3, x)")]
    [InlineData("mat(delim: \"[\", 1, 2; 3, 4)")]
    public void Function_calls_and_named_arguments_survive(string typst)
    {
        Split(typst).Should().Be(typst);
    }

    [Theory]
    [InlineData("integral.double")]
    [InlineData("limits.lim.sup")]
    [InlineData("product.co")]
    public void Dotted_paths_stay_whole(string typst)
    {
        // "double" and "co" are only meaningful as the tail of a path; split
        // in isolation they would become nonsense.
        Split(typst).Should().Be(typst);
    }

    [Fact]
    public void Text_in_quotes_is_text()
    {
        // \text{kg} arrives here already quoted. Splitting would render "k g".
        Split("9.8 \"kg\"").Should().Be("9.8 \"kg\"");
    }

    [Fact]
    public void A_styled_word_is_not_spelled_out()
    {
        // upright(Hello) is a word set upright, not five variables multiplied.
        Split("upright(Hello)").Should().Be("upright(Hello)");
    }

    [Fact]
    public void An_unsupported_command_keeps_its_name()
    {
        // Anything still carrying a backslash was not translated. Mangling it
        // into spaced letters would hide why it failed; the point is that an
        // unsupported command stays legible in the error.
        Split("\\undefinedcmd{x}").Should().Be("\\undefinedcmd{x}");
    }

    [Fact]
    public void An_unterminated_quote_does_not_lose_the_rest_of_the_equation()
    {
        // Malformed input should degrade, not truncate.
        Split("a \"unclosed").Should().Be("a \"unclosed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 + 2")]
    public void Input_with_no_variables_is_unchanged(string s)
    {
        Split(s).Should().Be(s);
    }
}
