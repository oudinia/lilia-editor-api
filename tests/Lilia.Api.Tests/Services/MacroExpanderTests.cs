using FluentAssertions;
using Lilia.Core.Capabilities;
using Lilia.Core.Services.MathParser;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Expanding a document's own macros before the maths is parsed.
///
/// <para>The parser handles 180 of 180 real equations, so it is not the
/// limitation. What it cannot do is know that <c>\R</c> means <c>\mathbb{R}</c>
/// in this document and the number 8 in another — that is not a parser's job,
/// and expansion is what removes the question.</para>
/// </summary>
public class MacroExpanderTests
{
    private static MacroExpander.Result Expand(string source, string preamble) =>
        MacroExpander.Expand(source, PreambleMacroCollector.Collect(preamble));

    // ── The basic job ─────────────────────────────────────────────────

    [Fact]
    public void A_shorthand_becomes_what_it_stands_for()
    {
        var result = Expand(@"x \in \R", @"\newcommand{\R}{\mathbb{R}}");

        result.Source.Should().Be(@"x \in \mathbb{R}");
        result.Expanded.Should().Be(1);
    }

    [Fact]
    public void The_same_name_expands_to_whatever_this_document_says()
    {
        // The reason a catalogue cannot do this. Both are real corpus
        // definitions of \R.
        Expand(@"\R", @"\newcommand{\R}{\mathbb{R}}").Source.Should().Be(@"\mathbb{R}");
        Expand(@"\R", @"\newcommand{\R}{8}").Source.Should().Be("8");
    }

    [Fact]
    public void Arguments_are_substituted()
    {
        var result = Expand(@"\abs{x+1}", @"\newcommand{\abs}[1]{\lvert#1\rvert}");

        result.Source.Should().Be(@"\lvert x+1\rvert");
    }

    [Fact]
    public void Several_arguments_land_in_the_right_places()
    {
        var result = Expand(@"\pair{a}{b}", @"\newcommand{\pair}[2]{(#1,#2)}");

        result.Source.Should().Be("(a,b)");
    }

    [Fact]
    public void A_single_token_argument_needs_no_braces()
    {
        // \frac12 is legal LaTeX and people write their own macros that way.
        Expand(@"\half2", @"\newcommand{\half}[1]{\frac{#1}{2}}")
            .Source.Should().Be(@"\frac{2}{2}");
    }

    [Fact]
    public void A_macro_defined_with_def_expands_too()
    {
        Expand(@"\eps > 0", @"\def\eps{\varepsilon}").Source.Should().Be(@"\varepsilon > 0");
    }

    [Fact]
    public void Nested_braces_in_an_argument_survive()
    {
        // No space before \frac: the backslash already ends \lvert, so TeX
        // reads them as two control sequences either way. The separator is
        // added only where it is needed — see the pair of tests below.
        Expand(@"\abs{\frac{1}{2}}", @"\newcommand{\abs}[1]{\lvert#1\rvert}")
            .Source.Should().Be(@"\lvert\frac{1}{2}\rvert");
    }

    // ── Token boundaries ──────────────────────────────────────────────

    [Fact]
    public void A_letter_argument_does_not_glue_itself_onto_the_command_before_it()
    {
        // The bug this pair exists for. \lvert#1\rvert with the argument x
        // must not become \lvertx\rvert: TeX ends a control word at the first
        // non-letter, so that is not \lvert followed by x — it is one
        // undefined control sequence called \lvertx. The equation would then
        // fail to parse, or parse as something nobody wrote.
        Expand(@"\abs{x}", @"\newcommand{\abs}[1]{\lvert#1\rvert}")
            .Source.Should().Be(@"\lvert x\rvert");
    }

    [Fact]
    public void No_space_is_added_where_none_is_needed()
    {
        // Inserting one indiscriminately would be harmless in TeX but would
        // make every expansion differ from what the author wrote, for no
        // reason, and make diffs unreadable.
        Expand(@"\sq{2}", @"\newcommand{\sq}[1]{#1^2}")
            .Source.Should().Be("2^2");
    }

