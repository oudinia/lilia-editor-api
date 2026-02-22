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
}
