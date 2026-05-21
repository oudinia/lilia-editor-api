using System.Threading.Channels;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

/// <summary>
/// Production sink: enqueues sync-telemetry events on a Channel and
/// flushes them to <c>sync_telemetry_events</c> from a single
/// background loop, so the editor's sync path never touches the DB
/// connection pool. Buffer is bounded; on overflow the oldest event is
/// dropped with a warning — telemetry must never apply backpressure to
/// a user's edits. Mirrors <see cref="DbImportTelemetrySink"/>.
/// </summary>
public sealed class DbSyncTelemetrySink : ISyncTelemetrySink
{
    private readonly Channel<SyncTelemetryRecord> _channel;
    private readonly ILogger<DbSyncTelemetrySink> _logger;

    public DbSyncTelemetrySink(ILogger<DbSyncTelemetrySink> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<SyncTelemetryRecord>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public void Record(SyncTelemetryRecord evt)
    {
        if (!_channel.Writer.TryWrite(evt))
        {
            _logger.LogWarning("[SyncTelemetry] dropped event kind={Kind} doc={Doc}", evt.EventKind, evt.DocumentId);
        }
    }

    internal ChannelReader<SyncTelemetryRecord> Reader => _channel.Reader;
    internal void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains the sync-telemetry channel and persists batches to the DB.
/// One per process; restarted by the host on failure.
/// </summary>
public sealed class SyncTelemetryFlusher : BackgroundService
{
    private readonly DbSyncTelemetrySink _sink;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncTelemetryFlusher> _logger;
    private const int BatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(3);

    public SyncTelemetryFlusher(
        ISyncTelemetrySink sink,
        IServiceScopeFactory scopeFactory,
        ILogger<SyncTelemetryFlusher> logger)
    {
        // Only flush when the registered sink is the DB-backed one;
        // tests / no-op installs skip this background work entirely.
        _sink = sink as DbSyncTelemetrySink
            ?? throw new InvalidOperationException(
                "SyncTelemetryFlusher requires DbSyncTelemetrySink. Don't register it in test hosts.");
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<SyncTelemetryRecord>(BatchSize);
        var reader = _sink.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var ct = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                ct.CancelAfter(FlushInterval);

                try
                {
                    while (buffer.Count < BatchSize && await reader.WaitToReadAsync(ct.Token))
                    {
                        while (buffer.Count < BatchSize && reader.TryRead(out var evt))
                        {
                            buffer.Add(evt);
                        }
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Flush window elapsed — drain whatever's queued.
                }

                if (buffer.Count == 0) continue;

                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
                foreach (var rec in buffer)
                {
                    db.SyncTelemetryEvents.Add(new SyncTelemetryEvent
                    {
                        EventKind = rec.EventKind,
                        Severity = rec.Severity,
                        Source = rec.Source,
                        DocumentId = rec.DocumentId,
                        UserId = rec.UserId,
                        AttemptCount = rec.AttemptCount,
                        DurationMs = rec.DurationMs,
                        Detail = Truncate(rec.Detail, 500),
                        Metadata = rec.Metadata,
                    });
                }
                await db.SaveChangesAsync(stoppingToken);
                buffer.Clear();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SyncTelemetry] flush failed; dropping {Count} buffered events", buffer.Count);
                buffer.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s.Substring(0, max);
}
