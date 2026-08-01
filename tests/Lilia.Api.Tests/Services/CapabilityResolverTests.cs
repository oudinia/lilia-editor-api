using FluentAssertions;
using Lilia.Api.Services.Capabilities;
using Lilia.Core.Capabilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// P3.3 phase 2 — fanning requirements across providers and merging answers.
///
/// <para>The resolver's job is not to be clever. It is to guarantee that
/// <b>every requirement asked about comes back</b>, whatever the providers do —
/// answer, stay silent, or throw. A requirement that quietly drops out of the
/// report is indistinguishable, to the caller, from one that was satisfied, and
/// that is the failure this whole plan exists to remove.</para>
///
/// <para>The catalogues are real tables; these use stub providers so the
/// merging rules are tested rather than the data. Provider-to-database
/// behaviour is exercised by the integration suite.</para>
/// </summary>
public class CapabilityResolverTests
{
    private sealed class StubProvider(
        string name,
        Func<Requirement, bool> handles,
        Func<Requirement, CapabilityVerdict?> answer,
        bool available = true,
        bool throws = false) : ICapabilityProvider
    {
        public string Name => name;
        public bool IsAvailable => available;
        public bool Handles(Requirement requirement) => handles(requirement);

        public Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
            IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
        {
            if (throws) throw new InvalidOperationException("catalogue exploded");

            IReadOnlyList<CapabilityVerdict> verdicts =
                [.. requirements.Select(answer).OfType<CapabilityVerdict>()];
            return Task.FromResult(verdicts);
        }
    }

    private static StubProvider Always(string name, Support support, bool available = true) =>
        new(name, _ => true, r => new CapabilityVerdict(r, support, name), available);

    private static CapabilityResolver Sut(params ICapabilityProvider[] providers) =>
        new(providers, NullLogger<CapabilityResolver>.Instance);

    private static readonly Requirement Command = new CommandRequirement("setmainfont");
    private static readonly Requirement Package = new PackageRequirement("fontspec");

    // ── Nothing disappears ────────────────────────────────────────────

    [Fact]
    public async Task Every_requirement_comes_back_even_with_no_providers()
    {
        // The worst case for silent loss: nobody to answer. The requirement
        // must still appear, carrying Unknown — never be absent, which a caller
        // reads as nothing to worry about.
        var report = await Sut().ResolveAsync([Command, Package], RenderTarget.Lualatex);

        report.Requirements.Should().HaveCount(2);
        report.Requirements.Should().OnlyContain(r => r.Support == Support.Unknown);
    }

    [Fact]
    public async Task A_requirement_no_provider_handles_still_appears()
    {
        var onlyCommands = new StubProvider(
            "commands", r => r is CommandRequirement, r => new CapabilityVerdict(r, Support.Full, "commands"));

        var report = await Sut(onlyCommands).ResolveAsync([Command, Package], RenderTarget.Lualatex);

        report.Requirements.Should().HaveCount(2);
        report.Requirements.Single(r => r.Requirement.Equals(Package)).Support.Should().Be(Support.Unknown);
    }

    [Fact]
    public async Task A_provider_that_handles_but_declines_to_answer_leaves_unknown()
    {
        // Handles() says yes, ResolveAsync returns nothing for it. Without the
        // aggregate seeded at Unknown this requirement would vanish.
        var silent = new StubProvider("silent", _ => true, _ => null);

        var report = await Sut(silent).ResolveAsync([Command], RenderTarget.Lualatex);

        report.Requirements.Should().ContainSingle()
            .Which.Support.Should().Be(Support.Unknown);
    }

    [Fact]
    public async Task Duplicates_collapse_to_one_answer()
    {
        // A document repeats the same character thousands of times; resolving
        // each occurrence would make the question expensive enough to avoid.
        var han = new CodepointRequirement(0x4E2D);
        var report = await Sut(Always("p", Support.Full))
            .ResolveAsync([han, han, han], RenderTarget.Lualatex);

        report.Requirements.Should().ContainSingle();
    }

    // ── Merging ───────────────────────────────────────────────────────

    [Fact]
    public async Task Disagreement_resolves_to_the_pessimistic_answer()
    {
        var report = await Sut(Always("optimist", Support.Full), Always("pessimist", Support.None))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        report.Requirements.Single().Support.Should().Be(Support.None);
    }

    [Fact]
    public async Task A_silent_provider_does_not_drag_a_real_answer_down()
    {
        // Unknown must not compete, or one unopinionated provider would flatten
        // every requirement to Unknown and the report would say nothing.
        var quiet = new StubProvider("quiet", _ => true, r => CapabilityVerdict.Unknown(r, "quiet"));

        var report = await Sut(quiet, Always("real", Support.Full))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        report.Requirements.Single().Support.Should().Be(Support.Full);
    }

