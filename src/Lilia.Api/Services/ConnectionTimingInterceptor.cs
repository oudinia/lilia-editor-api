using System.Data.Common;
using System.Diagnostics;
using Lilia.Core.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lilia.Api.Services;

/// <summary>
/// How long opening a database connection takes, added to its request as <c>db.open</c> (<see cref="Timing"/>). The
/// first requests after a quiet spell took 3–7 s while their queries took under 1 s (production, 14 Nov 2026): the
/// rest goes before the first query, which is where a new connection to a suspended Neon compute waits. An open over
/// <see cref="ConnectionOpenTiming.SlowMs"/> is logged on its own. A pooled connection opens in well under a millisecond.
/// </summary>
public sealed class ConnectionTimingInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<ConnectionTimingInterceptor> _logger;

    public ConnectionTimingInterceptor(ILogger<ConnectionTimingInterceptor> logger) => _logger = logger;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        ConnectionOpenTiming.Record(eventData.Duration, _logger);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ConnectionOpenTiming.Record(eventData.Duration, _logger);
        return Task.CompletedTask;
    }
}

/// <summary>What <see cref="ConnectionTimingInterceptor"/> does with an open's duration, apart from EF (testable).</summary>
public static class ConnectionOpenTiming
{
    public const int SlowMs = 1000;

    public static void Record(TimeSpan duration, ILogger logger)
    {
        Timing.Current?.Add("db.open", (long)(duration.TotalSeconds * Stopwatch.Frequency));
        if (duration.TotalMilliseconds >= SlowMs)
            logger.LogWarning("[DB] slow connection open {Ms:0}ms", duration.TotalMilliseconds);
    }
}
