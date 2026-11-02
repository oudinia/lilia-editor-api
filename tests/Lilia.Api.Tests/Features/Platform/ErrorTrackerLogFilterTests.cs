using FluentAssertions;
using Lilia.Api.Features.Platform.Services;
using Serilog.Events;
using Serilog.Parsing;
using Lilia.Api.Features.Platform.Controllers;

namespace Lilia.Api.Tests.Features.Platform;

/// <summary>
/// Logged errors from Wolverine and the background workers go to Bugsink; those already captured
/// another way (the exception filter, Sentry's request middleware, the browser) do not, so nothing
/// counts twice.
/// </summary>
public class ErrorTrackerLogFilterTests
{
    private static LogEvent From(string? source)
    {
        var props = source is null
            ? Array.Empty<LogEventProperty>()
            : new[] { new LogEventProperty("SourceContext", new ScalarValue(source)) };
        return new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, null,
            new MessageTemplateParser().Parse("x"), props);
    }

    [Theory]
    [InlineData("Wolverine.RDBMS.DurabilityAgent")]
    [InlineData("Lilia.Api.Services.BlockShapeWorker")]
    [InlineData("Microsoft.EntityFrameworkCore.Query")]
    [InlineData("ModelContextProtocol.Server.McpServer")]
    [InlineData(null)]
    public void Errors_with_no_other_way_to_Bugsink_are_sent(string? source) =>
        ErrorTrackerLogFilter.IsAlreadyReported(From(source)).Should().BeFalse();

    [Theory]
    [InlineData("Lilia.Api.Filters.ExceptionLoggingFilter")]
    [InlineData("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware")]
    [InlineData("Serilog.AspNetCore.RequestLoggingMiddleware")]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    public void Errors_already_captured_another_way_are_not_sent_twice(string source) =>
        ErrorTrackerLogFilter.IsAlreadyReported(From(source)).Should().BeTrue();

    // The browser's errors: by the controller's type, so the filter follows it when it moves (2 Nov,
    // LogController went to Features/Platform and its old name in a string would have matched nothing).
    [Fact]
    public void Browser_errors_sent_through_the_log_controller_are_not_sent_twice() =>
        ErrorTrackerLogFilter.IsAlreadyReported(From(typeof(LogController).FullName)).Should().BeTrue();
}
