using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Capabilities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// P3.3 phase 3 — reading a document's requirements before rendering it.
///
/// <para>The extractor's value depends entirely on being believable. A report
/// that flags things which turn out fine gets skimmed past, and once that
/// happens it is worth less than no report at all — it is trusted less than
/// nothing while still looking like diligence. So most of what is asserted here
/// is what it must <b>not</b> collect.</para>
/// </summary>
public class RequirementExtractorTests
{
    private static JsonElement Block(string json) => JsonDocument.Parse(json).RootElement;

    private static IReadOnlyList<Requirement> From(params string[] blocks) =>
        RequirementExtractor.Extract(blocks.Select(Block));

    // ── Characters ────────────────────────────────────────────────────

    [Fact]
    public void Ascii_prose_needs_nothing()
    {
        // Every target renders it. Collecting it would bury the real entries.
        From("""{"text":"Hello, world! (1975)"}""").Should().BeEmpty();
    }

    [Fact]
    public void A_non_ascii_character_is_collected_with_its_script()
    {
        var requirements = From("""{"text":"中文"}""");

        requirements.OfType<CodepointRequirement>().Should().Contain(c => c.Codepoint == 0x4E2D);
        requirements.OfType<ScriptRequirement>().Select(s => s.Script).Should().Contain("Han");
    }

    [Fact]
    public void A_repeated_character_is_one_requirement()
    {
        // A CJK document repeats characters constantly. One requirement per
        // occurrence would make a document-sized question expensive enough that
        // callers stop asking it.
        From("""{"text":"中中中文文"}""")
            .OfType<CodepointRequirement>().Should().HaveCount(2);
    }

    [Fact]
    public void An_astral_character_is_one_requirement_not_two_halves()
    {
        var requirements = From("""{"text":"hi 😀"}""");

        requirements.OfType<CodepointRequirement>().Should().ContainSingle()
            .Which.Codepoint.Should().Be(0x1F600);
    }

    [Theory]
    [InlineData("العربية", "Arabic")]
    [InlineData("עברית", "Hebrew")]
    [InlineData("Ελληνικά", "Greek")]
    [InlineData("한국어", "Hangul")]
    public void Scripts_that_change_what_a_renderer_must_do_are_named(string text, string script)
    {
        From($$"""{"text":"{{text}}"}""")
            .OfType<ScriptRequirement>().Select(s => s.Script).Should().Contain(script);
    }

    [Fact]
    public void An_accented_latin_letter_needs_no_script()
    {
        // é needs a code point that the font must cover, but not right-to-left
        // layout or contextual shaping. Naming a script for it would add a row
        // to every European document that nobody can act on.
        var requirements = From("""{"text":"café"}""");

        requirements.OfType<CodepointRequirement>().Should().ContainSingle();
        requirements.OfType<ScriptRequirement>().Should().BeEmpty();
    }

    [Fact]
    public void Characters_are_found_wherever_they_appear()
    {
        // Prose, captions, alt text, list items. A character in a caption fails
        // to render exactly as loudly as one in a paragraph.
        From("""{"caption":"图 1","items":["中"],"alt":"文"}""")
            .OfType<CodepointRequirement>().Should().HaveCount(3);
    }

    [Fact]
    public void An_unfamiliar_block_shape_is_still_examined()
    {
        // A block type added later must not go unexamined because nobody
        // updated a list of known keys here.
        From("""{"somethingNobodyPlannedFor":{"nested":["中"]}}""")
            .OfType<CodepointRequirement>().Should().NotBeEmpty();
    }

    // ── Commands ──────────────────────────────────────────────────────

    [Fact]
    public void Commands_come_from_latex_fields()
    {
        From("""{"source":"\\alpha + \\beta"}""")
            .OfType<CommandRequirement>().Select(c => c.Normalised)
            .Should().BeEquivalentTo("\\alpha", "\\beta");
    }

    [Fact]
    public void The_legacy_latex_key_is_read_too()
    {
        // Pre-P1.4 spelling, and still the shape most rows are in.
        From("""{"latex":"\\gamma"}""")
            .OfType<CommandRequirement>().Should().NotBeEmpty();
    }

