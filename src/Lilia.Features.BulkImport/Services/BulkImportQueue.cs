using Wolverine;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>
/// Where the slice sends its messages. Wolverine's bus in the app (RabbitMQ or durable local queues, by
/// <c>BulkImport:RabbitMq</c>); tests put their own in, to run each step by hand and in any order.
/// </summary>
public interface IBulkImportQueue
{
    Task SendAsync(object message, TimeSpan? delay = null);
}

public sealed class WolverineBulkImportQueue(IMessageBus bus) : IBulkImportQueue
{
    public async Task SendAsync(object message, TimeSpan? delay = null)
    {
        if (delay is { } d && d > TimeSpan.Zero) await bus.ScheduleAsync(message, d);
        else await bus.PublishAsync(message);
    }
}
