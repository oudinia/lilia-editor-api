using FluentAssertions;
using Lilia.Api.Services;
using Serilog.Events;
using Serilog.Parsing;

namespace Lilia.Api.Tests.Services;

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
    [InlineData("Lilia.Api.Controllers.LogController")]
    [InlineData("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware")]
    [InlineData("Serilog.AspNetCore.RequestLoggingMiddleware")]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    public void Errors_already_captured_another_way_are_not_sent_twice(string source) =>
        ErrorTrackerLogFilter.IsAlreadyReported(From(source)).Should().BeTrue();
}
