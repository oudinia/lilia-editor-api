using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Regression coverage for the public /api/public/latex-coverage/*
/// surface — the contract between the catalog service and the public
/// coverage page. Every test uses the anonymous client because these
/// endpoints are [AllowAnonymous].
///
/// Pair with CatalogFixtureTests (parser end-to-end proofs) and
/// CatalogIntegrityTests (catalog ↔ parser contract).
/// </summary>
[Collection("Integration")]
public class PublicCoverageControllerTests : IntegrationTestBase
{
    public PublicCoverageControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private HttpClient Anon() => CreateAnonymousClient();

    // ─── /summary ─────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_returns_counts_and_handler_kinds()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/summary");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("totalTokens").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("totalPackages").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("totalClasses").GetInt32().Should().BeGreaterThan(0);
        root.GetProperty("coveragePercent").GetDouble().Should().BeInRange(0, 100);

        root.GetProperty("tokens").GetProperty("full").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        root.GetProperty("packages").GetProperty("full").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        var categories = root.GetProperty("categories");
        categories.GetArrayLength().Should().BeGreaterThan(0);

        var handlerKinds = root.GetProperty("handlerKinds");
        handlerKinds.GetArrayLength().Should().BeGreaterThan(0,
            "summary should carry a non-empty handler-kind breakdown for the facet chip row");
        // Each entry must have kind + count.
        foreach (var hk in handlerKinds.EnumerateArray())
        {
            hk.GetProperty("kind").GetString().Should().NotBeNullOrEmpty();
            hk.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
        }
    }

    // ─── /packages ────────────────────────────────────────────────────

    [Fact]
    public async Task Packages_without_filter_returns_full_list()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/packages");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(10,
            "catalog ships dozens of packages");
    }

    [Fact]
    public async Task Packages_filtered_by_q_narrows_results()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/packages?q=ams");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var slugs = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("slug").GetString()!)
            .ToList();
        slugs.Should().NotBeEmpty();
        slugs.Should().OnlyContain(s => s.Contains("ams", StringComparison.OrdinalIgnoreCase),
            "ILIKE q filter should only return matching rows");
    }

    [Fact]
    public async Task PackageDetail_existing_slug_returns_tokens()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/packages/amsmath");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("slug").GetString().Should().Be("amsmath");
        doc.RootElement.GetProperty("tokens").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PackageDetail_unknown_slug_returns_404()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/packages/definitely-not-a-real-package");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── /tokens ──────────────────────────────────────────────────────

    [Fact]
    public async Task Tokens_search_by_name_finds_section_row()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/tokens?q=section");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThan(0,
            "'section' must match \\section / \\subsection / \\subsubsection / \\paragraph etc.");
        var names = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        names.Should().Contain("section");
    }

    [Fact]
    public async Task Tokens_filter_by_handler_kind_returns_only_matching_rows()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/tokens?handlerKind=section-regex");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var item in items.EnumerateArray())
        {
            item.GetProperty("handlerKind").GetString().Should().Be("section-regex");
        }
    }

    [Fact]
    public async Task Tokens_filter_by_kind_environment_excludes_commands()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/tokens?kind=environment&limit=200");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            item.GetProperty("kind").GetString().Should().Be("environment");
        }
    }

    [Fact]
    public async Task Tokens_filter_by_package_kernel_returns_only_null_package_rows()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/tokens?package=kernel&limit=10");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            // Default JSON serialiser writes null-valued properties
            // intermittently (ASP.NET config dependent). Either
            // shape is valid: key missing, or key present with null.
            if (item.TryGetProperty("packageSlug", out var pkg))
            {
                pkg.ValueKind.Should().Be(JsonValueKind.Null,
                    "'kernel' filter should produce rows with null package_slug");
            }
        }
    }

    [Fact]
    public async Task Tokens_pagination_respects_limit_and_offset()
    {
        using var c = Anon();
        var first = await c.GetAsync("/api/public/latex-coverage/tokens?limit=5&offset=0");
        var next = await c.GetAsync("/api/public/latex-coverage/tokens?limit=5&offset=5");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        next.StatusCode.Should().Be(HttpStatusCode.OK);

        using var d1 = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var d2 = JsonDocument.Parse(await next.Content.ReadAsStringAsync());

        d1.RootElement.GetProperty("limit").GetInt32().Should().Be(5);
        d1.RootElement.GetProperty("offset").GetInt32().Should().Be(0);
        d2.RootElement.GetProperty("offset").GetInt32().Should().Be(5);

        var firstNames = d1.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        var secondNames = d2.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        firstNames.Should().NotIntersectWith(secondNames,
            "offset=5 must yield a disjoint page from offset=0");
    }

    [Fact]
    public async Task Tokens_limit_clamps_at_200()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/tokens?limit=99999");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("limit").GetInt32().Should().BeLessThanOrEqualTo(200,
            "controller clamps oversize limits to 200");
    }

    // ─── /classes ─────────────────────────────────────────────────────

    [Fact]
    public async Task Classes_returns_core_document_classes()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/classes");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var slugs = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("slug").GetString()!)
            .ToList();
        slugs.Should().Contain(new[] { "article", "book", "beamer" });
    }

    [Fact]
    public async Task ClassDetail_article_returns_article_sectioning()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/classes/article");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var names = doc.RootElement.GetProperty("sectioning").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        names.Should().Contain(new[] { "section", "subsection", "subsubsection" });
        names.Should().NotContain("chapter",
            "article class has no \\chapter");
    }

    [Fact]
    public async Task ClassDetail_book_adds_chapter_and_part()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/classes/book");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var names = doc.RootElement.GetProperty("sectioning").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        names.Should().Contain(new[] { "part", "chapter", "section" });
    }

    [Fact]
    public async Task ClassDetail_moderncv_returns_class_specific_tokens()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/classes/moderncv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        // CV classes have no standard sectioning commands
        doc.RootElement.GetProperty("sectioning").GetArrayLength().Should().Be(0);

        var tokenNames = doc.RootElement.GetProperty("classSpecificTokens").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        tokenNames.Should().Contain("cventry",
            "moderncv class ships \\cventry as a class-specific command");
    }

    [Fact]
    public async Task ClassDetail_letter_has_no_sectioning()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/classes/letter");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("sectioning").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ClassDetail_unknown_slug_returns_404()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/classes/no-such-class-slug");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── /implementation-status ───────────────────────────────────────

    [Fact]
    public async Task ImplementationStatus_returns_all_five_stages()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/implementation-status");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var ids = doc.RootElement.GetProperty("stages").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .ToList();
        ids.Should().BeEquivalentTo(new[]
        {
            "truthful-catalog",
            "ci-contract",
            "catalog-driven-dispatch",
            "end-to-end-fixtures",
            "typst-oracle",
        });

        foreach (var s in doc.RootElement.GetProperty("stages").EnumerateArray())
        {
            var status = s.GetProperty("status").GetString();
            status.Should().BeOneOf("shipped", "in_progress", "planned");
            s.GetProperty("progressPercent").GetInt32().Should().BeInRange(0, 100);
        }
    }

    [Fact]
    public async Task ImplementationStatus_tests_tile_is_consistent()
    {
        using var c = Anon();
        var res = await c.GetAsync("/api/public/latex-coverage/implementation-status");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var tests = doc.RootElement.GetProperty("tests");
        tests.GetProperty("ciAssertions").GetInt32().Should().BeGreaterThan(0);
        tests.GetProperty("perHandlerFixtures").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        tests.GetProperty("perHandlerFixturesTarget").GetInt32()
            .Should().BeGreaterThanOrEqualTo(tests.GetProperty("perHandlerFixtures").GetInt32(),
                "actual should never exceed target");
    }

    [Fact]
    public async Task ImplementationStatus_snapshot_counts_are_consistent_with_summary()
    {
        using var c = Anon();
        var sumRes = await c.GetAsync("/api/public/latex-coverage/summary");
        var statusRes = await c.GetAsync("/api/public/latex-coverage/implementation-status");
        sumRes.StatusCode.Should().Be(HttpStatusCode.OK);
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var summary = JsonDocument.Parse(await sumRes.Content.ReadAsStringAsync());
        using var status = JsonDocument.Parse(await statusRes.Content.ReadAsStringAsync());

        var summaryFull = summary.RootElement.GetProperty("tokens").GetProperty("full").GetInt32();
        var snapshotFull = status.RootElement.GetProperty("catalogSnapshot").GetProperty("full").GetInt32();
        snapshotFull.Should().Be(summaryFull,
            "both endpoints pull coverage counts from the same table and must agree");
    }
}
