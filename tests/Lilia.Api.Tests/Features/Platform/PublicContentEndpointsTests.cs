using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Platform;

/// <summary>
/// What anyone may read without signing in (3 Nov 2026; no test called these): help articles, the
/// knowledge base, the changelog, lorem ipsum. Each shows its own content to a stranger and nothing
/// else: help serves help articles only, never a person's document by its id, and not as a PDF either.
/// </summary>
public class PublicContentEndpointsTests : IntegrationTestBase
{
    public PublicContentEndpointsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Document> SeedHelpAsync(string slug, string category, string title)
    {
        await using var db = CreateDbContext();
        if (!await db.Users.AnyAsync(u => u.Id == "help-author")) await SeedUserAsync("help-author");
        var doc = await SeedDocumentAsync("help-author", title);
        await db.Documents.Where(d => d.Id == doc.Id).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.IsHelpContent, true)
            .SetProperty(d => d.HelpSlug, slug)
            .SetProperty(d => d.HelpCategory, category));
        return doc;
    }

    [Fact]
    public async Task Help_lists_finds_and_serves_its_articles_to_anyone()
    {
        var article = await SeedHelpAsync("tables", "editing", "Making a table");
        await SeedBlockAsync(article.Id, "paragraph", """{"text":"Press the table button."}""");
        var anonymous = CreateAnonymousClient();

        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/lilia/help?category=editing");
        list.EnumerateArray().Select(a => a.GetProperty("title").GetString()).Should().Equal("Making a table");
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/lilia/help/categories"))
            .EnumerateArray().Should().NotBeEmpty();

        var bySlug = await anonymous.GetFromJsonAsync<JsonElement>("/api/lilia/help/slug/tables");
        bySlug.GetProperty("id").GetGuid().Should().Be(article.Id);
        (await anonymous.GetAsync($"/api/lilia/help/{article.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/lilia/help/search?q=table")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Help_never_serves_a_persons_document_by_its_id()
    {
        await SeedUserAsync("help-ada");
        var mine = await SeedDocumentAsync("help-ada", "Ada's private draft");
        var anonymous = CreateAnonymousClient();

        (await anonymous.GetAsync($"/api/lilia/help/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/lilia/help/{mine.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/lilia/help/slug/no-such-article")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var all = await anonymous.GetFromJsonAsync<JsonElement>("/api/lilia/help");
        all.EnumerateArray().Should().NotContain(a => a.GetProperty("id").GetGuid() == mine.Id);
    }

    [Fact]
    public async Task The_knowledge_base_lists_searches_and_serves_enabled_articles_in_the_asked_language()
    {
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM kb_articles WHERE slug LIKE 'kbtest-%';
                INSERT INTO kb_articles (slug, lang, title, summary, body, tool_slug, keywords, enabled) VALUES
                  ('kbtest-tables', 'en', 'Tables from CSV', 'Paste a CSV, get a table.', 'Body', 'latex-table', 'csv table', true),
                  ('kbtest-tables', 'fr', 'Tableaux depuis un CSV', 'Collez un CSV.', 'Corps', 'latex-table', 'csv tableau', true),
                  ('kbtest-hidden', 'en', 'Hidden draft', '', 'Secret body', NULL, 'csv', false);
                """);
        }
        var anonymous = CreateAnonymousClient();

        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/kb");
        var slugs = list.EnumerateArray().Select(a => a.GetProperty("slug").GetString()).ToList();
        slugs.Should().Contain("kbtest-tables").And.NotContain("kbtest-hidden");

        var found = await anonymous.GetFromJsonAsync<JsonElement>("/api/kb/search?q=csv");
        found.EnumerateArray().Select(a => a.GetProperty("slug").GetString()).Should().Contain("kbtest-tables").And.NotContain("kbtest-hidden");
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/kb/tool/latex-table"))
            .EnumerateArray().Select(a => a.GetProperty("slug").GetString()).Should().Contain("kbtest-tables");

        (await anonymous.GetFromJsonAsync<JsonElement>("/api/kb/kbtest-tables?lang=fr")).GetProperty("title").GetString()
            .Should().Be("Tableaux depuis un CSV");
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/kb/kbtest-tables?lang=es")).GetProperty("title").GetString()
            .Should().Be("Tables from CSV", "a language without the article falls back to English");
        (await anonymous.GetAsync("/api/kb/kbtest-hidden")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_changelog_is_public_and_newest_first()
    {
        await using (var db = CreateDbContext())
        {
            db.ChangelogEntries.AddRange(
                new ChangelogEntry { Id = Guid.NewGuid(), EntryDate = new DateOnly(2026, 10, 1), Title = new() { ["en"] = "Older" }, CreatedAt = DateTimeOffset.UtcNow },
                new ChangelogEntry { Id = Guid.NewGuid(), EntryDate = new DateOnly(2026, 11, 2), Title = new() { ["en"] = "Newer", ["fr"] = "Plus récent" }, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var entries = await CreateAnonymousClient().GetFromJsonAsync<JsonElement>("/api/public/changelog");

        var titles = entries.EnumerateArray().Select(e => e.GetProperty("title").GetProperty("en").GetString()).ToList();
        titles.IndexOf("Newer").Should().BeLessThan(titles.IndexOf("Older"));
        entries.EnumerateArray().First(e => e.GetProperty("title").GetProperty("en").GetString() == "Newer")
            .GetProperty("entryDate").GetString().Should().Be("2026-11-02");
    }

    [Fact]
    public async Task Lorem_ipsum_stays_within_its_limits()
    {
        var anonymous = CreateAnonymousClient();

        var words = await anonymous.GetFromJsonAsync<JsonElement>("/api/lorem/words?count=10000");
        words.GetProperty("count").GetInt32().Should().Be(500);
        words.GetProperty("text").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeLessThanOrEqualTo(500);
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/lorem/paragraphs?count=0")).GetProperty("count").GetInt32().Should().Be(1);
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/lorem/sentences?startWithLorem=true")).GetProperty("text").GetString()
            .Should().StartWith("Lorem ipsum");

        var generated = await anonymous.PostAsJsonAsync("/api/lorem/generate", new { type = "s", count = 2, startWithLorem = false });
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await generated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
