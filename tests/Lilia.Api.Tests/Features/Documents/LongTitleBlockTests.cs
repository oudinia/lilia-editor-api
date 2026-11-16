using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Documents;

/// <summary>
/// The document sweep (E-040, 15 Nov 2026): a Title block longer than 255 characters made saving it fail with a
/// database error (500), because its title is copied into the document's name, which holds 255. The block keeps the
/// whole title; the name is cut under the limit.
/// </summary>
public class LongTitleBlockTests : IntegrationTestBase
{
    private readonly string _userId = $"long-title-{Guid.NewGuid():N}"[..28];

    public LongTitleBlockTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    [Fact]
    public async Task A_title_longer_than_the_document_name_saves_and_keeps_its_full_text()
    {
        var client = CreateClientAs(_userId);
        var doc = (await (await client.PostAsJsonAsync("/api/documents", new { title = "Short" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var title = string.Join(" ", Enumerable.Repeat("A very long title word", 15)); // 344 characters

        var response = await client.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "title", content = new { title, author = "A", date = "" }, sortOrder = 0 });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var saved = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc);
        saved.Title.Length.Should().BeLessThanOrEqualTo(BlockService.DocumentNameMaxLength);
        saved.Title.Should().StartWith("A very long title word").And.EndWith("…");
        var block = await db.Blocks.AsNoTracking().FirstAsync(b => b.DocumentId == doc && b.Type == "title");
        block.Content.RootElement.GetProperty("title").GetString().Should().Be(title);
    }

    // The launch bot's #117 (16 Nov 2026): the editor saved the cut name back as the document's title, and the reverse
    // sync replaced the Title block's full title with it; the PDF printed the cut form.
    [Fact]
    public async Task Saving_the_cut_name_back_leaves_the_full_title_in_the_block()
    {
        var client = CreateClientAs(_userId);
        var doc = (await (await client.PostAsJsonAsync("/api/documents", new { title = "Short" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var title = string.Join(" ", Enumerable.Repeat("A very long title word", 15));
        (await client.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "title", content = new { title, author = "A", date = "" }, sortOrder = 0 })).EnsureSuccessStatusCode();

        string name;
        await using (var db = CreateDbContext()) name = (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc)).Title;
        (await client.PutAsJsonAsync($"/api/documents/{doc}", new { title = name })).EnsureSuccessStatusCode();

        await using var check = CreateDbContext();
        var block = await check.Blocks.AsNoTracking().FirstAsync(b => b.DocumentId == doc && b.Type == "title");
        block.Content.RootElement.GetProperty("title").GetString().Should().Be(title);
    }

    [Fact]
    public async Task A_long_rename_from_the_top_bar_saves_and_the_block_gets_the_full_title()
    {
        var client = CreateClientAs(_userId);
        var doc = (await (await client.PostAsJsonAsync("/api/documents", new { title = "Short" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "title", content = new { title = "Old", author = "A", date = "" }, sortOrder = 0 })).EnsureSuccessStatusCode();
        var title = string.Join(" ", Enumerable.Repeat("Another long title word", 14));

        var response = await client.PutAsJsonAsync($"/api/documents/{doc}", new { title });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc)).Title.Length.Should().BeLessThanOrEqualTo(BlockService.DocumentNameMaxLength);
        var block = await db.Blocks.AsNoTracking().FirstAsync(b => b.DocumentId == doc && b.Type == "title");
        block.Content.RootElement.GetProperty("title").GetString().Should().Be(title);
    }

    [Theory]
    [InlineData("Short title", "Short title")]
    [InlineData("  Padded  ", "Padded")]
    public void A_title_within_the_limit_is_kept_as_is(string title, string name)
        => BlockService.DocumentNameOf(title).Should().Be(name);
}
