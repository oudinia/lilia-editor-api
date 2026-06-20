using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="InsertionsController"/> — the per-doc
/// insertion catalog that drives the editor's InsertionsPanel + ⌘K palette.
///
/// What we cover:
///   - Anonymous → 401
///   - Wrong owner → 404
///   - Missing doc → 404
///   - Empty package list → kernel-only tokens
///   - Installed package → kernel + that package's tokens
///   - Tokens with maps_to_block_type → hidden (block toolbar already covers them)
///   - Tokens with coverage_level=unsupported|none → hidden
///   - Telemetry-shape: insert_template surfaces correctly when set
/// </summary>
[Collection("Integration")]
public class InsertionsControllerTests : IntegrationTestBase
{
    public InsertionsControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    // Names of latex_tokens rows this test class seeded. latex_tokens is
    // catalog data (NOT wiped by the base CleanupTestDataAsync), so we must
    // remove our own fixtures — otherwise their null handler_kind leaks into
    // CatalogIntegrityTests when both classes share the collection's DB.
    private readonly List<string> _seededTokenNames = new();

    [Fact]
    public async Task GetInsertions_Anonymous_Returns401()
    {
        using var anon = CreateAnonymousClient();
        var docId = Guid.NewGuid();

        var response = await anon.GetAsync($"/api/lilia/insertions?docId={docId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInsertions_NonexistentDoc_Returns404()
    {
        const string userId = "user-insertions-1";
        await SeedUserAsync(userId);
        using var client = CreateClientAs(userId);

        var response = await client.GetAsync($"/api/lilia/insertions?docId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInsertions_WrongOwner_Returns404()
    {
        const string ownerId = "user-insertions-owner";
        const string strangerId = "user-insertions-stranger";
        await SeedUserAsync(ownerId);
        await SeedUserAsync(strangerId);
        var doc = await SeedDocumentAsync(ownerId);

        using var client = CreateClientAs(strangerId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInsertions_EmptyPackages_ReturnsKernelTokensOnly()
    {
        const string userId = "user-insertions-2";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);

        await SeedTokenAsync("kernelcmd", "command", packageSlug: null,
            coverageLevel: "full", insertTemplate: @"\kernelcmd{|CURSOR|}");
        await SeedTokenAsync("amspkg-cmd", "command", packageSlug: "amsmath",
            coverageLevel: "full", insertTemplate: @"\amscmd{|CURSOR|}");

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InsertionDto>>();
        rows.Should().NotBeNull();
        rows!.Should().Contain(r => r.Name == "kernelcmd");
        rows.Should().NotContain(r => r.Name == "amspkg-cmd");
    }

    [Fact]
    public async Task GetInsertions_WithInstalledPackage_IncludesPackageTokens()
    {
        const string userId = "user-insertions-3";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);

        // Install amsmath on this document.
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(doc.Id);
            d!.LatexPackages = """[{"name":"amsmath"}]""";
            await db.SaveChangesAsync();
        }

        await SeedTokenAsync("kernelcmd-3", "command", packageSlug: null,
            coverageLevel: "full", insertTemplate: @"\kernelcmd{|CURSOR|}");
        await SeedTokenAsync("amspkg-cmd-3", "command", packageSlug: "amsmath",
            coverageLevel: "full", insertTemplate: @"\amscmd{|CURSOR|}");
        await SeedTokenAsync("other-cmd-3", "command", packageSlug: "tikz",
            coverageLevel: "full", insertTemplate: @"\tikzcmd{|CURSOR|}");

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InsertionDto>>();
        rows!.Should().Contain(r => r.Name == "kernelcmd-3");
        rows.Should().Contain(r => r.Name == "amspkg-cmd-3");
        rows.Should().NotContain(r => r.Name == "other-cmd-3");
    }

    [Fact]
    public async Task GetInsertions_TokensMappedToBlockType_AreHidden()
    {
        const string userId = "user-insertions-4";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);

        await SeedTokenAsync("section-mapped", "command", packageSlug: null,
            coverageLevel: "full", insertTemplate: null, mapsToBlockType: "heading");
        await SeedTokenAsync("emph-unmapped", "command", packageSlug: null,
            coverageLevel: "full", insertTemplate: @"\emph{|SELECTION|}");

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        var rows = await response.Content.ReadFromJsonAsync<List<InsertionDto>>();
        rows!.Should().NotContain(r => r.Name == "section-mapped");
        rows.Should().Contain(r => r.Name == "emph-unmapped");
    }

    [Fact]
    public async Task GetInsertions_UnsupportedCoverage_AreHidden()
    {
        const string userId = "user-insertions-5";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);

        await SeedTokenAsync("good", "command", packageSlug: null, coverageLevel: "full");
        await SeedTokenAsync("partial", "command", packageSlug: null, coverageLevel: "partial");
        await SeedTokenAsync("shimmed", "command", packageSlug: null, coverageLevel: "shimmed");
        await SeedTokenAsync("unsupported", "command", packageSlug: null, coverageLevel: "unsupported");
        await SeedTokenAsync("none", "command", packageSlug: null, coverageLevel: "none");

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        var rows = await response.Content.ReadFromJsonAsync<List<InsertionDto>>();
        rows!.Select(r => r.Name).Should().Contain(new[] { "good", "partial", "shimmed" });
        rows.Should().NotContain(r => r.Name == "unsupported");
        rows.Should().NotContain(r => r.Name == "none");
    }

    [Fact]
    public async Task GetInsertions_InsertTemplate_RoundTrips()
    {
        const string userId = "user-insertions-6";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);

        await SeedTokenAsync("href-tok", "command", packageSlug: null,
            coverageLevel: "full", insertTemplate: @"\href{|CURSOR|}{|SELECTION|}");

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        var rows = await response.Content.ReadFromJsonAsync<List<InsertionDto>>();
        var hrefRow = rows!.Single(r => r.Name == "href-tok");
        hrefRow.InsertTemplate.Should().Be(@"\href{|CURSOR|}{|SELECTION|}");
    }

