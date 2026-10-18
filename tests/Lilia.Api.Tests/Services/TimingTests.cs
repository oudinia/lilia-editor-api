using FluentAssertions;
using Lilia.Core.Diagnostics;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>Where a request's time goes (18 Oct): steps and queries add to the request that runs them.</summary>
public class TimingTests
{
    [Fact]
    public async Task Steps_and_queries_add_to_the_current_request_slowest_first()
    {
        RequestTiming timing = null!;
        await Task.Run(async () =>
        {
            timing = Timing.Begin();
            // Durations given, not waited for: under the parallel test run a 30 ms wait took 265 ms and
            // the order flipped (18 Oct). Measure() itself is checked below, without timing.
            using (Timing.Measure("pdf.measured")) await Task.Yield();
            Timing.Current!.Add("pdf.project", System.Diagnostics.Stopwatch.Frequency / 20);  // 50 ms
            Timing.Current!.Add("pdf.compile", System.Diagnostics.Stopwatch.Frequency / 5);   // 200 ms
            await Task.Run(() => Timing.Current!.AddDb(TimeSpan.FromMilliseconds(12))); // below, in another task
            Timing.Current!.AddDb(TimeSpan.FromMilliseconds(8));
            Timing.Mark("tex.cache hit");
            Timing.Mark("tex.cache hit");
        });

        timing.DbQueries.Should().Be(2);
        timing.DbMs.Should().BeApproximately(20, 1);
        var steps = timing.Steps();
        steps.Should().StartWith("pdf.compile 200ms").And.Contain("pdf.project 50ms").And.Contain("pdf.measured").And.Contain("tex.cache hit×2");
        steps.IndexOf("pdf.compile").Should().BeLessThan(steps.IndexOf("pdf.project"), "slowest first");
    }

    [Fact]
    public async Task Outside_a_request_measuring_does_nothing()
    {
        await Task.Run(() =>
        {
            Timing.Current.Should().BeNull();
            using (Timing.Measure("anything")) { }
            Timing.Mark("anything");
        });
    }
}
