using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Ask Lilia's prompt caching (3 Nov 2026), through the real SDK adapter against a fake Anthropic API: what
/// the request carries, and how the cached tokens come back. Nothing is sent to Anthropic.
/// </summary>
public class AskLiliaCachingTests
{
    /// <summary>Answers every call with one text reply and the given usage; keeps each request body.</summary>
    private sealed class FakeAnthropic(string usageJson) : HttpMessageHandler
    {
        public List<JsonDocument> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)));
            var body = $$"""
                {"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5",
                 "content":[{"type":"text","text":"Done."}],"stop_reason":"end_turn","stop_sequence":null,
                 "usage":{{usageJson}}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (IChatClient Client, FakeAnthropic Api) Client(string usageJson)
    {
        var api = new FakeAnthropic(usageJson);
        var client = new AnthropicClient { ApiKey = "test-key", HttpClient = new HttpClient(api) }.AsIChatClient("claude-sonnet-5");
        return (client, api);
    }

    private const string NoCache = """{"input_tokens":1200,"output_tokens":40}""";

    [Fact]
    public async Task The_request_asks_for_automatic_caching_and_keeps_the_rest()
    {
        var (client, api) = Client(NoCache);
        var options = new ChatOptions
        {
            ModelId = "claude-sonnet-5",
            MaxOutputTokens = AskLiliaService.MaxOutputTokens,
            Instructions = "You are Lilia.",
            RawRepresentationFactory = _ => AskLiliaService.CachedRequest("claude-sonnet-5"),
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Is my abstract too long?")], options);

        var body = api.Requests.Single().RootElement;
        body.GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        body.GetProperty("model").GetString().Should().Be("claude-sonnet-5");
        body.GetProperty("max_tokens").GetInt32().Should().Be(AskLiliaService.MaxOutputTokens);
        body.GetProperty("messages").GetArrayLength().Should().Be(1, "the question, once: the base request brings no messages of its own");
        body.GetProperty("system").ToString().Should().Contain("You are Lilia.");
    }

    [Fact]
    public async Task Cached_tokens_come_back_inside_the_input_count_with_reads_and_writes_apart()
    {
        var (client, _) = Client("""{"input_tokens":1200,"cache_creation_input_tokens":3000,"cache_read_input_tokens":40000,"output_tokens":40}""");

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")],
            new ChatOptions { RawRepresentationFactory = _ => AskLiliaService.CachedRequest("claude-sonnet-5") });

        var use = AiUsage.From(response.Usage);
        use.Should().Be(new AiTokenUse(Input: 44200, Output: 40, CacheWrite: 3000, CacheRead: 40000));
    }

    [Fact]
    public void A_cached_question_costs_its_cache_price_not_the_full_one()
    {
        // An unpriced model: 1 credit per 1,000 tokens, which keeps the arithmetic visible.
        var catalog = new AiCatalogService(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiOptions()), NullLogger<AiCatalogService>.Instance);

        // 1,200 uncached + 3,000 written × 1.25 + 40,000 read × 0.1 = 8,950 input; + 40 output = 8,990 → 9.
        catalog.CreditsFor("unpriced-model", new AiTokenUse(44200, 40, CacheWrite: 3000, CacheRead: 40000)).Should().Be(9);
        // The same tokens, as they were counted before caching: 44,240 → 45.
        catalog.CreditsFor("unpriced-model", 44200, 40).Should().Be(45);
        // And the dollar estimate drops the same way.
        AiArchitectPricing.ComputeCostUsd("claude-sonnet-5", new AiTokenUse(44200, 40, 3000, 40000))
            .Should().BeLessThan(AiArchitectPricing.ComputeCostUsd("claude-sonnet-5", 44200, 40) / 3);
    }
}
