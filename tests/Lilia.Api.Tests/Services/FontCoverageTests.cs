using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// P2.5 — font coverage from measured facts.
///
/// <para>The queries need the tooling database, so what is asserted here is the
/// part that decides what gets asked and what happens when the catalogue is
/// absent. Both are where a coverage feature turns harmful rather than merely
/// unhelpful: asking about the wrong characters, or reporting "nothing missing"
/// when the real answer is "I have no idea".</para>
/// </summary>
public class FontCoverageTests
{
    private static FontCoverageService Sut(string? connectionString = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(connectionString is null
                ? []
                : new Dictionary<string, string?> { ["ConnectionStrings:LatexFacts"] = connectionString })
            .Build();

        return new FontCoverageService(config, NullLogger<FontCoverageService>.Instance);
    }

    // ── What gets asked ───────────────────────────────────────────────

    [Fact]
    public void Ascii_is_not_worth_asking_about()
    {
        // Every font has it. Including it would turn every lookup into a scan
        // over characters that were never in question.
        FontCoverageService.InterestingCodePoints("Hello, world! (1975)").Should().BeEmpty();
    }

    [Fact]
    public void Non_ascii_characters_are_collected()
    {
        var points = FontCoverageService.InterestingCodePoints("Hello שלום");

        points.Should().NotBeEmpty();
        points.Should().OnlyContain(cp => cp >= 0x80);
        points.Should().Contain(0x05E9, "the Hebrew shin is exactly what needs checking");
    }

    [Fact]
    public void Repeats_are_asked_about_once()
    {
        // A paragraph of Chinese repeats characters constantly; the useful
        // question is about distinct code points.
        FontCoverageService.InterestingCodePoints("中中中文文").Should().HaveCount(2);
    }

    [Fact]
    public void Results_are_ordered_so_the_answer_is_stable()
    {
        FontCoverageService.InterestingCodePoints("文中é").Should().BeInAscendingOrder();
    }

    [Fact]
    public void An_astral_character_is_one_code_point_not_two_halves()
    {
        // Enumerated as runes rather than chars. An emoji is a surrogate pair,
        // and asking whether a font covers half of one is meaningless.
        var points = FontCoverageService.InterestingCodePoints("hi 😀");

        points.Should().ContainSingle();
        points[0].Should().Be(0x1F600);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_asks_nothing(string? text)
    {
        FontCoverageService.InterestingCodePoints(text).Should().BeEmpty();
    }

    // ── When the catalogue is absent ──────────────────────────────────

    [Fact]
    public void Unavailable_is_reported_rather_than_implied()
    {
        Sut().IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Configured_reports_available()
    {
        Sut("Host=localhost;Database=x").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task With_no_catalogue_nothing_is_claimed_about_coverage()
    {
        // The dangerous alternative is returning an empty "uncovered" list and
        // letting the caller read it as "this font is fine" — a confident wrong
        // answer about the exact failure this catalogue exists to prevent. The
        // list is empty, but IsAvailable is false, and the endpoint surfaces
        // both so the two cannot be confused.
        var sut = Sut();

        (await sut.UncoveredCodePointsAsync("Latin Modern Roman", "שלום")).Should().BeEmpty();
        sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task With_no_catalogue_no_fonts_are_suggested()
    {
        (await Sut().FontsCoveringAsync([0x05E9])).Should().BeEmpty();
    }

    [Fact]
    public async Task Asking_about_nothing_does_not_hit_the_database()
    {
        // A deliberately unroutable connection string: if this returned without
        // touching it, the short-circuit works. Otherwise the test would hang or
        // throw rather than pass.
        var sut = Sut("Host=203.0.113.1;Port=1;Database=nope;Timeout=1");

        (await sut.FontsCoveringAsync([])).Should().BeEmpty();
        (await sut.UncoveredCodePointsAsync("Any", "plain ascii only")).Should().BeEmpty();
    }

    // ── Portability ───────────────────────────────────────────────────

    [Theory]
    [InlineData("tex-tree", true)]
    [InlineData("system", false)]
    public void Only_tex_tree_fonts_travel_with_the_document(string provenance, bool portable)
    {
        // The portability trap: a system font renders perfectly here and breaks
        // the moment the .tex reaches a collaborator or Overleaf. The measured
        // corpus is 4,269 tex-tree against 931 system, so this is not a rare
        // edge — roughly one font in six is a trap.
        new FontOption("Some Family", provenance).IsPortable.Should().Be(portable);
    }
}
