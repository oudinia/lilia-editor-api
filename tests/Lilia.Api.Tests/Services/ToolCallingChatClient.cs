using Microsoft.Extensions.AI;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A model that calls tools from a script: each round, <c>next</c> gets the round number and the
/// options it was sent and returns the function calls to make (empty: it answers <see cref="Answer"/>).
/// With tool choice "none" it answers, as the real model must. No AI is called in tests.
/// </summary>
internal sealed class ToolCallingChatClient(Func<int, IReadOnlyList<FunctionCallContent>> next) : IChatClient
{
    public string Answer { get; init; } = "Here is what I did.";

    /// <summary>The tool mode of each call, in order.</summary>
    public List<ChatToolMode?> ToolModes { get; } = new();

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var round = ToolModes.Count;
        ToolModes.Add(options?.ToolMode);
        var calls = options?.ToolMode is NoneChatToolMode ? [] : next(round);
        var message = calls.Count == 0
            ? new ChatMessage(ChatRole.Assistant, Answer)
            : new ChatMessage(ChatRole.Assistant, calls.Cast<AIContent>().ToList());
        return Task.FromResult(new ChatResponse(message)
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 40 },
        });
    }

    public static FunctionCallContent Call(string name, Dictionary<string, object?>? args = null) =>
        new(Guid.NewGuid().ToString("N"), name, args ?? new Dictionary<string, object?>());

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
