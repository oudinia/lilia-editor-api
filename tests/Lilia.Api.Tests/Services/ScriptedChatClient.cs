using Microsoft.Extensions.AI;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A model that answers from a script, in order, and keeps what it was sent. No AI is called in tests.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<string> _replies;
    private readonly object _gate = new();

    public ScriptedChatClient(params string[] replies) => _replies = new Queue<string>(replies);

    /// <summary>Each call's messages, copied at the time of the call.</summary>
    public List<List<ChatMessage>> Calls { get; } = new();

    public void Enqueue(params string[] replies)
    {
        lock (_gate) foreach (var r in replies) _replies.Enqueue(r);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string reply;
        lock (_gate)
        {
            Calls.Add(messages.ToList());
            reply = _replies.Count > 0 ? _replies.Dequeue() : throw new InvalidOperationException("The script has no reply left.");
        }
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 40 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
