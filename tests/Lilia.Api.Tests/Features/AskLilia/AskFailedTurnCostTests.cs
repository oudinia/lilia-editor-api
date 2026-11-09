using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A turn that fails or is cancelled after some rounds keeps their cost (9 Nov 2026: the browser closed a chapter draft
/// at 125 s, after it had written 33 blocks; the audit row stayed "pending" with no cost and nothing was charged, so the
/// monthly AI caps never saw it). The author is charged only when the turn had written to the document.
/// </summary>
public class AskFailedTurnCostTests : FourCallersTestBase, IDisposable
{
    private readonly SecondRoundFails _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AskFailedTurnCostTests(TestDatabaseFixture fixture) : base(fixture)
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
    public async Task A_turn_that_fails_after_writing_keeps_its_cost_and_is_charged()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.FirstRound = [ToolCallingChatClient.Call("set_title", new() { ["title"] = "Sleep and Memory" })];

        await c.PostAsJsonAsync("/api/ai/ask", new { message = "Change the title to Sleep and Memory", documentId = s.DocumentId.ToString(), editMode = true });

        var (row, charged) = await RowAndChargeAsync();
        row.Status.Should().Be("error");
        (row.Rounds, row.PromptTokens, row.CompletionTokens).Should().Be((1, 20_000, 4_000));
        row.CostUsd.Should().BeGreaterThan(0);
        charged.Should().BeGreaterThan(0, "the title was written: the author got that part");
    }

    [Fact]
    public async Task A_turn_that_fails_before_writing_keeps_its_cost_and_is_not_charged()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(OwnerId);
        _chat.FirstRound = [ToolCallingChatClient.Call("get_outline")];

        await c.PostAsJsonAsync("/api/ai/ask", new { message = "Change the title to Sleep and Memory", documentId = s.DocumentId.ToString(), editMode = true });

        var (row, charged) = await RowAndChargeAsync();
        row.Status.Should().Be("error");
        row.CostUsd.Should().BeGreaterThan(0);
        charged.Should().Be(0);
    }

    [Fact]
    public async Task A_turn_the_browser_closes_keeps_its_cost_and_says_it_was_cancelled()
    {
        var s = await SeedSharedDocumentAsync();
        _chat.FirstRound = [ToolCallingChatClient.Call("set_title", new() { ["title"] = "Sleep and Memory" })];
        _chat.WaitForCancel = true;
        // The request's own token, as the browser closing the request cancels it (the test host does not pass a
        // client's cancel through to the server, so the service is called directly).
        using var browser = new CancellationTokenSource();
        using var scope = _factory.Services.CreateScope();
        var ask = scope.ServiceProvider.GetRequiredService<IAskLiliaService>().AskAsync(OwnerId,
            new AskLiliaRequest("Change the title to Sleep and Memory", DocumentId: s.DocumentId.ToString(), EditMode: true), browser.Token);
        await _chat.SecondRoundStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await browser.CancelAsync();
        await FluentActions.Awaiting(() => ask).Should().ThrowAsync<OperationCanceledException>();

        var (row, charged) = await RowAndChargeAsync();
        row.Status.Should().Be("error");
        row.ErrorMessage.Should().StartWith("cancelled");
        row.CostUsd.Should().BeGreaterThan(0);
        charged.Should().BeGreaterThan(0, "the title was written before the browser closed");
    }

    private async Task<(Lilia.Core.Entities.AiRequest Row, int Charged)> RowAndChargeAsync()
    {
        await using var db = CreateDbContext();
        var row = await db.AiRequests.AsNoTracking().Where(r => r.UserId == OwnerId).OrderByDescending(r => r.CreatedAt).FirstAsync();
        var charged = -await db.AiCreditLedger.Where(l => l.AiRequestId == row.Id && l.Reason == "spend").SumAsync(l => l.Delta);
        return (row, charged);
    }

    /// <summary>The first round calls tools; the second fails, or waits until the request is cancelled.</summary>
    private sealed class SecondRoundFails : IChatClient
    {
        private int _round;
        public IReadOnlyList<FunctionCallContent> FirstRound { get; set; } = [];
        public bool WaitForCancel { get; set; }
        public TaskCompletionSource SecondRoundStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _round) == 1)
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, FirstRound.Cast<AIContent>().ToList()))
                {
                    Usage = new UsageDetails { InputTokenCount = 20_000, OutputTokenCount = 4_000 },
                };
            SecondRoundStarted.TrySetResult();
            if (!WaitForCancel) throw new HttpRequestException("Anthropic is unavailable");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
