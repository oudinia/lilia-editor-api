using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lilia.Api.Tests.Shared;

/// <summary>
/// The first requests after a quiet spell took 3–7 s while their queries took under 1 s (production, 14 Nov 2026).
/// Opening a connection now shows on the request's line as db.open, and a slow open is logged on its own.
/// </summary>
public class ConnectionTimingInterceptorTests
{
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void An_open_is_added_to_its_request_s_timing()
    {
        var timing = Timing.Begin();
        var log = new ListLogger();

        ConnectionOpenTiming.Record(TimeSpan.FromMilliseconds(3200), log);
        ConnectionOpenTiming.Record(TimeSpan.FromMilliseconds(0.2), log);

        timing.Steps().Should().StartWith("db.open 3200ms×2");
    }

    [Fact]
    public void A_slow_open_is_logged_and_a_fast_one_is_not()
    {
        var log = new ListLogger();
        ConnectionOpenTiming.Record(TimeSpan.FromMilliseconds(40), log);
        ConnectionOpenTiming.Record(TimeSpan.FromMilliseconds(4100), log);

        log.Lines.Should().ContainSingle()
            .Which.Should().Be((LogLevel.Warning, "[DB] slow connection open 4100ms"));
    }

    [Fact]
    public void Outside_a_request_nothing_is_recorded()
        => FluentActions.Invoking(() => ConnectionOpenTiming.Record(TimeSpan.FromSeconds(1), new ListLogger()))
            .Should().NotThrow();
}
