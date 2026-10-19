using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Challenge (19 Oct): Ask Lilia questions the author on their own document. It never writes, even
/// with the dock's Edit switch on; while it runs, the author's answers stay with it whatever they
/// say. The model is scripted (no AI is called).
/// </summary>
[Collection("Integration")]
public class ChallengeAskTests : FourCallersTestBase, IDisposable
{
    private readonly ScriptedChatClient _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ChallengeAskTests(TestDatabaseFixture fixture) : base(fixture)
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

    [Fact]
    public async Task A_challenge_in_Edit_mode_asks_and_never_gets_the_tools_to_write()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.Enqueue("Jury mode — I'll ask what a thesis committee would. What is new in your work?");

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "Rehearse my defence: ask me what a jury would", documentId = s.DocumentId.ToString(), editMode = true });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("skillId").GetString().Should().Be("lilia-challenge");
        body.GetProperty("documentChanged").GetBoolean().Should().BeFalse();
        var system = _chat.Calls.Single().First().Text;
        system.Should().Contain("Lilia Challenge").And.NotContain("EDIT MODE IS ON");
    }

    [Fact]
    public async Task While_a_challenge_runs_the_author_s_answer_stays_with_it_whatever_its_words()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.Enqueue("That answers the why, not the how much. Next: what baseline did you compare with?");

        // Without the hint, "table" and "columns" would send this answer to the Table skill.
        var answer = "The table shows the columns of results for each method, and the new one is faster.";
        var res = await c.PostAsJsonAsync("/api/ai/ask", new
        {
            message = answer,
            documentId = s.DocumentId.ToString(),
            continueSkill = "lilia-challenge",
            history = new[] { new { role = "user", content = "Be Reviewer 2" }, new { role = "assistant", content = "What is new in your work?" } },
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("skillId").GetString().Should().Be("lilia-challenge");
        _chat.Calls.Single().Should().Contain(m => m.Text == "What is new in your work?", "the thread so far goes with it");
    }
}
