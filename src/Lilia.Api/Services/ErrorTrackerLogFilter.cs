using Serilog.Events;

namespace Lilia.Api.Services;

/// <summary>
/// Which logged errors go to the error tracker (Bugsink) through the Serilog sink.
///
/// <para>Serilog replaces ASP.NET's logger providers, so Sentry's own logger never saw a logged
/// error: only exceptions captured explicitly reached Bugsink. Errors outside a request (Wolverine's
/// durability agent, the background workers) were in the log file and nowhere else; 17 Oct 2026,
/// Bugsink held three setup probes while the log held ~300 errors a day.</para>
///
/// <para>Left out, because they already arrive another way and would count twice:</para>
/// <list type="bullet">
/// <item>ExceptionLoggingFilter: captures its exceptions itself, with tags.</item>
/// <item>ASP.NET's exception handler and the request log ("responded 500"): Sentry's middleware
/// captures the request's exception.</item>
/// <item>EF's "Failed executing DbCommand": the same exception is logged again by whoever ran the
/// query, with the stack that matters.</item>
/// <item>LogController (CLIENT …): browser errors, which the browser sends to its own project.</item>
/// </list>
/// </summary>
public static class ErrorTrackerLogFilter
{
    private static readonly string[] AlreadyReportedSources =
    [
        "Lilia.Api.Filters.ExceptionLoggingFilter",
        "Lilia.Api.Controllers.LogController",
        "Microsoft.AspNetCore.Diagnostics",
        "Serilog.AspNetCore.RequestLoggingMiddleware",
        "Microsoft.EntityFrameworkCore.Database.Command",
    ];

    public static bool IsAlreadyReported(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var value)
        && value is ScalarValue { Value: string source }
        && AlreadyReportedSources.Any(s => source == s || source.StartsWith(s + ".", StringComparison.Ordinal));
}
