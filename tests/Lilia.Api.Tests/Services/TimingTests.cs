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
            using (Timing.Measure("pdf.project")) await Task.Delay(30);
            using (Timing.Measure("pdf.compile")) await Task.Delay(80);
            await Task.Run(() => Timing.Current!.AddDb(TimeSpan.FromMilliseconds(12))); // below, in another task
            Timing.Current!.AddDb(TimeSpan.FromMilliseconds(8));
            Timing.Mark("tex.cache hit");
            Timing.Mark("tex.cache hit");
        });

        timing.DbQueries.Should().Be(2);
        timing.DbMs.Should().BeApproximately(20, 1);
        var steps = timing.Steps();
        steps.Should().StartWith("pdf.compile ").And.Contain("pdf.project ").And.Contain("tex.cache hit×2");
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