    [Fact]
    public async Task Every_providers_answer_is_kept_for_tracing()
    {
        // The catalogues are hand-authored, so "which one claims this" is the
        // first question anyone asks about a surprising verdict.
        var report = await Sut(Always("a", Support.Full), Always("b", Support.Partial))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        report.Requirements.Single().Verdicts.Select(v => v.Source)
            .Should().BeEquivalentTo("a", "b");
    }

    // ── Providers that cannot answer ──────────────────────────────────

    [Fact]
    public async Task An_unavailable_provider_is_named_in_the_report()
    {
        // The font catalogue lives on a different database server, so this is a
        // normal operating condition rather than a hypothetical.
        var report = await Sut(Always("fonts", Support.Full, available: false))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        report.UnavailableProviders.Should().Contain("fonts");
    }

    [Fact]
    public async Task A_clean_report_from_a_degraded_resolver_is_not_fully_satisfied()
    {
        // The property callers branch on. "Nothing known to be wrong" is not
        // "nothing wrong", and conflating them here would undo the model at the
        // last step.
        var report = await Sut(
                Always("good", Support.Full),
                new StubProvider("fonts", _ => false, _ => null, available: false))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        report.Requirements.Single().Support.Should().Be(Support.Full);
        report.IsFullySatisfied.Should().BeFalse("a provider could not be consulted");
    }

    [Fact]
    public async Task A_provider_that_throws_does_not_take_the_report_down()
    {
        var report = await Sut(new StubProvider("broken", _ => true, _ => null, throws: true),
                               Always("working", Support.Full))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        report.Requirements.Should().ContainSingle();
        report.UnavailableProviders.Should().Contain("broken");
    }

    [Fact]
    public async Task A_thrown_provider_leaves_its_requirements_unknown_not_absent()
    {
        var report = await Sut(new StubProvider("broken", _ => true, _ => null, throws: true))
            .ResolveAsync([Command], RenderTarget.Lualatex);

        var resolved = report.Requirements.Single();
        resolved.Support.Should().Be(Support.Unknown);
        resolved.Verdicts.Should().ContainSingle().Which.Detail.Should().Contain("catalogue exploded");
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_provider_failure()
    {
        // A cancelled request is not a broken catalogue, and recording it as
        // one would make every navigation-away look like an outage.
        var cancelling = new StubProvider("slow", _ => true, _ => throw new OperationCanceledException());

        var act = () => Sut(cancelling).ResolveAsync([Command], RenderTarget.Lualatex);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── The report ────────────────────────────────────────────────────

    [Fact]
    public async Task Problems_are_ordered_worst_first()
    {
        var impossible = new CodepointRequirement(0x4E2D);
        var partial = new PackageRequirement("tikz");
        var fine = new CommandRequirement("textbf");

        var provider = new StubProvider("p", _ => true, r => new CapabilityVerdict(r, r switch
        {
            CodepointRequirement => Support.Impossible,
            PackageRequirement => Support.Partial,
            _ => Support.Full,
        }, "p"));

        var report = await Sut(provider).ResolveAsync([fine, partial, impossible], RenderTarget.Pdflatex);

        report.Problems.Select(p => p.Support)
            .Should().ContainInOrder(Support.Impossible, Support.Partial);
        report.Problems.Should().NotContain(p => p.Requirement.Equals(fine));
    }

    [Fact]
    public async Task Alternatives_from_every_provider_are_offered_once()
    {
        // The "and what else would work?" half of the question. Two providers
        // suggesting lualatex should not offer it twice.
        var a = new StubProvider("a", _ => true, r => new CapabilityVerdict(r, Support.None, "a", null, ["lualatex"]));
        var b = new StubProvider("b", _ => true, r => new CapabilityVerdict(r, Support.None, "b", null, ["lualatex", "xelatex"]));

        var report = await Sut(a, b).ResolveAsync([Command], RenderTarget.Pdflatex);

        report.Requirements.Single().Alternatives.Should().BeEquivalentTo("lualatex", "xelatex");
    }

    [Fact]
    public async Task An_empty_document_is_fully_satisfied()
    {
        var report = await Sut(Always("p", Support.Full)).ResolveAsync([], RenderTarget.Typst);

        report.IsFullySatisfied.Should().BeTrue();
        report.Problems.Should().BeEmpty();
    }

    [Fact]
    public async Task The_report_says_what_it_was_resolved_against()
    {
        var report = await Sut().ResolveAsync([Command], RenderTarget.Typst);

        report.Target.Should().Be(RenderTarget.Typst);
    }
}
