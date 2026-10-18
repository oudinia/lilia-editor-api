using System.Collections.Concurrent;
using System.Diagnostics;

namespace Lilia.Core.Diagnostics;

/// <summary>
/// Where a request's time goes (the user, 18 Oct: "timing logs around many actions to find
/// bottlenecks"). One <see cref="RequestTiming"/> per request, reached from anywhere below it
/// through <see cref="Timing.Current"/>: database queries add themselves (the query interceptor),
/// and the slow steps (render, compile, bibliography, Typst, storage) are measured with
/// <c>using (Timing.Measure("pdf.compile")) { … }</c>. The request log line carries the summary.
/// Outside a request (background work, tests) there is nothing to add to and measuring costs nothing.
/// </summary>
public sealed class RequestTiming
{
    private long _dbCount;
    private long _dbTicks;
    private readonly ConcurrentDictionary<string, (int Count, long Ticks)> _steps = new();

    public long DbQueries => Interlocked.Read(ref _dbCount);
    public double DbMs => Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _dbTicks)).TotalMilliseconds;

    public void AddDb(TimeSpan duration)
    {
        Interlocked.Increment(ref _dbCount);
        Interlocked.Add(ref _dbTicks, (long)(duration.TotalSeconds * Stopwatch.Frequency));
    }

    public void Add(string step, long ticks) =>
        _steps.AddOrUpdate(step, (1, ticks), (_, v) => (v.Count + 1, v.Ticks + ticks));

    /// <summary>"pdf.project 812ms, pdf.compile 1650ms, tex.cache hit×1": slowest first.</summary>
    public string Steps() => string.Join(", ", _steps
        .OrderByDescending(s => s.Value.Ticks)
        .Select(s =>
        {
            var ms = Stopwatch.GetElapsedTime(0, s.Value.Ticks).TotalMilliseconds;
            var times = s.Value.Count > 1 ? $"×{s.Value.Count}" : "";
            return s.Value.Ticks == 0 ? $"{s.Key}{times}" : $"{s.Key} {ms:0}ms{times}";
        }));
}

public static class Timing
{
    private static readonly AsyncLocal<RequestTiming?> CurrentTiming = new();

    public static RequestTiming? Current => CurrentTiming.Value;

    /// <summary>Start timing the current request (the timing middleware, first thing).</summary>
    public static RequestTiming Begin() => CurrentTiming.Value = new RequestTiming();

    /// <summary>Measure a step until the returned scope is disposed.</summary>
    public static Scope Measure(string step) => new(CurrentTiming.Value, step);

    /// <summary>Count an event with no duration ("tex.cache hit").</summary>
    public static void Mark(string step) => CurrentTiming.Value?.Add(step, 0);

    public readonly struct Scope : IDisposable
    {
        private readonly RequestTiming? _timing;
        private readonly string _step;
        private readonly long _start;

        internal Scope(RequestTiming? timing, string step)
        {
            _timing = timing;
            _step = step;
            _start = timing is null ? 0 : Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_timing is not null) _timing.Add(_step, Stopwatch.GetTimestamp() - _start);
        }
    }
}
