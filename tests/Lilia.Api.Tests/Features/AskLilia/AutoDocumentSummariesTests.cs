using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// Automatic document summaries are opt-in (Oussama, 26 Nov 2026, under GDPR): off for everyone until turned on in
/// Settings. While off, the summary endpoint refuses before any AI call, and Ask Lilia's library context leaves the
/// stored one-liners out. The model is scripted (no AI is called).
/// </summary>
public class AutoDocumentSummariesTests : FourCallersTestBase, IDisposable
{
    private readonly ScriptedChatClient _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AutoDocumentSummariesTests(TestDatabaseFixture fixture) : base(fixture)
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

    private static async Task<bool> SettingAsync(HttpClient c)
    {
        var res = await c.GetAsync("/api/preferences");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("autoDocumentSummaries").GetBoolean();
    }

    private static async Task TurnAsync(HttpClient c, bool on)
    {
        var res = await c.PutAsJsonAsync("/api/preferences", new { autoDocumentSummaries = on });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Off_by_default_and_the_summary_is_refused_without_calling_the_AI()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);

        (await SettingAsync(c)).Should().BeFalse("summaries are opt-in for every account");

        var res = await c.PostAsync($"/api/documents/{s.DocumentId}/summary", null);

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadAsStringAsync()).Should().Contain("summaries_off");
        _chat.Calls.Should().BeEmpty("no excerpt may reach the AI provider while the switch is off");
        await using var db = CreateDbContext();
        (await db.Documents.IgnoreQueryFilters().Where(d => d.Id == s.DocumentId).Select(d => d.AiSummary).SingleAsync())
            .Should().BeNull();
    }

    [Fact]
    public async Task Turned_on_the_summary_is_written_and_turning_it_off_refuses_again_and_keeps_it()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.Enqueue("A short paper on authorisation.");

        await TurnAsync(c, true);
        (await SettingAsync(c)).Should().BeTrue();
        var res = await c.PostAsync($"/api/documents/{s.DocumentId}/summary", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.Content.ReadAsStringAsync()).Should().Contain("A short paper on authorisation.");
        _chat.Calls.Should().HaveCount(1);

        await TurnAsync(c, false);
        var again = await c.PostAsync($"/api/documents/{s.DocumentId}/summary", null);
        again.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _chat.Calls.Should().HaveCount(1, "turning it off stops new requests at once");
        await using var db = CreateDbContext();
        (await db.Documents.IgnoreQueryFilters().Where(d => d.Id == s.DocumentId).Select(d => d.AiSummary).SingleAsync())
            .Should().Be("A short paper on authorisation.", "stored summaries are kept, not deleted");
    }

    [Fact]
    public async Task Ask_Lilia_s_library_leaves_the_one_liners_out_while_off()
    {
        var s = await SeedSharedDocumentAsync("Thesis on lattices");
        await using (var db = CreateDbContext())
            await db.Documents.IgnoreQueryFilters().Where(d => d.Id == s.DocumentId)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.AiSummary, "A gist that must stay private."));
        using var c = Caller(OwnerId);
        _chat.Enqueue("Your thesis.", "Your thesis, again.");

        (await c.PostAsJsonAsync("/api/ai/ask", new { message = "Which document is closest to done?" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var off = _chat.Calls[0].Last().Text;
        off.Should().Contain("THE LIBRARY").And.Contain("Thesis on lattices")
            .And.NotContain("A gist that must stay private.").And.Contain("automatic summaries are off");

        await TurnAsync(c, true);
        (await c.PostAsJsonAsync("/api/ai/ask", new { message = "Which document is closest to done?" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        _chat.Calls[1].Last().Text.Should().Contain("A gist that must stay private.");
    }
}
