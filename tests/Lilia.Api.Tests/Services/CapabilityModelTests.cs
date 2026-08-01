using FluentAssertions;
using Lilia.Core.Capabilities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// P3.3 phase 1 — the capability model.
///
/// <para>No providers yet and nothing wired up. What is asserted here is the
/// part that decides whether the rest can be trusted: that "I could not tell"
/// is representable, survives being merged with real answers, and cannot be
/// mistaken for "fine".</para>
///
/// <para>Every failure this plan addresses has one shape — the system produced
/// plausible output, knew something had gone wrong, and told nobody. A missing
/// verdict that reads as satisfied is that failure at the type level, so it is
/// the thing worth pinning before any provider exists.</para>
/// </summary>
public class CapabilityModelTests
{
    // ── Unknown must not read as fine ─────────────────────────────────

    [Fact]
    public void An_unpopulated_verdict_reports_ignorance_not_support()
    {
        // Support.Unknown is 0 deliberately, so a struct nobody filled in fails
        // in the safe direction rather than claiming full support.
        default(Support).Should().Be(Support.Unknown);
    }

    [Fact]
    public void Unknown_is_not_satisfied()
    {
        // The single most important line in the model. Reading "I could not
        // tell" as "yes" is the silent-success failure, and it is the reading a
        // caller falls into unless the type refuses it.
        Support.Unknown.IsSatisfied().Should().BeFalse();
    }

    [Fact]
    public void Unknown_is_worth_telling_somebody_about()
    {
        Support.Unknown.NeedsReporting().Should().BeTrue();
    }

    [Theory]
    [InlineData(Support.Full, true)]
    [InlineData(Support.Shimmed, true)]
    [InlineData(Support.Partial, false)]
    [InlineData(Support.None, false)]
    [InlineData(Support.Impossible, false)]
    [InlineData(Support.Unknown, false)]
    public void Only_a_working_render_counts_as_satisfied(Support support, bool satisfied)
    {
        support.IsSatisfied().Should().Be(satisfied);
    }

    // ── Merging disagreement ──────────────────────────────────────────

    [Fact]
    public void The_pessimistic_verdict_wins()
    {
        // Overstating support produces a document that fails to compile, or
        // renders with characters silently dropped. Understating it produces a
        // warning about something that turns out fine. Only one of those is
        // recoverable by the person reading it.
        Support.Full.WorseOf(Support.None).Should().Be(Support.None);
        Support.Shimmed.WorseOf(Support.Impossible).Should().Be(Support.Impossible);
    }

    [Fact]
    public void Merging_does_not_depend_on_the_order_providers_answered_in()
    {
        // Providers are resolved concurrently; a verdict that changed with
        // arrival order would be unreproducible in exactly the cases anyone
        // cared to investigate.
        foreach (var a in Enum.GetValues<Support>())
        {
            foreach (var b in Enum.GetValues<Support>())
            {
                a.WorseOf(b).Should().Be(b.WorseOf(a), $"{a} and {b} must merge the same way either way round");
            }
        }
    }

    [Fact]
    public void A_real_answer_beats_no_answer()
    {
        // Unknown does not compete. Otherwise one unreachable provider would
        // drag every requirement to Unknown and the whole report would say
        // nothing — which is the failure mode of being too careful.
        Support.Unknown.WorseOf(Support.Full).Should().Be(Support.Full);
        Support.Full.WorseOf(Support.Unknown).Should().Be(Support.Full);
    }

    [Fact]
    public void Unknown_survives_only_when_nothing_answered()
    {
        Support.Unknown.WorseOf(Support.Unknown).Should().Be(Support.Unknown);
    }

    [Fact]
    public void Impossible_outranks_none_because_the_remedies_differ()
    {
        // CJK under pdflatex is Impossible — no setup changes it. A missing
        // package is None — install it and the answer changes. Collapsing them
        // would send someone to install their way out of an engine limitation.
        Support.None.WorseOf(Support.Impossible).Should().Be(Support.Impossible);
    }

    // ── Requirement identity ──────────────────────────────────────────

