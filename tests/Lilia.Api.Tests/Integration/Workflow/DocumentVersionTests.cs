using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration.Workflow;

/// <summary>
/// Content-addressed document versions (the user, 18 Oct: "Ideally versioning is immutable? Like
/// state management in the front end?"). A version is the hash of the blocks' hashes, which the
/// database keeps itself; the PDF and the check are cached by it, and the PDF's ETag is it.
/// </summary>
[Collection("Integration")]
public class DocumentVersionTests : IntegrationTestBase
{
    private readonly string _userId = $"versions-{Guid.NewGuid():N}"[..28];
    private HttpClient _client = null!;

    public DocumentVersionTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        _client = CreateClientAs(_userId);
    }

    private async Task<Guid> NewDocumentAsync(string title) =>
        (await (await _client.PostAsJsonAsync("/api/documents", new { title })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private async Task<Guid> AddBlockAsync(Guid doc, string type, object content, int sortOrder) =>
        (await (await _client.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type, content, sortOrder })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private async Task<string> VersionAsync(Guid doc)
    {
        await using var db = CreateDbContext();
        return (await new DocumentVersions(db).OfAsync(doc))!;
    }

    private async Task<string?> BlockHashAsync(Guid block)
    {
        await using var db = CreateDbContext();
        return await db.Blocks.Where(b => b.Id == block).Select(b => b.ContentHash).FirstAsync();
    }

    [Fact]
    public async Task The_database_keeps_each_block_s_hash_whatever_writes_it()
    {
        var doc = await NewDocumentAsync("Hashes");
        var block = await AddBlockAsync(doc, "paragraph", new { text = "First." }, 0);
        var first = await BlockHashAsync(block);
        first.Should().MatchRegex("^[0-9a-f]{64}$", "set on insert");

        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/{block}", new { type = "paragraph", content = new { text = "Second." } })).EnsureSuccessStatusCode();
        var second = await BlockHashAsync(block);
        second.Should().NotBe(first, "set on update");

        // Even a raw write that tries to set the hash itself gets the true one.
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE blocks SET content = '{{\"text\":\"First.\"}}'::jsonb, content_hash = 'forged' WHERE id = {0}", block);
        (await BlockHashAsync(block)).Should().Be(first, "the same content has the same hash, whoever writes it");
    }

    [Fact]
    public async Task The_version_changes_with_everything_in_the_pdf_and_nothing_else()
    {
        var doc = await NewDocumentAsync("Versions");
        var a = await AddBlockAsync(doc, "paragraph", new { text = "Alpha." }, 0);
        var b = await AddBlockAsync(doc, "paragraph", new { text = "Bravo." }, 1);
        var seen = new HashSet<string> { await VersionAsync(doc) };
        async Task Changed(string why) => seen.Add(await VersionAsync(doc)).Should().BeTrue(why);
        async Task Same(string why) => seen.Should().Contain(await VersionAsync(doc), why);

        // Opening it, or a compile writing its label numbers, changes nothing in the PDF.
        (await _client.GetAsync($"/api/documents/{doc}")).EnsureSuccessStatusCode();
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE documents SET last_opened_at = now(), \"LabelNumbers\" = '{{}}', \"LabelNumbersAt\" = now() WHERE id = {0}", doc);
        await Same("opening it or writing label numbers");

        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/{a}", new { type = "paragraph", content = new { text = "Alpha, edited." } })).EnsureSuccessStatusCode();
        await Changed("a block edited");
        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/reorder", new { blockIds = new[] { b, a } })).EnsureSuccessStatusCode();
        await Changed("blocks reordered");
        await AddBlockAsync(doc, "heading", new { text = "Results", level = 1 }, 2);
        await Changed("a block added");
        (await _client.DeleteAsync($"/api/documents/{doc}/blocks/{b}")).EnsureSuccessStatusCode();
        await Changed("a block deleted");
        (await _client.PutAsJsonAsync($"/api/documents/{doc}", new { paperSize = "letter" })).EnsureSuccessStatusCode();
        await Changed("a setting");
        (await _client.PutAsJsonAsync($"/api/documents/{doc}", new { title = "Versions, renamed" })).EnsureSuccessStatusCode();
        await Changed("the title");
        (await _client.PostAsJsonAsync($"/api/documents/{doc}/bibliography", new
        {
            citeKey = "knuth84", entryType = "book",
            data = JsonSerializer.Deserialize<JsonElement>("""{"title":"The TeXbook","author":"Knuth, Donald","year":"1984"}"""),
        })).EnsureSuccessStatusCode();
        await Changed("a bibliography entry");
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE blocks SET metadata = '{{\"span\":\"page\"}}'::jsonb WHERE id = {0}", a);
        await Changed("a block's metadata, written directly");
    }

    [Fact]
    public async Task An_unchanged_pdf_is_not_sent_again_and_an_edited_one_is()
    {
        var doc = await NewDocumentAsync("ETag");
        var para = await AddBlockAsync(doc, "paragraph", new { text = "Sent once." }, 0);

        var first = await _client.GetAsync($"/api/documents/{doc}/export/pdf?engine=pdflatex");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull();
        first.Headers.CacheControl!.Private.Should().BeTrue();

        var again = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{doc}/export/pdf?engine=pdflatex");
        again.Headers.IfNoneMatch.Add(etag!);
        (await _client.SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        var hits = CompileCache.Versions.Hits;
        (await _client.GetAsync($"/api/documents/{doc}/export/pdf?engine=pdflatex")).StatusCode.Should().Be(HttpStatusCode.OK);
        CompileCache.Versions.Hits.Should().BeGreaterThan(hits, "without the ETag, the PDF comes from the version cache");

        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/{para}", new { type = "paragraph", content = new { text = "Sent twice." } })).EnsureSuccessStatusCode();
        var edited = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{doc}/export/pdf?engine=pdflatex");
        edited.Headers.IfNoneMatch.Add(etag!);
        var response = await _client.SendAsync(edited);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "an edit is a new version");
        response.Headers.ETag.Should().NotBe(etag);
    }

    [Fact]
    public async Task The_check_of_an_unchanged_document_is_answered_by_version()
    {
        var doc = await NewDocumentAsync("Checked by version");
        var para = await AddBlockAsync(doc, "paragraph", new { text = @"Broken \liliaversionundefined{x}." }, 0);

        var first = await (await _client.PostAsync($"/api/latex/{doc}/validate", null)).Content.ReadFromJsonAsync<JsonElement>();
        first.GetProperty("valid").GetBoolean().Should().BeFalse();
        first.GetProperty("errorBlockId").GetString().Should().Be(para.ToString());

        var hits = CompileCache.Checks.Hits;
        var second = await (await _client.PostAsync($"/api/latex/{doc}/validate", null)).Content.ReadFromJsonAsync<JsonElement>();
        CompileCache.Checks.Hits.Should().BeGreaterThan(hits);
        second.GetProperty("valid").GetBoolean().Should().BeFalse();
        second.GetProperty("errorBlockId").GetString().Should().Be(para.ToString(), "the same answer, the same shape");

        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/{para}", new { type = "paragraph", content = new { text = "Fixed." } })).EnsureSuccessStatusCode();
        (await (await _client.PostAsync($"/api/latex/{doc}/validate", null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("valid").GetBoolean().Should().BeTrue();
    }
}
