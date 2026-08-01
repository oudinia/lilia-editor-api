using FluentAssertions;
using Lilia.Api.Services.Capabilities;
using Lilia.Core.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The measured-facts provider for LaTeX commands.
///
/// <para>The verdicts come from <c>latex_facts.command_support</c> on a
/// separate database, so what is asserted here is the part that decides whether
/// the answers can be trusted at all: what happens when the catalogue is not
/// reachable, and whether "we could not prove it" is ever allowed to read as a
/// verdict.</para>
///
/// <para>That distinction carries the whole dataset. 65 of 374 commands failed
/// every probe, and the largest group among them is macros the document itself
/// defines — <c>\x</c> is the most-used unprovable command in the corpus and is
/// not a command at all. Reporting those as unsupported would flag <c>\x</c> on
/// every document that defines it, and a report that cries wolf is worth less
/// than no report.</para>
/// </summary>
public class CommandSupportProviderTests
{
    private static CommandSupportProvider Sut(string? connectionString = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(connectionString is null
                ? []
                : new Dictionary<string, string?> { ["ConnectionStrings:LatexFacts"] = connectionString })
            .Build();

        return new CommandSupportProvider(config, NullLogger<CommandSupportProvider>.Instance);
    }

    private static readonly IReadOnlyList<Requirement> Frac = [new CommandRequirement("frac")];

    // ── What it answers about ─────────────────────────────────────────

    [Fact]
    public void It_answers_about_commands()
    {
        Sut().Handles(new CommandRequirement("frac")).Should().BeTrue();
    }

    [Theory]
    [InlineData("package")]
    [InlineData("codepoint")]
    [InlineData("font")]
    public void It_stays_out_of_other_providers_questions(string kind)
    {
        Requirement requirement = kind switch
        {
            "package" => new PackageRequirement("amsmath"),
            "codepoint" => new CodepointRequirement(0x4E2D),
            _ => new FontRequirement("Libertinus Serif"),
        };

        Sut().Handles(requirement).Should().BeFalse();
    }

    // ── When the catalogue is absent ──────────────────────────────────

    [Fact]
    public void Unconfigured_reports_unavailable()
    {
        // A separate database server, so this is a normal operating condition:
        // unconfigured in development, unreachable in production while the rest
        // of the API works perfectly.
        Sut().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Configured_reports_available()
    {
        Sut("Host=localhost;Database=x").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task With_no_catalogue_nothing_is_claimed()
    {
        // The dangerous alternative is returning an empty list, which the
        // resolver would read as "this provider had no objection".
        var verdicts = await Sut().ResolveAsync(Frac, RenderTarget.Pdflatex);

        verdicts.Should().ContainSingle();
        verdicts[0].Support.Should().Be(Support.Unknown);
        verdicts[0].Support.IsSatisfied().Should().BeFalse();
    }

    [Fact]
    public async Task An_unreachable_catalogue_reports_unknown_rather_than_throwing()
    {
        // Deliberately unroutable. An advisory catalogue on another server must
        // not be able to fail an export.
        var sut = Sut("Host=203.0.113.1;Port=1;Database=nope;Timeout=1;Command Timeout=1");

        var verdicts = await sut.ResolveAsync(Frac, RenderTarget.Pdflatex);

        verdicts.Should().ContainSingle();
        verdicts[0].Support.Should().Be(Support.Unknown);
        verdicts[0].Detail.Should().NotBeNullOrEmpty("the reason has to reach whoever reads the report");
    }

    // ── Targets ───────────────────────────────────────────────────────

    [Fact]
    public async Task Typst_gets_no_verdict_from_a_LaTeX_measurement()
    {
        // Every fact in the table was established by compiling LaTeX. Claiming
        // it holds for Typst would be a confident answer to a question nothing
        // measured.
        var verdicts = await Sut("Host=localhost;Database=x").ResolveAsync(Frac, RenderTarget.Typst);

        verdicts.Should().ContainSingle();
        verdicts[0].Support.Should().Be(Support.Unknown);
        verdicts[0].Detail.Should().Contain("Typst");
    }

    [Theory]
    [InlineData(RenderTarget.Pdflatex)]
    [InlineData(RenderTarget.Xelatex)]
    [InlineData(RenderTarget.Lualatex)]
    public async Task The_LaTeX_engines_are_answered_identically(RenderTarget target)
    {
        // Not a shortcut — a measurement. All 374 commands were compiled under
        // all three engines with zero real disagreements, which is why the
        // table has no target column. This pins that: if a per-engine
        // distinction is ever introduced, it should break here first.
        var verdicts = await Sut().ResolveAsync(Frac, target);

        verdicts.Should().ContainSingle();
        verdicts[0].Support.Should().Be(Support.Unknown, "unconfigured, but uniformly so across LaTeX engines");
    }

    // ── Asking about nothing ──────────────────────────────────────────

    [Fact]
    public async Task Asking_about_no_commands_does_not_hit_the_database()
    {
        // Unroutable connection string: returning promptly proves the
        // short-circuit, since otherwise this would hang or throw.
        var sut = Sut("Host=203.0.113.1;Port=1;Database=nope;Timeout=1");

        (await sut.ResolveAsync([], RenderTarget.Pdflatex)).Should().BeEmpty();
    }

    [Fact]
    public async Task Requirements_it_does_not_handle_are_left_to_other_providers()
    {
        var sut = Sut("Host=203.0.113.1;Port=1;Database=nope;Timeout=1");

        var verdicts = await sut.ResolveAsync(
            [new PackageRequirement("amsmath"), new CodepointRequirement(0x4E2D)],
            RenderTarget.Pdflatex);

        verdicts.Should().BeEmpty();
    }
}
