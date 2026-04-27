using System.Reflection;
using FluentAssertions;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Phase 2 step 12d — single-source-of-truth integrity gate that
/// connects the three Typst test layers:
///
///   Tier 1 source-shape   — TypstExportFixtureTests
///   Tier 1 single compile — TypstFixtureCombinatorialTests
///   Tier 2 reflection     — TypstHandlerCoverageTests
///
/// Adding a new canonical block type to BlockTypes must come with
/// fixtures in each tier, otherwise this gate fails. Likewise, every
/// "expected fallback" claim in the combinatorial test must be a
/// real catalog entry — no silent grace periods.
///
/// This is the lightweight stand-in for a future <c>typst_tokens</c>
/// catalog table. Once Typst usage telemetry surfaces a top-N gap
/// list, this test is the right spot to assert: "every gap with &gt;
/// X events in the last 7 days has a tracking issue and a planned
/// fixture." For the launch we just enforce parity across the three
/// existing test layers.
/// </summary>
public class TypstCatalogIntegrityTests
{
    /// <summary>Block types the Typst exporter is responsible for.
    /// Mirrors TypstHandlerCoverageTests.RequiredCoverage — keep in sync.</summary>
    private static readonly HashSet<string> CanonicalBlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BlockTypes.Paragraph,
        BlockTypes.Heading,
        BlockTypes.Equation,
        BlockTypes.Figure,
        BlockTypes.Table,
        BlockTypes.Code,
        BlockTypes.List,
        BlockTypes.Blockquote,
        BlockTypes.Theorem,
        BlockTypes.Abstract,
        BlockTypes.Bibliography,
        BlockTypes.TableOfContents,
        BlockTypes.PageBreak,
    };

    [Fact]
    public void Every_canonical_block_type_has_at_least_one_source_shape_fixture()
    {
        var fixtures = LoadFixtures<TypstExportFixtureTests.Fx>(
            typeof(TypstExportFixtureTests),
            nameof(TypstExportFixtureTests.Fixtures))
            .Select(f => f.BlockType.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = CanonicalBlockTypes
            .Where(t => !fixtures.Contains(t))
            .ToList();

        missing.Should().BeEmpty(
            "every canonical block type must have a source-shape fixture in TypstExportFixtureTests — missing: {0}",
            string.Join(", ", missing));
    }

    [Fact]
    public void Every_canonical_block_type_has_at_least_one_compile_fixture()
    {
        var fixtures = LoadFixtures<TypstFixtureCombinatorialTests.Fx>(
            typeof(TypstFixtureCombinatorialTests),
            nameof(TypstFixtureCombinatorialTests.Fixtures))
            .Select(f => f.BlockType.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = CanonicalBlockTypes
            .Where(t => !fixtures.Contains(t))
            .ToList();

        missing.Should().BeEmpty(
            "every canonical block type must have a single-block compile fixture in TypstFixtureCombinatorialTests — missing: {0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// Expected-fallback fixtures (ExpectCompile=false) document a
    /// known LatexToTypstMath / Typst-feature gap. Each must name the
    /// gap in its fixture name (substring 'gap' or 'fallback') so the
    /// reason is greppable from the test output. Keeps the test honest
    /// about why a fixture is allowed to "fail" Typst compile.
    /// </summary>
    [Fact]
    public void Expected_fallback_fixtures_name_the_gap()
    {
        var fixtures = LoadFixtures<TypstFixtureCombinatorialTests.Fx>(
            typeof(TypstFixtureCombinatorialTests),
            nameof(TypstFixtureCombinatorialTests.Fixtures))
            .Where(f => !f.ExpectCompile)
            .ToList();

        fixtures.Should().NotBeEmpty(
            "at least one expected-fallback fixture must exist — the design doc's transparent fallback contract is non-trivial coverage");

        var unnamed = fixtures
            .Where(f =>
                !f.Name.Contains("gap", StringComparison.OrdinalIgnoreCase) &&
                !f.Name.Contains("fallback", StringComparison.OrdinalIgnoreCase) &&
                !f.Name.Contains("preview", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name)
            .ToList();

        unnamed.Should().BeEmpty(
            "every expected-fallback fixture must mention 'gap', 'fallback', or 'preview' in its name so the reason is visible — offenders: {0}",
            string.Join(" | ", unnamed));
    }

    /// <summary>
    /// Sanity gate — combinatorial fixture count must be at least
    /// twice the canonical block type count (one happy fixture per
    /// type plus edge cases / aliases). Acts as an alarm if someone
    /// accidentally truncates the fixture list.
    /// </summary>
    [Fact]
    public void Combinatorial_fixture_count_is_above_floor()
    {
        var count = LoadFixtures<TypstFixtureCombinatorialTests.Fx>(
            typeof(TypstFixtureCombinatorialTests),
            nameof(TypstFixtureCombinatorialTests.Fixtures))
            .Count();
        count.Should().BeGreaterThan(CanonicalBlockTypes.Count * 2,
            "expected fixture count > 2× canonical block types ({0}); got {1} — did someone truncate?",
            CanonicalBlockTypes.Count * 2, count);
    }

    private static IEnumerable<TFx> LoadFixtures<TFx>(Type host, string memberName)
    {
        var member = host.GetMethod(memberName,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{host.Name}.{memberName} not found");
        var rows = (IEnumerable<object[]>)member.Invoke(null, null)!;
        return rows.Select(r => (TFx)r[0]);
    }
}
