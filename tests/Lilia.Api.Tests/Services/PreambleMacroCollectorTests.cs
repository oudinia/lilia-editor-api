using FluentAssertions;
using Lilia.Core.Capabilities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Collecting the macros a document defines for itself.
///
/// <para>Most of the inputs below are <b>verbatim from the TeX.SE corpus</b>,
/// not invented. That matters because the point of the collector is to handle
/// what people actually write, and what people actually write includes
/// <c>\newcommand{\R}{8}</c>.</para>
///
/// <para>The asymmetry to keep in mind throughout: collecting a macro that is
/// not there is far worse than missing one that is. A missed macro means the
/// capability report flags a command the document defines — annoying, and
/// visible. A phantom macro means the report says a genuinely missing command
/// is fine, which is a false all-clear and exactly the failure this plan
/// exists to remove.</para>
/// </summary>
public class PreambleMacroCollectorTests
{
    private static IReadOnlyDictionary<string, MacroDefinition> Collect(string source) =>
        PreambleMacroCollector.Collect(source);

    // ── The forms people actually use ─────────────────────────────────

    [Fact]
    public void Newcommand_is_collected()
    {
        var macros = Collect(@"\newcommand{\R}{\mathbb{R}}");

        macros.Should().ContainKey(@"\R");
        macros[@"\R"].Body.Should().Be(@"\mathbb{R}");
        macros[@"\R"].Arity.Should().Be(0);
    }

    [Fact]
    public void Def_is_collected_because_it_is_nearly_as_common()
    {
        // 9.5% of corpus posts, against 9.9% for \newcommand. An earlier count
        // put \def at exactly 0.0% — a clean zero that was Postgres treating
        // the backslash in a LIKE pattern as an escape. Dropping \def on that
        // number would have missed 1,962 posts.
        var macros = Collect(@"\def\eps{\varepsilon}");

        macros.Should().ContainKey(@"\eps");
        macros[@"\eps"].Form.Should().Be("def");
    }

    [Theory]
    [InlineData(@"\renewcommand{\phi}{\varphi}", @"\phi")]
    [InlineData(@"\providecommand{\abs}[1]{|#1|}", @"\abs")]
    [InlineData(@"\newcommand*{\N}{\mathbb{N}}", @"\N")]
    public void The_other_spellings_are_collected_too(string source, string name)
    {
        Collect(source).Should().ContainKey(name);
    }

    [Fact]
    public void Declaremathoperator_is_a_macro_by_another_name()
    {
        // 83 corpus posts use it, and a document defining \argmax this way has
        // just as much claim to it as one using \newcommand.
        var macros = Collect(@"\DeclareMathOperator{\argmax}{arg\,max}");

        macros.Should().ContainKey(@"\argmax");
    }

    [Fact]
    public void A_name_without_braces_is_still_a_definition()
    {
        // \newcommand\foo{...} is legal and appears in the corpus.
        Collect(@"\newcommand\foo{bar}").Should().ContainKey(@"\foo");
    }

    // ── Arity ─────────────────────────────────────────────────────────

    [Fact]
    public void Argument_count_is_read()
    {
        // Verbatim from the corpus. The parser needs the arity to expand it.
        var macros = Collect(@"\newcommand{\abs}[1]{\lvert#1\rvert}");

        macros[@"\abs"].Arity.Should().Be(1);
    }

    [Fact]
    public void A_default_argument_is_not_mistaken_for_the_count()
    {
        // \newcommand{\x}[2][default]{...} takes 2 arguments, the first
        // optional. Reading the second bracket as the count would report the
        // wrong arity and the expansion would be wrong.
        var macros = Collect(@"\newcommand{\pair}[2][x]{(#1,#2)}");

        macros[@"\pair"].Arity.Should().Be(2);
    }

    [Fact]
    public void Def_arity_comes_from_its_parameter_tokens()
    {
        // \def has no bracketed count — the parameters are written out.
        var macros = Collect(@"\def\pair#1#2{(#1,#2)}");

        macros[@"\pair"].Arity.Should().Be(2);
    }

    // ── Bodies with structure ─────────────────────────────────────────

    [Fact]
    public void Nested_braces_do_not_end_the_body_early()
    {
        var macros = Collect(@"\newcommand{\norm}[1]{\left\lVert{#1}\right\rVert}");

        macros[@"\norm"].Body.Should().Be(@"\left\lVert{#1}\right\rVert");
    }

