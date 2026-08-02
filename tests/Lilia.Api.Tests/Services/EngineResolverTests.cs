using FluentAssertions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Resolving the engine from the catalog on top of the regex floor.
///
/// <para>Detection was eleven hard-coded command names, so a package nobody had
/// added to the list was compiled by an engine that could not read it. Recording
/// the requirement makes adding one a row rather than a deploy — and the point is
/// not correctness (the tools runner retries on a mismatch either way) but not
/// spending the doomed compile at all.</para>
/// </summary>
public class EngineResolverTests
{
    private sealed class FakeRequirements : IEngineRequirementSource
    {
        private readonly Dictionary<string, LatexEngine> _map;
        public FakeRequirements(params (string slug, LatexEngine engine)[] entries) =>
            _map = entries.ToDictionary(e => e.slug, e => e.engine, StringComparer.OrdinalIgnoreCase);
        public LatexEngine? RequiredEngine(string slug) =>
            _map.TryGetValue(slug, out var e) ? e : null;
    }

    private static EngineResolver WithCatalog(params (string, LatexEngine)[] entries) =>
        new(new FakeRequirements(entries));

    private static EngineResolver WithoutCatalog() => new(new RegexOnlyEngineRequirements());

    [Fact]
    public void A_package_the_regex_never_heard_of_still_escalates()
    {
        // The whole point: `tikz-feynman` means nothing to EngineDetector.
        var resolver = WithCatalog(("tikz-feynman", LatexEngine.Lualatex));

        resolver.Resolve(@"\usepackage{tikz-feynman}").Should().Be(LatexEngine.Lualatex);
        WithoutCatalog().Resolve(@"\usepackage{tikz-feynman}").Should().Be(LatexEngine.Pdflatex);
    }

    [Fact]
    public void It_reads_every_slug_in_a_package_list()
    {
        var resolver = WithCatalog(("tikz-feynman", LatexEngine.Lualatex));

        resolver.Resolve(@"\usepackage{amsmath,tikz-feynman,booktabs}").Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void Options_do_not_hide_the_package()
    {
        var resolver = WithCatalog(("tikz-feynman", LatexEngine.Lualatex));

        resolver.Resolve(@"\usepackage[compat=1.1]{tikz-feynman}").Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void The_regex_floor_still_applies_with_an_empty_catalog()
    {
        // Losing the catalog must not lose what we already knew.
        WithoutCatalog().Resolve(@"\setmainfont{Charter}").Should().Be(LatexEngine.Lualatex);
        WithoutCatalog().Resolve(@"\directlua{print(1)}").Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void A_pdflatex_package_never_pulls_a_document_back_down()
    {
        // booktabs runs anywhere; that must not undo fontspec's requirement.
        var resolver = WithCatalog(("booktabs", LatexEngine.Pdflatex));

        resolver.Resolve(@"\usepackage{fontspec}\usepackage{booktabs}")
            .Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void Ordinary_content_stays_on_the_cheapest_engine()
    {
        var resolver = WithCatalog(("tikz-feynman", LatexEngine.Lualatex));

        resolver.Resolve(@"\begin{tabular}{lr}A & 1\\\end{tabular}").Should().Be(LatexEngine.Pdflatex);
        resolver.Resolve(@"\usepackage{amsmath,booktabs}").Should().Be(LatexEngine.Pdflatex);
    }

    [Fact]
    public void Empty_input_is_pdflatex()
    {
        WithoutCatalog().Resolve(null).Should().Be(LatexEngine.Pdflatex);
        WithoutCatalog().Resolve("").Should().Be(LatexEngine.Pdflatex);
    }
}
