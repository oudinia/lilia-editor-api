using System.Threading.Channels;
using Lilia.Core.Entities;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

/// <summary>
/// Production sink: enqueues events on a Channel and flushes to
/// <c>import_telemetry_events</c> from a single background loop. The
/// parser hot path stays off the DB connection pool.
///
/// Buffer is bounded; on overflow we drop the *oldest* event and log
/// a warning. Telemetry should never apply backpressure to the import.
/// </summary>
public sealed class DbImportTelemetrySink : IImportTelemetrySink
{
    private readonly Channel<ImportTelemetryRecord> _channel;
    private readonly ILogger<DbImportTelemetrySink> _logger;

    public DbImportTelemetrySink(ILogger<DbImportTelemetrySink> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<ImportTelemetryRecord>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public void Record(ImportTelemetryRecord evt)
    {
        if (!_channel.Writer.TryWrite(evt))
        {
            _logger.LogWarning("[Telemetry] dropped event kind={Kind} token={Token}", evt.EventKind, evt.TokenOrEnv);
        }
    }

    internal ChannelReader<ImportTelemetryRecord> Reader => _channel.Reader;
    internal void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains the sink's channel and persists batches to the DB. One per
/// process. Restarted by the host on failure.
/// </summary>
public sealed class ImportTelemetryFlusher : BackgroundService
{
    private readonly DbImportTelemetrySink _sink;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportTelemetryFlusher> _logger;
    private const int BatchSize = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(3);

    public ImportTelemetryFlusher(
        IImportTelemetrySink sink,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportTelemetryFlusher> logger)
    {
        // We only flush when the registered sink is the DB-backed one;
        // tests / no-op installs skip this background work entirely.
        _sink = sink as DbImportTelemetrySink
            ?? throw new InvalidOperationException(
                "ImportTelemetryFlusher requires DbImportTelemetrySink. Don't register it in test hosts.");
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<ImportTelemetryRecord>(BatchSize);
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
                    db.ImportTelemetryEvents.Add(new ImportTelemetryEvent
                    {
                        EventKind = rec.EventKind,
                        Severity = rec.Severity,
                        SourceFormat = rec.SourceFormat,
                        TokenOrEnv = rec.TokenOrEnv,
                        BlockKindEmitted = rec.BlockKindEmitted,
                        BlockKindExpected = rec.BlockKindExpected,
                        SampleText = Truncate(rec.SampleText, 200),
                        SourceFileName = rec.SourceFileName,
                        Metadata = rec.Metadata,
                    });
                }
                await db.SaveChangesAsync(stoppingToken);
                buffer.Clear();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "[Telemetry] flush failed; dropping {Count} buffered events", buffer.Count);
                buffer.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s.Substring(0, max);
}
