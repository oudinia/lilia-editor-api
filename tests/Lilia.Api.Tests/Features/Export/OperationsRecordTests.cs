using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// The record of what authors take away (10 Nov 2026, paying per operation): a download from the export screens counts
/// when it says it is one (download=true), the live preview's fetch of the same PDF route doesn't, the LaTeX route's
/// preview mode doesn't, and a file through the connector's link counts as the connector's.
/// </summary>
public class OperationsRecordTests : IntegrationTestBase
{
    public OperationsRecordTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<List<(string Format, string Source)>> ExportsOfAsync(string userId)
    {
        await using var db = CreateDbContext();
        return (await db.Operations.AsNoTracking().Where(o => o.UserId == userId).OrderBy(o => o.CreatedAt).ToListAsync())
            .Select(o => (o.Format, o.Source)).ToList();
    }

    private async Task<(HttpClient Client, Guid DocId)> AuthorWithDocumentAsync(string userId)
    {
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId, "Recorded");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"A sentence."}""");
        return (CreateClientAs(userId), doc.Id);
    }

    [Fact]
    public async Task A_download_counts_and_a_fetch_without_download_does_not()
    {
        var (client, docId) = await AuthorWithDocumentAsync("ops_screens");

        (await client.GetAsync($"/api/documents/{docId}/export/docx")).EnsureSuccessStatusCode();
        (await ExportsOfAsync("ops_screens")).Should().BeEmpty("a fetch that doesn't say it is a download isn't one");

        (await client.GetAsync($"/api/documents/{docId}/export/docx?download=true")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/documents/{docId}/export/markdown?download=true")).EnsureSuccessStatusCode();

        (await ExportsOfAsync("ops_screens")).Should().Equal(("docx", "screens"), ("markdown", "screens"));
    }

    [Fact]
    public async Task The_latex_route_counts_zip_and_tex_but_not_its_preview()
    {
        var (client, docId) = await AuthorWithDocumentAsync("ops_latex");

        (await client.GetAsync($"/api/documents/{docId}/export/latex?mode=preview&download=true")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/documents/{docId}/export/latex?mode=tex&download=true")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/documents/{docId}/export/latex?download=true")).EnsureSuccessStatusCode();

        (await ExportsOfAsync("ops_latex")).Should().Equal(("tex", "screens"), ("zip", "screens"));
    }

    [Fact]
    public async Task A_file_through_the_connector_s_link_counts_as_the_connector_s()
    {
        var (_, docId) = await AuthorWithDocumentAsync("ops_connector");
        var transfers = Fixture.Factory.Services.GetRequiredService<ConnectorTransfers>();
        var (token, _) = transfers.IssueExport("ops_connector", docId, "docx");

        using var anon = CreateAnonymousClient();
        (await anon.GetAsync($"/api/connector/files/{token}")).EnsureSuccessStatusCode();
        (await anon.GetAsync("/api/connector/files/not-a-real-token-at-all-not-a-real-token-xx")).IsSuccessStatusCode.Should().BeFalse();

        (await ExportsOfAsync("ops_connector")).Should().Equal(("docx", "connector"));
    }
}
