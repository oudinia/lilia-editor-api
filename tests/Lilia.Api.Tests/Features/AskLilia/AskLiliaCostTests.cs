using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// What Ask Lilia costs (4 Nov 2026). The changing context (the document's blocks, the library) goes after the latest
/// question, which carries a cache marker, so the next question reads the unchanged system prompt and conversation
/// from the prompt cache at a tenth of the price, even after an edit; and each turn's audit row keeps its skill, cache use, rounds, largest prompt and cost. The model is
/// scripted (no AI is called).
/// </summary>
public class AskLiliaCostTests : FourCallersTestBase, IDisposable
{
    private readonly ScriptedChatClient _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AskLiliaCostTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AI:Anthropic:ApiKey", "sk-test-not-a-key");
            b.UseSetting("AI:Enabled", "true");
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IChatClient)).ToList()) s.Remove(d);
                s.AddSingleton<IChatClient>(_chat);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Caller(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static readonly object[] Thread =
    [
        new { role = "user", content = "Is my introduction clear?" },
        new { role = "assistant", content = "Mostly; the second sentence is long." },
    ];

    [Fact]
    public async Task The_document_s_blocks_come_with_the_latest_message_not_the_system_prompt()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.Enqueue("It reads well.");

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "Does this paragraph read well?", documentId = s.DocumentId.ToString(), history = Thread });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var sent = _chat.Calls.Single();
        sent.First().Text.Should().Contain("LIVE CONTEXT").And.NotContain("Private text");
        sent[1].Text.Should().Be("Is my introduction clear?", "the conversation sits between the system prompt and the latest message");
        var latest = sent.Last();
        latest.Role.Should().Be(ChatRole.User);
        latest.Text.Should().Contain("<live_context>").And.Contain("Private text").And.Contain("Does this paragraph read well?");
        var question = latest.Contents.First();
        question.Should().BeOfType<TextContent>().Which.Text.Should().Be("Does this paragraph read well?", "the question comes first, the live context after it");
        var raw = question.RawRepresentation.Should().BeOfType<Anthropic.Models.Messages.ContentBlockParam>().Subject;
        raw.Value.Should().BeOfType<Anthropic.Models.Messages.TextBlockParam>()
            .Which.CacheControl.Should().NotBeNull("the cache's read point for the next question");
    }

    [Fact]
    public async Task An_edit_between_questions_leaves_the_start_of_the_request_unchanged()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.Enqueue("It reads well.", "Better now.");

        (await c.PostAsJsonAsync("/api/ai/ask", new { message = "Does this paragraph read well?", documentId = s.DocumentId.ToString(), history = Thread }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlAsync($"UPDATE blocks SET content = {"{\"text\":\"Edited text\"}"}::jsonb WHERE id = {s.BlockId}");
        // The same question, so the same skill answers: a question for another skill brings its own guidance.
        (await c.PostAsJsonAsync("/api/ai/ask", new { message = "Does this paragraph read well?", documentId = s.DocumentId.ToString(), history = Thread }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var (first, second) = (_chat.Calls[0], _chat.Calls[1]);
        (second.First().Text == first.First().Text).Should().BeTrue("the system prompt holds nothing the edit changed");
        second.Take(second.Count - 1).Select(m => m.Text).Should().Equal(first.Take(first.Count - 1).Select(m => m.Text));
        second.Last().Text.Should().Contain("Edited text").And.NotContain("Private text");
    }

    [Fact]
    public async Task Without_a_document_the_library_comes_with_the_latest_message()
    {
        await SeedSharedDocumentAsync("Thesis on lattices");
        using var c = Caller(OwnerId);
        _chat.Enqueue("Your thesis is the furthest along.");

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "Which document is closest to done?" });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var sent = _chat.Calls.Single();
        sent.First().Text.Should().NotContain("Thesis on lattices");
        sent.Last().Text.Should().Contain("<live_context>").And.Contain("THE LIBRARY").And.Contain("Thesis on lattices");
    }

    [Fact]
    public async Task Each_turn_records_its_skill_cache_use_rounds_largest_prompt_and_cost()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.Usage = new UsageDetails
        {
            InputTokenCount = 1_000, OutputTokenCount = 40, CachedInputTokenCount = 600,
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["CacheCreationInputTokens"] = 300 },
        };
        _chat.Enqueue("It reads well.");

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "Does this paragraph read well?", documentId = s.DocumentId.ToString() });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        await using var db = CreateDbContext();
        var row = await db.AiRequests.AsNoTracking().Where(r => r.UserId == OwnerId).OrderByDescending(r => r.CreatedAt).FirstAsync();
        row.Status.Should().Be("success");
        row.Skill.Should().NotBeNullOrEmpty();
        (row.PromptTokens, row.CompletionTokens, row.CacheReadTokens, row.CacheWriteTokens).Should().Be((1_000, 40, 600, 300));
        (row.Rounds, row.LargestPromptTokens).Should().Be((1, 1_000));
        row.CostUsd.Should().BeGreaterThan(0);
    }
}