    [Fact]
    public void Prose_is_not_scanned_for_commands()
    {
        // The single most important negative. A Windows path or a code sample
        // in a paragraph is not a control sequence, and reporting it as one is
        // how a pre-flight report becomes noise.
        From("""{"text":"Save to C:\\Users\\alpha and run \\begin here"}""")
            .OfType<CommandRequirement>().Should().BeEmpty();
    }

    [Fact]
    public void A_code_block_is_not_latex()
    {
        From("""{"language":"c","text":"printf(\"\\n\"); \\newline"}""")
            .OfType<CommandRequirement>().Should().BeEmpty();
    }

    [Fact]
    public void Nested_values_under_a_latex_key_stay_latex()
    {
        From("""{"source":{"parts":["\\frac{1}{2}"]}}""")
            .OfType<CommandRequirement>().Select(c => c.Normalised).Should().Contain("\\frac");
    }

    [Fact]
    public void A_command_repeated_across_blocks_is_one_requirement()
    {
        From("""{"source":"\\alpha"}""", """{"source":"\\alpha"}""")
            .OfType<CommandRequirement>().Should().ContainSingle();
    }

    // ── Document-level ────────────────────────────────────────────────

    [Fact]
    public void The_font_and_class_are_requirements_too()
    {
        var requirements = RequirementExtractor.Extract([], "Linux Libertine", "beamer");

        requirements.OfType<FontRequirement>().Select(f => f.Family).Should().Contain("Linux Libertine");
        requirements.OfType<DocumentClassRequirement>().Select(d => d.Name).Should().Contain("beamer");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_font_is_not_a_requirement(string? font)
    {
        RequirementExtractor.Extract([], font).Should().BeEmpty();
    }

    // ── Determinism ───────────────────────────────────────────────────

    [Fact]
    public void The_same_document_produces_the_same_order()
    {
        // Two runs are compared during investigation; an unstable order makes
        // the diff unreadable.
        const string block = """{"text":"中文 עברית","source":"\\beta \\alpha"}""";

        From(block).Select(r => r.Key).Should().Equal(From(block).Select(r => r.Key));
    }

    [Fact]
    public void An_empty_document_needs_nothing()
    {
        RequirementExtractor.Extract([]).Should().BeEmpty();
    }

    // ── The script table ──────────────────────────────────────────────

    [Theory]
    [InlineData(0x00E9, null)]   // é — a code point to cover, but no script behaviour
    [InlineData(0x4E2D, "Han")]
    [InlineData(0x05E9, "Hebrew")]
    [InlineData(0x0627, "Arabic")]
    [InlineData(0x0915, "Devanagari")]
    [InlineData(0x0E01, "Thai")]
    [InlineData(0x0410, "Cyrillic")]
    public void A_script_is_named_only_when_it_changes_what_a_renderer_must_do(int codepoint, string? script)
    {
        // The table is coarse on purpose — .NET exposes no Unicode Script
        // property, and the useful resolution of the question is "does this
        // need a CJK font, right-to-left layout, or contextual shaping".
        RequirementExtractor.ScriptOf(codepoint).Should().Be(script);
    }

    [Fact]
    public void An_unlisted_range_returns_no_script_rather_than_a_wrong_one()
    {
        // Under-reporting is the safe direction: the code-point requirement is
        // still resolved either way, whereas mislabelling a script sends the
        // reader after the wrong remedy.
        RequirementExtractor.ScriptOf(0x1D400).Should().BeNull();
    }

    // ── Summary ───────────────────────────────────────────────────────

    [Fact]
    public void The_summary_says_what_kind_of_trouble_to_expect()
    {
        var summary = RequirementExtractor.Summarise(
            From("""{"text":"中","source":"\\alpha"}"""));

        summary.Should().Contain("commands").And.Contain("characters").And.Contain("scripts");
    }

    [Fact]
    public void An_ascii_document_summarises_plainly()
    {
        RequirementExtractor.Summarise(From("""{"text":"plain"}"""))
            .Should().Be("nothing beyond plain ASCII");
    }
}
