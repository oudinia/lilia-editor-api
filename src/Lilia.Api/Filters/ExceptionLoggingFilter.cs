using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Sentry;

namespace Lilia.Api.Filters;

/// <summary>
/// Global exception filter that logs all unhandled controller exceptions
/// with full request context (user, route, params) and forwards to Sentry.
/// Applied globally via AddControllers — no per-controller try/catch needed.
/// </summary>
public class ExceptionLoggingFilter : IExceptionFilter
{
    private readonly ILogger<ExceptionLoggingFilter> _logger;

    public ExceptionLoggingFilter(ILogger<ExceptionLoggingFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;

        // Extract user identity
        var userId = httpContext.User?.FindFirst("sub")?.Value
                  ?? httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? "anonymous";

        // Extract route values for context (documentId, blockId, etc.)
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "unknown";
        var action = context.RouteData.Values["action"]?.ToString() ?? "unknown";
        var routeParams = context.RouteData.Values
            .Where(kv => kv.Key != "controller" && kv.Key != "action")
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");

        // Build a structured log with all context
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = userId,
            ["Controller"] = controller,
            ["Action"] = action,
            ["Method"] = request.Method,
            ["Path"] = request.Path.Value,
            ["QueryString"] = request.QueryString.Value,
            ["RouteParams"] = routeParams,
            ["ContentType"] = request.ContentType,
            ["ContentLength"] = request.ContentLength,
        }))
        {
            _logger.LogError(
                context.Exception,
                "Unhandled exception in {Controller}.{Action} [{Method} {Path}] for user {UserId}",
                controller, action, request.Method, request.Path.Value, userId);
        }

        // Forward to Sentry with enriched context
        SentrySdk.CaptureException(context.Exception, scope =>
        {
            scope.SetTag("controller", controller);
            scope.SetTag("action", action);
            scope.SetTag("http.method", request.Method);
            scope.User = new SentryUser { Id = userId };

            foreach (var (key, value) in routeParams)
            {
                scope.SetExtra($"route.{key}", value);
            }

            if (request.QueryString.HasValue)
            {
                scope.SetExtra("queryString", request.QueryString.Value);
            }
        });

        // Don't mark as handled — let ASP.NET's UseExceptionHandler produce the response
    }
}