    [Fact]
    public void An_escaped_brace_is_not_a_delimiter()
    {
        var macros = Collect(@"\newcommand{\set}[1]{\{#1\}}");

        macros.Should().ContainKey(@"\set");
        macros[@"\set"].Body.Should().Be(@"\{#1\}");
    }

    [Fact]
    public void An_unbalanced_definition_is_skipped_rather_than_guessed()
    {
        // Better to miss a macro than to invent where its body ended and then
        // report the rest of the document as part of it.
        Collect(@"\newcommand{\broken}{\mathbb{R}").Should().BeEmpty();
    }

    // ── Comments ──────────────────────────────────────────────────────

    [Fact]
    public void A_commented_out_definition_is_not_collected()
    {
        // The dangerous direction. A phantom macro makes the report say a
        // genuinely missing command is fine.
        Collect("% \\newcommand{\\R}{\\mathbb{R}}").Should().BeEmpty();
    }

    [Fact]
    public void A_definition_after_a_comment_on_an_earlier_line_survives()
    {
        var macros = Collect("% a note about macros\n\\newcommand{\\R}{\\mathbb{R}}");

        macros.Should().ContainKey(@"\R");
    }

    [Fact]
    public void An_escaped_percent_does_not_start_a_comment()
    {
        var macros = Collect(@"\newcommand{\pct}{50\% off}");

        macros.Should().ContainKey(@"\pct");
        macros[@"\pct"].Body.Should().Contain(@"\%");
    }

    // ── TeX semantics ─────────────────────────────────────────────────

    [Fact]
    public void The_later_definition_wins()
    {
        // What TeX does: a \renewcommand after a \newcommand replaces it.
        var macros = Collect("\\newcommand{\\R}{\\mathbb{R}}\n\\renewcommand{\\R}{\\mathbf{R}}");

        macros[@"\R"].Body.Should().Be(@"\mathbf{R}");
    }

    // ── Real corpus definitions ───────────────────────────────────────

    [Fact]
    public void The_five_meanings_of_R_are_all_collected()
    {
        // Verbatim from five different corpus posts. The same name, five
        // different things — including a plain number. This is why no catalogue
        // can hold these and only the document can answer.
        foreach (var (source, body) in new[]
        {
            (@"\newcommand{\R}{\mathbb{R}}", @"\mathbb{R}"),
            (@"\newcommand{\R}{8}", "8"),
            (@"\newcommand{\R}{\mathbbm{R}}", @"\mathbbm{R}"),
            (@"\newcommand{\R}{\mathbb R}", @"\mathbb R"),
            (@"\newcommand{\R} {\mbox {$ I\!\!R $}}", @"\mbox {$ I\!\!R $}"),
        })
        {
            var macros = Collect(source);
            macros.Should().ContainKey(@"\R", $"'{source}' defines it");
            macros[@"\R"].Body.Trim().Should().Be(body);
        }
    }

    [Fact]
    public void A_realistic_preamble_yields_every_macro()
    {
        const string preamble = """
            \documentclass{article}
            \usepackage{amsmath}
            % shorthands
            \newcommand{\R}{\mathbb{R}}
            \newcommand{\abs}[1]{\lvert#1\rvert}
            \def\eps{\varepsilon}
            \DeclareMathOperator{\argmax}{arg\,max}
            \begin{document}
            """;

        var macros = Collect(preamble);

        macros.Keys.Should().BeEquivalentTo([@"\R", @"\abs", @"\eps", @"\argmax"]);
    }

    [Fact]
    public void A_usepackage_is_not_a_macro_definition()
    {
        Collect(@"\usepackage{amsmath}").Should().BeEmpty();
    }

    // ── Lookup ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("R")]
    [InlineData(@"\R")]
    public void Lookup_tolerates_either_spelling_of_the_name(string asked)
    {
        // Requirements normalise with a leading backslash; catalogue rows and
        // parsed source do not agree. Both must find the same macro.
        var macros = Collect(@"\newcommand{\R}{\mathbb{R}}");

        PreambleMacroCollector.Defines(macros, asked).Should().BeTrue();
    }

    [Fact]
    public void A_command_the_document_does_not_define_is_not_claimed()
    {
        var macros = Collect(@"\newcommand{\R}{\mathbb{R}}");

        PreambleMacroCollector.Defines(macros, @"\frac").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_nothing_out(string? source)
    {
        PreambleMacroCollector.Collect(source).Should().BeEmpty();
    }
}
