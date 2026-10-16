using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// A cached preview follows the document (16 Oct): the full HTML preview kept a block's old text
/// after an edit, and every preview kept the old title after a rename, for up to an hour.
/// </summary>
[Collection("Integration")]
public class PreviewCacheFreshnessTests : IntegrationTestBase
{
    public PreviewCacheFreshnessTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<string> ContentAsync(string url) =>
        (await Client.GetFromJsonAsync<JsonElement>(url)).GetProperty("content").GetString()!;

    [Fact]
    public async Task The_full_html_preview_shows_a_block_edit_and_the_latex_preview_shows_a_rename()
    {
        await SeedUserAsync("test_user_001");
        var doc = await (await Client.PostAsJsonAsync("/api/documents", new { title = "Old Title" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = doc.GetProperty("id").GetGuid();
        var block = await (await Client.PostAsJsonAsync($"/api/documents/{id}/blocks", new { type = "paragraph", content = new { text = "First text." }, sortOrder = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        (await ContentAsync($"/api/documents/{id}/preview/html/full")).Should().Contain("First text.");
        (await ContentAsync($"/api/documents/{id}/preview/latex")).Should().Contain("Old Title");

        (await Client.PutAsJsonAsync($"/api/documents/{id}/blocks/{block.GetProperty("id").GetGuid()}", new { type = "paragraph", content = new { text = "Second text." } }))
            .EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/documents/{id}", new { title = "New Title" })).EnsureSuccessStatusCode();

        (await ContentAsync($"/api/documents/{id}/preview/html/full")).Should().Contain("Second text.");
        (await ContentAsync($"/api/documents/{id}/preview/latex")).Should().Contain("New Title");
    }
}
