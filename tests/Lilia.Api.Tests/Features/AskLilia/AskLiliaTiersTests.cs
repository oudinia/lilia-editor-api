using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A model and effort per skill tier (3 Nov 2026): the "fast" skills (polish, coverage, tutor) run on
/// Claude Haiku 5.5 at low effort, the others on the default model as before, and a model the user picked
/// still wins. Nothing is sent to Anthropic.
/// </summary>
public class AskLiliaTiersTests
{
    private static readonly AiSkill Polish = new("lilia-polish", "Polish", "", [], "fast", 5);
    private static readonly AiSkill Citations = new("lilia-citations", "Citations", "", [], "default", 6);

    private static AskLiliaService Service(bool haikuInCatalog = true)
    {
        var catalog = new Mock<IAiCatalogService>();
        catalog.Setup(c => c.DefaultModelId()).Returns("claude-sonnet-5");
        catalog.Setup(c => c.IsAllowedFor("claude-sonnet-5", It.IsAny<string>())).Returns(true);
        catalog.Setup(c => c.Get(It.IsAny<string>())).Returns<string>(id =>
            id == "claude-haiku-5-5" && !haikuInCatalog ? null : new AiModel { Id = id });
        // The settings as appsettings.json ships them.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Tiers:fast:Model"] = "claude-haiku-5-5",
            ["AI:Tiers:fast:Effort"] = "low",
        }).Build();
        var hub = new Mock<IHubContext<Lilia.Api.Hubs.DocumentHub>>();
        return new AskLiliaService(
            Mock.Of<IChatClient>(), Mock.Of<IEntitlementService>(), catalog.Object,
            Mock.Of<IAskLiliaRouter>(), Mock.Of<IKbService>(),
            Mock.Of<IDocumentService>(), Mock.Of<IBlockService>(), Mock.Of<IRenderService>(), Mock.Of<IVersionService>(),
            Mock.Of<ILmlTextParser>(), new Lilia.Import.Services.LatexParser(), hub.Object,
            new LiliaDbContext(new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            Options.Create(new AiOptions()), config, NullLogger<AskLiliaService>.Instance);
    }

    [Fact]
    public async Task A_fast_skill_runs_on_Haiku_at_low_effort()
    {
        (await Service().ChooseModelAsync("u1", null, Polish, default)).Should().Be(("claude-haiku-5-5", "low"));
    }

    [Fact]
    public async Task Other_skills_run_on_the_default_model_as_before()
    {
        (await Service().ChooseModelAsync("u1", null, Citations, default)).Should().Be(("claude-sonnet-5", (string?)null));
    }

    [Fact]
    public async Task A_model_the_user_picked_still_wins()
    {
        (await Service().ChooseModelAsync("u1", "claude-sonnet-5", Polish, default)).Should().Be(("claude-sonnet-5", (string?)null));
    }

    [Fact]
    public async Task Without_Haiku_in_the_catalog_a_fast_skill_runs_as_before()
    {
        (await Service(haikuInCatalog: false).ChooseModelAsync("u1", null, Polish, default)).Should().Be(("claude-sonnet-5", (string?)null));
    }

    private sealed class FakeAnthropic : HttpMessageHandler
    {
        public JsonDocument? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            const string body = """
                {"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"Done."}],
                 "stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":2}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task The_request_carries_the_tiers_model_effort_and_the_cache()
    {
        var api = new FakeAnthropic();
        var client = new AnthropicClient { ApiKey = "test-key", HttpClient = new HttpClient(api) }.AsIChatClient("claude-sonnet-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Tighten this paragraph.")],
            new ChatOptions { RawRepresentationFactory = _ => AskLiliaService.CachedRequest("claude-haiku-5-5", "low") });

        var body = api.Request!.RootElement;
        body.GetProperty("model").GetString().Should().Be("claude-haiku-5-5");
        body.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("low");
        body.GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
    }
}
