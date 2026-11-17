using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Engines;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The connector test (17 Nov 2026): an assistant had to type a reference's BibTeX itself, details and all.
/// add_reference looks it up (here a stand-in for Crossref and arXiv) and writes it in the bibliography block.
/// </summary>
public class McpAddReferenceTests : IntegrationTestBase, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public McpAddReferenceTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IBibliographyService>();
            s.AddScoped<IBibliographyService, FakeLookups>();
        }));
    }

    public void Dispose() => _factory.Dispose();

    private sealed class FakeLookups : IBibliographyService
    {
        private static DoiLookupResultDto Work(string key, string type, object data) =>
            new(key, type, JsonSerializer.SerializeToElement(data));

        public Task<DoiLookupResultDto?> LookupDoiAsync(string doi) => Task.FromResult<DoiLookupResultDto?>(doi switch
        {
            "10.1145/321879.321884" => Work("tarjan1975", "article", new { title = "Efficiency of a Good But Not Linear Set Union Algorithm", author = "Robert E. Tarjan", journal = "J. ACM", year = "1975", doi }),
            "10.1000/other" => Work("tarjan1975", "article", new { title = "Another paper", author = "Robert E. Tarjan", journal = "SIAM J. Comput.", year = "1975", doi }),
            _ => null,
        });
        public Task<DoiLookupResultDto?> LookupArxivAsync(string arxivId) => Task.FromResult<DoiLookupResultDto?>(
            arxivId == "2106.09685" ? Work("hu2021", "article", new { title = "LoRA", author = "Edward J. Hu", year = "2021" }) : null);
        public Task<DoiLookupResultDto?> LookupIsbnAsync(string isbn) => Task.FromResult<DoiLookupResultDto?>(null);
        public Task<List<DoiLookupResultDto>> SearchByTitleAsync(string query, int maxResults = 10) => Task.FromResult(new List<DoiLookupResultDto>());
        public Task<List<BibliographyEntryDto>> GetEntriesAsync(Guid documentId) => Task.FromResult(new List<BibliographyEntryDto>());
        public Task<BibliographyEntryDto?> GetEntryAsync(Guid documentId, Guid entryId) => Task.FromResult<BibliographyEntryDto?>(null);
        public Task<BibliographyEntryDto> CreateEntryAsync(Guid documentId, CreateBibliographyEntryDto dto) => throw new NotSupportedException();
        public Task<BibliographyEntryDto?> UpdateEntryAsync(Guid documentId, Guid entryId, UpdateBibliographyEntryDto dto) => throw new NotSupportedException();
        public Task<bool> DeleteEntryAsync(Guid documentId, Guid entryId) => throw new NotSupportedException();
        public Task<List<BibliographyEntryDto>> ImportBibTexAsync(Guid documentId, string bibTexContent, bool overwrite = true) => throw new NotSupportedException();
        public Task<string> ExportBibTexAsync(Guid documentId) => Task.FromResult("");
    }

    private HttpClient App(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        c.DefaultRequestHeaders.Add("X-Test-Scope", "openid documents:read documents:write");
        return c;
    }

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var text = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private async Task<string> BibliographyTextAsync(Guid docId)
    {
        await using var db = CreateDbContext();
        var b = await db.Blocks.SingleAsync(x => x.DocumentId == docId && x.Type == "bibliography");
        return b.Content.RootElement.GetProperty("text").GetString()!;
    }

    [Fact]
    public async Task A_DOI_becomes_an_entry_in_a_new_bibliography_block_with_a_key_to_cite()
    {
        await SeedUserAsync("mcpref_owner");
        var doc = await SeedDocumentAsync("mcpref_owner", "Union-find");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Union-find is classic."}""", 0);
        var app = App("mcpref_owner");

        var r = await CallAsync(app, "add_reference", new { documentId = doc.Id.ToString(), doi = "https://doi.org/10.1145/321879.321884" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r.GetProperty("citeKey").GetString().Should().Be("tarjan1975");
        r.GetProperty("cite").GetString().Should().Be("@cite{tarjan1975}");
        (await BibliographyTextAsync(doc.Id)).Should().Contain("@article{tarjan1975,").And.Contain("J. ACM").And.Contain("10.1145/321879.321884");

        // A second work with the same suggested key gets its own.
        var r2 = await CallAsync(app, "add_reference", new { documentId = doc.Id.ToString(), doi = "10.1000/other" });
        r2.GetProperty("citeKey").GetString().Should().Be("tarjan1975a");
        // The same DOI again is not added twice.
        var r3 = await CallAsync(app, "add_reference", new { documentId = doc.Id.ToString(), doi = "doi:10.1145/321879.321884" });
        r3.GetProperty("citeKey").GetString().Should().Be("tarjan1975");
        r3.GetProperty("alreadyThere").GetBoolean().Should().BeTrue();
        var text = await BibliographyTextAsync(doc.Id);
        System.Text.RegularExpressions.Regex.Matches(text, "@article").Should().HaveCount(2);

        // An arXiv id, with its eprint kept.
        var r4 = await CallAsync(app, "add_reference", new { documentId = doc.Id.ToString(), arxiv = "arXiv:2106.09685" });
        r4.GetProperty("citeKey").GetString().Should().Be("hu2021");
        (await BibliographyTextAsync(doc.Id)).Should().Contain("eprint = {2106.09685}");
    }

    [Fact]
    public async Task An_unknown_DOI_or_two_ids_at_once_are_refused_and_a_viewer_cannot_add()
    {
        await SeedUserAsync("mcpref_owner2");
        await SeedUserAsync("mcpref_viewer");
        var doc = await SeedDocumentAsync("mcpref_owner2", "Refs");
        var app = App("mcpref_owner2");

        (await CallAsync(app, "add_reference", new { documentId = doc.Id.ToString(), doi = "10.9999/none" })).GetProperty("category").GetString().Should().Be("not-found");
        (await CallAsync(app, "add_reference", new { documentId = doc.Id.ToString(), doi = "10.1000/other", arxiv = "2106.09685" })).GetProperty("category").GetString().Should().Be("invalid");
        (await CallAsync(App("mcpref_viewer"), "add_reference", new { documentId = doc.Id.ToString(), doi = "10.1000/other" })).TryGetProperty("error", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("knuth1997", new string[0], "knuth1997")]
    [InlineData("knuth1997", new[] { "knuth1997" }, "knuth1997a")]
    [InlineData("knuth 1997!", new[] { "knuth1997", "knuth1997a" }, "knuth1997b")]
    public void A_key_the_document_already_uses_gets_a_letter(string key, string[] taken, string expected) =>
        LiliaMcpWriteTools.UniqueKey(key, taken.ToHashSet(StringComparer.OrdinalIgnoreCase)).Should().Be(expected);
}
