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
/// POST /api/ai/architect, end to end (3 Nov 2026): no test called it, which the test picker's
/// URL ownership (API #419) brought to light. The real service runs; the model is scripted (no AI is
/// called). It proposes changes and never writes; it sees only a document its caller may open; it
/// is locked when AI is off or has no key; a failed model call is a 502 that names nothing inside.
/// </summary>
public class AiArchitectEndpointTests : IntegrationTestBase, IDisposable
{
    private const string Url = "/api/ai/architect";
    private readonly ScriptedChatClient _chat = new();

    public AiArchitectEndpointTests(TestDatabaseFixture fixture) : base(fixture) { }

    private readonly List<WebApplicationFactory<Program>> _factories = new();

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private HttpClient Caller(string userId, string key = "sk-test-not-a-key", bool enabled = true)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AI:Anthropic:ApiKey", key);
            b.UseSetting("AI:Enabled", enabled ? "true" : "false");
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IChatClient)).ToList()) s.Remove(d);
                s.AddSingleton<IChatClient>(_chat);
            });
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static object Ask(string documentId, string text = "Give my paper a structure.") =>
        new { documentId, messages = new[] { new { role = "user", content = text } } };

    private const string Proposal = """
        {"reply":"Here is a skeleton.","operations":[
          {"op":"add","afterId":null,"block":{"type":"heading","content":{"text":"Introduction","level":1}}}
        ]}
        """;

    [Fact]
    public async Task It_proposes_changes_to_the_callers_document_and_writes_nothing()
    {
        await SeedUserAsync("arch-ada");
        var doc = await SeedDocumentAsync("arch-ada", "Ada's paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The analytical engine weaves algebra."}""");
        _chat.Enqueue(Proposal);

        var res = await Caller("arch-ada").PostAsJsonAsync(Url, Ask(doc.Id.ToString()));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reply").GetString().Should().Be("Here is a skeleton.");
        var op = body.GetProperty("operations").EnumerateArray().Should().ContainSingle().Subject;
        op.GetProperty("op").GetString().Should().Be("add");
        op.GetProperty("block").GetProperty("type").GetString().Should().Be("heading");
        body.GetProperty("usage").GetProperty("inputTokens").GetInt32().Should().Be(100);

        var system = _chat.Calls.Should().ContainSingle().Subject.First(m => m.Role == ChatRole.System).Text;
        system.Should().Contain("Ada's paper").And.Contain("The analytical engine weaves algebra.");

        await using var db = CreateDbContext();
        (await db.Blocks.CountAsync(b => b.DocumentId == doc.Id)).Should().Be(1, "the editor applies accepted changes, not the API");
        var audit = await db.AiRequests.SingleAsync(r => r.UserId == "arch-ada");
        audit.Should().Match<Lilia.Core.Entities.AiRequest>(r => r.Status == "success" && r.DocumentId == doc.Id);
    }

    [Fact]
    public async Task Another_persons_document_never_reaches_the_model()
    {
        await SeedUserAsync("arch-grace");
        await SeedUserAsync("arch-mallory");
        var doc = await SeedDocumentAsync("arch-grace", "Grace's private notes");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The compiler secret."}""");
        _chat.Enqueue(Proposal);

        var res = await Caller("arch-mallory").PostAsJsonAsync(Url, Ask(doc.Id.ToString()));

        res.StatusCode.Should().Be(HttpStatusCode.OK, "an unknown document is treated as a new draft");
        var system = _chat.Calls.Should().ContainSingle().Subject.First(m => m.Role == ChatRole.System).Text;
        system.Should().NotContain("Grace's private notes").And.NotContain("The compiler secret.");
        await using var db = CreateDbContext();
        (await db.AiRequests.SingleAsync(r => r.UserId == "arch-mallory")).DocumentId.Should().BeNull();
    }

    [Fact]
    public async Task With_AI_switched_off_it_is_locked_and_no_model_is_called()
    {
        var res = await Caller("arch-off", enabled: false).PostAsJsonAsync(Url, Ask("new"));

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("locked").GetBoolean().Should().BeTrue();
        body.GetProperty("reason").GetString().Should().Be("disabled");
        _chat.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_key_it_is_locked_and_no_model_is_called()
    {
        var res = await Caller("arch-nokey", key: "sk-placeholder").PostAsJsonAsync(Url, Ask("new"));

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString().Should().Be("no-key");
        _chat.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_model_call_is_a_502_that_names_nothing_inside_and_is_recorded()
    {
        await SeedUserAsync("arch-fail");
        // The script is empty: the model call throws "The script has no reply left."

        var res = await Caller("arch-fail").PostAsJsonAsync(Url, Ask("new"));

        res.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var text = await res.Content.ReadAsStringAsync();
        text.Should().Contain("The AI service failed to respond").And.NotContain("script");
        await using var db = CreateDbContext();
        (await db.AiRequests.SingleAsync(r => r.UserId == "arch-fail")).Status.Should().Be("error");
    }

    [Fact]
    public async Task A_request_without_a_document_or_a_message_is_refused()
    {
        var caller = Caller("arch-empty");

        (await caller.PostAsJsonAsync(Url, Ask(""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await caller.PostAsJsonAsync(Url, new { documentId = "new", messages = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _chat.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_sign_in_it_is_401()
    {
        var res = await CreateAnonymousClient().PostAsJsonAsync(Url, Ask("new"));
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
