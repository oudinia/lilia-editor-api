using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Blocks;
using Lilia.Core.Capabilities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Storing the equation's structure alongside its source.
///
/// <para><c>source</c> stays verbatim and authoritative — LaTeX export emits
/// exactly what the author typed. The tree is for the questions a string cannot
/// answer: whether a fraction has an empty denominator without compiling to find
/// out, structure for accessible PDF, search by meaning rather than by
/// characters.</para>
///
/// <para>It is built on write rather than derived on read because the
/// document's macros are known at write time and are not known later — an
/// equation reaching a reader has no preamble attached to it.</para>
/// </summary>
public class EquationAstStorageTests
{
    private static JsonElement Normalise(
        string contentJson,
        string? preamble = null,
        string blockType = "equation")
    {
        using var input = JsonDocument.Parse(contentJson);
        var macros = preamble is null ? null : PreambleMacroCollector.Collect(preamble);

        return JsonDocument
            .Parse(BlockContentNormaliser.Normalise(blockType, input.RootElement, macros).RootElement.GetRawText())
            .RootElement.Clone();
    }

    // ── What gets stored ──────────────────────────────────────────────

    [Fact]
    public void An_equation_gains_a_tree()
    {
        var content = Normalise("""{"source":"E = mc^2"}""");

        content.TryGetProperty("ast", out var ast).Should().BeTrue();
        ast.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void The_tree_carries_structure_rather_than_the_string_again()
    {
        // If \frac came back as text, the AST would buy nothing over `source`.
        var content = Normalise("""{"source":"\\frac{a}{b}"}""");

        var ast = content.GetProperty("ast");
        ast.GetProperty("type").GetString().Should().Be("fraction");
        ast.TryGetProperty("Numerator", out _).Should().BeTrue();
        ast.TryGetProperty("Denominator", out _).Should().BeTrue();
    }

    [Fact]
    public void The_source_is_still_exactly_what_the_author_typed()
    {
        // Non-negotiable. The LaTeX path emits `source`, and rewriting it to
        // match the tree would change documents that compile perfectly well.
        const string typed = @"\frac{1}{2}   +   \alpha";
        var content = Normalise($$"""{"source":"\\frac{1}{2}   +   \\alpha"}""");

        content.GetProperty("source").GetString().Should().Be(typed);
    }

    // ── Macros ────────────────────────────────────────────────────────

    [Fact]
    public void A_document_macro_is_resolved_before_the_tree_is_built()
    {
        // The reason macros are passed in at all: \R is not standard LaTeX, and
        // the parser cannot know this document means the reals by it.
        var content = Normalise(
            """{"source":"x \\in \\R"}""",
            preamble: @"\newcommand{\R}{\mathbb{R}}");

        content.TryGetProperty("ast", out var ast).Should().BeTrue();
        ast.GetRawText().Should().NotContain(@"\R", "the macro should have been expanded away");
    }

    [Fact]
    public void The_stored_source_keeps_the_macro_the_author_wrote()
    {
        // Expansion is for the tree only. Rewriting `source` would replace the
        // author's shorthand throughout their document, which is not a fix —
        // it is vandalism with good intentions.
        var content = Normalise(
            """{"source":"x \\in \\R"}""",
            preamble: @"\newcommand{\R}{\mathbb{R}}");

        content.GetProperty("source").GetString().Should().Be(@"x \in \R");
    }

    // ── When it cannot be built ───────────────────────────────────────

    [Fact]
    public void A_half_typed_equation_still_saves()
    {
        // Every keystroke goes through here. \frac{ is a normal intermediate
        // state, and refusing to save it would lose the author's work over a
        // feature they did not ask for.
        var act = () => Normalise("""{"source":"\\frac{"}""");

        act.Should().NotThrow();
    }

    [Fact]
    public void An_empty_equation_gets_no_tree_and_no_noise()
    {
        var content = Normalise("""{"source":""}""");

        content.TryGetProperty("ast", out _).Should().BeFalse();
    }

    [Fact]
    public void A_tree_that_cannot_be_built_is_absent_rather_than_null()
    {
        // A reader must be able to tell "nobody could parse this" from "this
        // parsed to nothing". Absent is the only honest signal for the first,
        // since null and absent are indistinguishable to most JSON readers.
        var content = Normalise("""{"source":"   ","ast":{"stale":true}}""");

        content.TryGetProperty("ast", out _).Should().BeFalse();
    }

    [Fact]
    public void A_stale_tree_is_replaced_not_kept()
    {
        // The dangerous case: an edit arrives carrying the AST of the previous
        // version. Keeping it would leave the tree describing maths the
        // document no longer contains — and nothing would say so.
        var content = Normalise("""{"source":"\\frac{a}{b}","ast":{"type":"variable","Name":"stale"}}""");

        content.GetProperty("ast").GetProperty("type").GetString().Should().Be("fraction");
    }

    // ── Other block types ─────────────────────────────────────────────

    [Fact]
    public void A_paragraph_is_left_completely_alone()
    {
        var content = Normalise("""{"text":"hello"}""", blockType: "paragraph");

        content.TryGetProperty("ast", out _).Should().BeFalse();
        content.GetProperty("text").GetString().Should().Be("hello");
    }

    [Fact]
    public void The_legacy_latex_key_still_produces_a_tree()
    {
        // Most rows are still in the pre-P1.4 shape, and they should not have
        // to be migrated before they gain structure.
        var content = Normalise("""{"latex":"E = mc^2"}""");

        content.TryGetProperty("ast", out _).Should().BeTrue();
    }
}
