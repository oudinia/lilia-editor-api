using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A long answer starts at once and keeps the connection alive (10 Nov 2026: Cloudflare answered 524 at about 125 s
/// while a chapter draft was still being written into the paper). Spaces first, then the JSON; a failure that comes
/// late says its status inside the 200. The keep-alive starts after 50 ms here instead of 20 s.
/// </summary>
public class AskKeepAliveTests : FourCallersTestBase, IDisposable
{
    private readonly SlowChat _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AskKeepAliveTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AI:Anthropic:ApiKey", "sk-test-not-a-key");
            b.UseSetting("AI:Enabled", "true");
            b.UseSetting("AI:AskKeepAliveAfterSeconds", "0.05");
            b.UseSetting("AI:AskKeepAliveEverySeconds", "0.05");
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IChatClient)).ToList()) s.Remove(d);
                s.AddSingleton<IChatClient>(_chat);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Caller()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", OwnerId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{OwnerId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", OwnerId);
        return c;
    }

    [Fact]
    public async Task A_long_answer_sends_spaces_then_the_answer()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller();

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "Does this paragraph read well?", documentId = s.DocumentId.ToString() });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await res.Content.ReadAsStringAsync();
        text.Should().StartWith(" ", "the keep-alive went out before the answer");
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("reply").GetString().Should().Contain("It reads well.");
        body.TryGetProperty("status", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_late_failure_says_its_status_inside_the_200()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller();
        _chat.Fail = true;

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "Does this paragraph read well?", documentId = s.DocumentId.ToString() });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetInt32().Should().Be(502);
        body.GetProperty("message").GetString().Should().Contain("failed to respond");
    }

    /// <summary>Answers after 400 ms, or fails then.</summary>
    private sealed class SlowChat : IChatClient
    {
        public bool Fail { get; set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(400, cancellationToken);
            if (Fail) throw new HttpRequestException("Anthropic is unavailable");
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "It reads well."))
            {
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 40 },
            };
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
