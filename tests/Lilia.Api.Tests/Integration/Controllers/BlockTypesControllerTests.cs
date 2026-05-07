using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class BlockTypesControllerTests : IntegrationTestBase
{
    public BlockTypesControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetBlockTypes_ReturnsAllBlockTypes()
    {
        var response = await Client.GetAsync("/api/blocktypes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Count.Should().BeGreaterThanOrEqualTo(BlockTypes.All.Length);
    }

    [Fact]
    public async Task GetBlockTypes_WithQuery_FiltersResults()
    {
        var response = await Client.GetAsync("/api/blocktypes?query=head");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Should().Contain(bt => bt.Type.Contains("heading", StringComparison.OrdinalIgnoreCase));
        blockTypes.Count.Should().BeLessThan(BlockTypes.All.Length);
    }

    [Fact]
    public async Task GetBlockTypes_IsAccessibleAnonymously()
    {
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync("/api/blocktypes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetBlockType_ReturnsSpecificType()
    {
        var response = await Client.GetAsync("/api/blocktypes/paragraph");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockType = await response.Content.ReadFromJsonAsync<BlockTypeMetadataDto>();
        blockType.Should().NotBeNull();
        blockType!.Type.Should().Be("paragraph");
        blockType.Label.Should().NotBeNullOrEmpty();
        blockType.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetBlockType_ReturnsNotFound_WhenTypeDoesNotExist()
    {
        var response = await Client.GetAsync("/api/blocktypes/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("paragraph")]
    [InlineData("heading")]
    [InlineData("equation")]
    [InlineData("figure")]
    [InlineData("code")]
    [InlineData("list")]
    [InlineData("blockquote")]
    [InlineData("table")]
    [InlineData("theorem")]
    [InlineData("abstract")]
    [InlineData("bibliography")]
    [InlineData("tableOfContents")]
    [InlineData("pageBreak")]
    [InlineData("columnBreak")]
    [InlineData("footnote")]
    [InlineData("embed")]
    public async Task GetBlockType_ReturnsMetadata_ForEachCanonicalType(string type)
    {
        var response = await Client.GetAsync($"/api/blocktypes/{type}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockType = await response.Content.ReadFromJsonAsync<BlockTypeMetadataDto>();
        blockType.Should().NotBeNull();
        blockType!.Type.Should().Be(type);
        blockType.Label.Should().NotBeNullOrEmpty();
        blockType.Category.Should().Be("document");
    }

    [Fact]
    public async Task GetBlockTypes_WithCategoryDocument_ReturnsOnlyDocumentTypes()
    {
        var response = await Client.GetAsync("/api/blocktypes?category=document");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Should().OnlyContain(bt => bt.Category == "document");
        blockTypes.Should().Contain(bt => bt.Type == "paragraph");
        blockTypes.Should().NotContain(bt => bt.Type.StartsWith("inv-"));
    }

    [Fact]
    public async Task GetBlockTypes_WithCategoryInvoice_ReturnsOnlyInvoiceTypes()
    {
        var response = await Client.GetAsync("/api/blocktypes?category=invoice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Should().OnlyContain(bt => bt.Category == "invoice");
        blockTypes!.Count.Should().Be(9);
        blockTypes.Should().Contain(bt => bt.Type == "inv-header");
        blockTypes.Should().NotContain(bt => bt.Type == "paragraph");
    }

    [Fact]
    public async Task GetBlockTypes_WithQueryAndCategory_FiltersBoth()
    {
        var response = await Client.GetAsync("/api/blocktypes?query=tax&category=invoice");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Should().OnlyContain(bt => bt.Category == "invoice");
        blockTypes.Should().Contain(bt => bt.Type == "inv-tax-summary");
    }

    [Theory]
    [InlineData("inv-header")]
    [InlineData("inv-party")]
    [InlineData("inv-line-items")]
    [InlineData("inv-tax-summary")]
    [InlineData("inv-totals")]
    [InlineData("inv-payment")]
    [InlineData("inv-allowance-charge")]
    [InlineData("inv-delivery")]
    [InlineData("inv-note")]
    public async Task GetBlockType_ReturnsMetadata_ForEachInvoiceType(string type)
    {
        var response = await Client.GetAsync($"/api/blocktypes/{type}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockType = await response.Content.ReadFromJsonAsync<BlockTypeMetadataDto>();
        blockType.Should().NotBeNull();
        blockType!.Type.Should().Be(type);
        blockType.Label.Should().NotBeNullOrEmpty();
        blockType.Category.Should().Be("invoice");
    }

    // ── LILIA-121 D1 — class-aware filtering by docId ─────────────────────
    //
    // The catalog is filtered against Document.LatexDocumentClass when
    // ?docId=X is supplied. Article docs hide \chapter / \frontmatter /
    // \backmatter; book docs include them. The response shape changes from
    // bare list → envelope with AllowedSections + DocumentClass when docId
    // is supplied (existing clients that don't pass docId still see the
    // bare list).

    [Fact]
    public async Task GetBlockTypes_WithoutDocId_ReturnsBareList()
    {
        // No docId → existing behavior, unchanged. Backward-compat for
        // clients that haven't adopted the per-doc cache key yet.
        var response = await Client.GetAsync("/api/blocktypes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blockTypes = await response.Content.ReadFromJsonAsync<List<BlockTypeMetadataDto>>();
        blockTypes.Should().NotBeNull();
        blockTypes!.Count.Should().BeGreaterThanOrEqualTo(BlockTypes.All.Length);
    }

    [Fact]
    public async Task GetBlockTypes_WithArticleDocId_HidesChapterAndFrontMatter()
    {
        // Seed an article doc; assert the response excludes the book-only
        // sectioning blocks AND the AllowedSections list mirrors the seeded
        // class table. The two checks together pin the brief's contract:
        // article docs hide \chapter, \frontmatter, \mainmatter, \backmatter.
        await SeedUserAsync("class-test-user-article");
        var doc = await SeedDocumentWithClassAsync("class-test-user-article", "article");

        var client = CreateClientAs("class-test-user-article");
        var response = await client.GetAsync($"/api/blocktypes?docId={doc.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<BlockTypesEnvelopeForTest>();
        envelope.Should().NotBeNull();
        envelope!.DocumentClass.Should().Be("article");

        envelope.BlockTypes.Should().NotContain(bt => bt.Type == BlockTypes.FrontMatter);
        envelope.BlockTypes.Should().NotContain(bt => bt.Type == BlockTypes.BackMatter);
        envelope.BlockTypes.Should().NotContain(bt => bt.Type == BlockTypes.ChapterBreak);
        envelope.BlockTypes.Should().Contain(bt => bt.Type == BlockTypes.Paragraph);
        envelope.BlockTypes.Should().Contain(bt => bt.Type == BlockTypes.Heading);

        envelope.AllowedSections.Should().Contain("section");
        envelope.AllowedSections.Should().NotContain("chapter");
        envelope.AllowedSections.Should().NotContain("frontmatter");
        envelope.AllowedSections.Should().NotContain("mainmatter");
        envelope.AllowedSections.Should().NotContain("backmatter");
    }

    [Fact]
    public async Task GetBlockTypes_WithBookDocId_IncludesChapterAndFrontMatter()
    {
        await SeedUserAsync("class-test-user-book");
        var doc = await SeedDocumentWithClassAsync("class-test-user-book", "book");

        var client = CreateClientAs("class-test-user-book");
        var response = await client.GetAsync($"/api/blocktypes?docId={doc.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<BlockTypesEnvelopeForTest>();
        envelope.Should().NotBeNull();
        envelope!.DocumentClass.Should().Be("book");

        // Book class allows the full sectioning hierarchy plus front/back
        // matter. The catalog entries that map to those slugs come back in
        // the response.
        envelope.BlockTypes.Should().Contain(bt => bt.Type == BlockTypes.FrontMatter);
        envelope.BlockTypes.Should().Contain(bt => bt.Type == BlockTypes.BackMatter);
        envelope.BlockTypes.Should().Contain(bt => bt.Type == BlockTypes.ChapterBreak);

        envelope.AllowedSections.Should().Contain("chapter");
        envelope.AllowedSections.Should().Contain("frontmatter");
        envelope.AllowedSections.Should().Contain("mainmatter");
        envelope.AllowedSections.Should().Contain("backmatter");
        envelope.AllowedSections.Should().Contain("section");
    }

    [Fact]
    public async Task GetBlockTypes_WithUnknownDocId_FallsBackToUnfilteredEnvelope()
    {
        // Stale docId in the URL bar shouldn't make the menu vanish — we
        // still return a useful envelope, just with empty AllowedSections
        // and an unfiltered catalog.
        var unknownId = Guid.NewGuid();
        var response = await Client.GetAsync($"/api/blocktypes?docId={unknownId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<BlockTypesEnvelopeForTest>();
        envelope.Should().NotBeNull();
        envelope!.AllowedSections.Should().BeEmpty();
        envelope.DocumentClass.Should().BeNull();
        envelope.BlockTypes.Should().Contain(bt => bt.Type == BlockTypes.Paragraph);
    }

    private async Task<Document> SeedDocumentWithClassAsync(string ownerId, string latexClass)
    {
        var doc = await SeedDocumentAsync(ownerId, $"{latexClass} doc");
        await using var db = CreateDbContext();
        var dbDoc = await db.Documents.FindAsync(doc.Id);
        dbDoc!.LatexDocumentClass = latexClass;
        await db.SaveChangesAsync();
        return dbDoc;
    }

    /// <summary>
    /// Mirror of <c>BlockTypesEnvelope</c> in the API project — declared
    /// here so the test asm doesn't have to reference Lilia.Api directly.
    /// </summary>
    private sealed class BlockTypesEnvelopeForTest
    {
        public List<BlockTypeMetadataDto> BlockTypes { get; set; } = [];
        public List<string> AllowedSections { get; set; } = [];
        public string? DocumentClass { get; set; }
    }
}