    [Fact]
    public void A_macro_defined_in_terms_of_another_resolves_fully()
    {
        var result = Expand(@"\RR", "\\newcommand{\\R}{\\mathbb{R}}\n\\newcommand{\\RR}{\\R^n}");

        result.Source.Should().Be(@"\mathbb{R}^n");
        result.HitDepthLimit.Should().BeFalse();
    }

    // ── What must be left alone ───────────────────────────────────────

    [Fact]
    public void Standard_commands_are_untouched()
    {
        var result = Expand(@"\frac{1}{2} + \alpha", @"\newcommand{\R}{\mathbb{R}}");

        result.Source.Should().Be(@"\frac{1}{2} + \alpha");
        result.Expanded.Should().Be(0);
    }

    [Fact]
    public void An_unknown_command_stays_visible()
    {
        // Rewriting it would hide the one thing the parser should report.
        Expand(@"\notdefinedanywhere{x}", @"\newcommand{\R}{\mathbb{R}}")
            .Source.Should().Contain(@"\notdefinedanywhere");
    }

    [Fact]
    public void A_prefix_is_not_mistaken_for_the_macro()
    {
        // \R must not match inside \Rightarrow. Getting this wrong would
        // silently corrupt an equation that never used the macro at all.
        Expand(@"a \Rightarrow b", @"\newcommand{\R}{\mathbb{R}}")
            .Source.Should().Be(@"a \Rightarrow b");
    }

    [Fact]
    public void A_macro_called_with_too_few_arguments_is_left_for_the_parser()
    {
        // Expanding a half-applied macro would produce something that looks
        // deliberate; leaving it lets the parser report what is actually wrong.
        var result = Expand(@"\pair{a}", @"\newcommand{\pair}[2]{(#1,#2)}");

        result.Source.Should().Contain(@"\pair");
    }

    // ── Termination ───────────────────────────────────────────────────

    [Fact]
    public void A_self_referential_macro_stops_rather_than_hanging()
    {
        // \newcommand{\a}{\a} is legal to write. TeX dies on it with a capacity
        // error; here it must degrade to a partial result, not a hung request.
        var result = Expand(@"\loop", @"\newcommand{\loop}{\loop}");

        result.HitDepthLimit.Should().BeTrue();
    }

    [Fact]
    public void A_cycle_between_two_macros_also_stops()
    {
        var result = Expand(@"\a", "\\newcommand{\\a}{\\b}\n\\newcommand{\\b}{\\a}");

        result.HitDepthLimit.Should().BeTrue();
    }

    [Fact]
    public void Hitting_the_limit_is_reported_not_hidden()
    {
        // A partially expanded equation is usable; a partially expanded
        // equation nobody knows about is the failure this plan is about.
        var result = Expand(@"\loop", @"\newcommand{\loop}{\loop}");

        result.Source.Should().NotBeNullOrEmpty();
        result.HitDepthLimit.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_in_nothing_out(string? source)
    {
        MacroExpander.Expand(source, new Dictionary<string, MacroDefinition>())
            .Source.Should().BeEmpty();
    }

    [Fact]
    public void With_no_macros_the_source_is_returned_untouched()
    {
        MacroExpander.Expand(@"E = mc^2", new Dictionary<string, MacroDefinition>())
            .Source.Should().Be(@"E = mc^2");
    }

    // ── The point of the exercise ─────────────────────────────────────

    [Fact]
    public void An_equation_that_could_not_be_parsed_before_can_be_after()
    {
        // End to end: define, expand, parse. Before expansion the parser sees a
        // command it cannot know; after, it sees ordinary LaTeX — which is the
        // whole reason expansion exists rather than a cleverer parser.
        const string preamble = @"\newcommand{\R}{\mathbb{R}}\newcommand{\norm}[1]{\lVert#1\rVert}";
        var macros = PreambleMacroCollector.Collect(preamble);

        var expanded = MacroExpander.Expand(@"\norm{x} \in \R", macros);
        expanded.Source.Should().Be(@"\lVert x\rVert \in \mathbb{R}");

        var parser = new LaTeXMathParser();
        parser.Invoking(p => p.Parse(expanded.Source)).Should().NotThrow();
        parser.Parse(expanded.Source).Should().NotBeNull();
    }
}