    [Fact]
    public void The_same_command_written_two_ways_is_one_requirement()
    {
        // Command names arrive from parsed source, catalogue rows and
        // hand-written provider tables, which do not agree about the leading
        // backslash. Two spellings would resolve separately, and one of them
        // would match no provider at all.
        new CommandRequirement("setmainfont").Key
            .Should().Be(new CommandRequirement("\\setmainfont").Key);
    }

    [Fact]
    public void Requirements_of_different_kinds_never_collide()
    {
        var keys = new Requirement[]
        {
            new CommandRequirement("x"),
            new PackageRequirement("x"),
            new DocumentClassRequirement("x"),
            new ScriptRequirement("x"),
            new FontRequirement("x"),
            new CodepointRequirement('x'),
        }.Select(r => r.Key).ToList();

        keys.Should().OnlyHaveUniqueItems("a package named x is not the command \\x");
    }

    [Fact]
    public void An_astral_character_is_one_requirement_not_two_halves()
    {
        // Same distinction P2.5 had to make for font coverage: asking whether a
        // target supports half a surrogate pair is meaningless.
        var emoji = new CodepointRequirement(0x1F600);

        emoji.Key.Should().Be("codepoint:U+1F600");
        emoji.Describe().Should().Contain("😀");
    }

    [Fact]
    public void Value_equality_lets_duplicates_collapse()
    {
        // A document repeats the same character constantly; resolving it once
        // per occurrence would make a document-sized question expensive enough
        // that callers avoid asking it.
        var set = new HashSet<Requirement>
        {
            new CodepointRequirement(0x4E2D),
            new CodepointRequirement(0x4E2D),
            new ScriptRequirement("Han"),
        };

        set.Should().HaveCount(2);
    }

    // ── Targets ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(RenderTarget.Pdflatex, "pdflatex")]
    [InlineData(RenderTarget.Lualatex, "lualatex")]
    [InlineData(RenderTarget.Typst, "typst")]
    public void Targets_use_the_vocabulary_callers_already_see(RenderTarget target, string wire)
    {
        // Matches the engine names in LaTeXRenderService, the X-Render-Engine
        // header and the ?engine= parameter, so a verdict can be reported
        // against a name the caller recognises.
        target.ToWireName().Should().Be(wire);
    }

    [Fact]
    public void Every_target_round_trips()
    {
        foreach (var target in RenderTargets.All)
        {
            RenderTargets.TryParse(target.ToWireName(), out var parsed).Should().BeTrue();
            parsed.Should().Be(target);
        }
    }

    [Theory]
    [InlineData("PDFLaTeX")]
    [InlineData("  typst  ")]
    public void Parsing_tolerates_case_and_spacing(string name)
    {
        RenderTargets.TryParse(name, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tectonic")]
    public void An_unrecognised_engine_is_refused_rather_than_guessed(string? name)
    {
        // Silently defaulting would land on pdflatex, the target least able to
        // render the things people ask about — so the wrong guess would produce
        // exactly the false alarms that make a report ignorable.
        RenderTargets.TryParse(name, out _).Should().BeFalse();
    }

    // ── Verdicts ──────────────────────────────────────────────────────

    [Fact]
    public void A_verdict_says_who_said_so()
    {
        // The catalogues are hand-authored, so "which table claims this" is the
        // first question anyone asks about a surprising answer.
        var verdict = new CapabilityVerdict(
            new PackageRequirement("fontspec"), Support.None, "packages", "not available under pdflatex");

        verdict.Source.Should().Be("packages");
        verdict.Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Alternatives_default_to_empty_rather_than_null()
    {
        // Every caller would otherwise need a null check before answering the
        // "what else would work?" half of the question, and one of them would
        // forget.
        new CapabilityVerdict(new FontRequirement("Arial"), Support.None, "fonts")
            .Alternatives.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void The_unknown_helper_produces_an_unsatisfied_verdict()
    {
        var verdict = CapabilityVerdict.Unknown(new ScriptRequirement("Han"), "fonts", "catalogue unreachable");

        verdict.Support.Should().Be(Support.Unknown);
        verdict.Support.IsSatisfied().Should().BeFalse();
        verdict.Detail.Should().Be("catalogue unreachable");
    }
}