    [Fact]
    public async Task GetInsertions_MalformedPackagesJson_DegradesToKernelOnly()
    {
        const string userId = "user-insertions-7";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);
        // Bad JSON — controller must not 500; should fall back to "no packages installed".
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(doc.Id);
            d!.LatexPackages = "not-valid-json{[";
            await db.SaveChangesAsync();
        }

        await SeedTokenAsync("kerneltok-7", "command", packageSlug: null, coverageLevel: "full");
        await SeedTokenAsync("amspkg-tok-7", "command", packageSlug: "amsmath", coverageLevel: "full");

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync($"/api/lilia/insertions?docId={doc.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InsertionDto>>();
        rows!.Should().Contain(r => r.Name == "kerneltok-7");
        rows.Should().NotContain(r => r.Name == "amspkg-tok-7");
    }

    /// <summary>
    /// Seed a single <c>latex_tokens</c> row for the test. We use a unique
    /// name per test so the global cleanup in IntegrationTestBase isn't
    /// strictly needed (LatexTokens isn't in the cleanup list — it's
    /// catalog data — and we don't want to wipe seeded tokens between
    /// tests). The tokens we add here are uniquely-named test fixtures.
    /// </summary>
    private async Task SeedTokenAsync(
        string name,
        string kind,
        string? packageSlug,
        string coverageLevel,
        string? insertTemplate = null,
        string? mapsToBlockType = null)
    {
        await using var db = CreateDbContext();
        // Defensive: if a previous run left a row, replace it.
        var existing = db.LatexTokens.FirstOrDefault(t =>
            t.Name == name && t.Kind == kind && t.PackageSlug == packageSlug);
        if (existing != null) db.LatexTokens.Remove(existing);

        db.LatexTokens.Add(new LatexToken
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = kind,
            PackageSlug = packageSlug,
            CoverageLevel = coverageLevel,
            InsertTemplate = insertTemplate,
            MapsToBlockType = mapsToBlockType,
            ExpectsBody = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        _seededTokenNames.Add(name);
    }

    public override async Task DisposeAsync()
    {
        // Remove the token fixtures we added so they don't pollute other
        // catalog tests sharing this collection's database.
        if (_seededTokenNames.Count > 0)
        {
            await using var db = CreateDbContext();
            var rows = db.LatexTokens.Where(t => _seededTokenNames.Contains(t.Name));
            db.LatexTokens.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
        await base.DisposeAsync();
    }
}
