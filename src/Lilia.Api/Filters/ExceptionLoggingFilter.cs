using System.Security.Claims;
using Lilia.Core.Exceptions;
using Lilia.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sentry;

namespace Lilia.Api.Filters;

/// <summary>
/// Global exception filter that:
///  - Converts <see cref="LiliaException"/> subtypes into structured JSON responses
///    with stable error codes and safe user-facing messages.
///  - Logs at the appropriate level (Warning for user errors, Error/Critical for system faults).
///  - Forwards system faults to Sentry with full request context.
///  - Never leaks internal details (stack traces, DB errors) to the client.
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

        var userId = httpContext.User?.FindFirst("sub")?.Value
                  ?? httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? "anonymous";

        var controller = context.RouteData.Values["controller"]?.ToString() ?? "unknown";
        var action = context.RouteData.Values["action"]?.ToString() ?? "unknown";
        var routeParams = context.RouteData.Values
            .Where(kv => kv.Key != "controller" && kv.Key != "action")
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");

        var correlationId = httpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? "unknown";

        if (context.Exception is LiliaException liliaEx)
        {
            HandleLiliaException(context, liliaEx, userId, controller, action, routeParams, correlationId);
        }
        else
        {
            HandleUnexpectedException(context, userId, controller, action, routeParams, correlationId);
        }
    }

    private void HandleLiliaException(
        ExceptionContext context,
        LiliaException ex,
        string userId, string controller, string action,
        Dictionary<string, string> routeParams,
        string correlationId)
    {
        var request = context.HttpContext.Request;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["ErrorCode"] = ex.ErrorCode,
            ["UserId"] = userId,
            ["Controller"] = controller,
            ["Action"] = action,
            ["Method"] = request.Method,
            ["Path"] = request.Path.Value,
            ["CorrelationId"] = correlationId,
        }))
        {
            // Log with the level the exception declares
            var logMessage = "Lilia error [{ErrorCode}] in {Controller}.{Action} [{Method} {Path}] for user {UserId}: {InternalDetails}";
            var internalDetails = ex.InternalDetails ?? ex.Message;

            _logger.Log(ex.LogLevel, ex,
                logMessage,
                ex.ErrorCode, controller, action, request.Method, request.Path.Value, userId, internalDetails);
        }

        if (ex.CaptureSentry)
        {
            SentrySdk.CaptureException(ex, scope =>
            {
                scope.SetTag("error.code", ex.ErrorCode);
                scope.SetTag("controller", controller);
                scope.SetTag("action", action);
                scope.SetTag("http.method", request.Method);
                scope.User = new SentryUser { Id = userId };
                foreach (var (key, value) in routeParams)
                    scope.SetExtra($"route.{key}", value);
            });
        }

        var response = new ApiErrorResponse(
            Code: ex.ErrorCode,
            Message: ex.UserMessage,
            StatusCode: ex.StatusCode,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow
        );

        context.Result = new ObjectResult(response) { StatusCode = ex.StatusCode };
        context.ExceptionHandled = true;
    }

    private void HandleUnexpectedException(
        ExceptionContext context,
        string userId, string controller, string action,
        Dictionary<string, string> routeParams,
        string correlationId)
    {
        var request = context.HttpContext.Request;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = userId,
            ["Controller"] = controller,
            ["Action"] = action,
            ["Method"] = request.Method,
            ["Path"] = request.Path.Value,
            ["QueryString"] = request.QueryString.Value,
            ["RouteParams"] = routeParams,
            ["CorrelationId"] = correlationId,
        }))
        {
            _logger.LogError(
                context.Exception,
                "Unhandled exception in {Controller}.{Action} [{Method} {Path}] for user {UserId}",
                controller, action, request.Method, request.Path.Value, userId);
        }

        SentrySdk.CaptureException(context.Exception, scope =>
        {
            scope.SetTag("controller", controller);
            scope.SetTag("action", action);
            scope.SetTag("http.method", request.Method);
            scope.User = new SentryUser { Id = userId };
            foreach (var (key, value) in routeParams)
                scope.SetExtra($"route.{key}", value);
            if (request.QueryString.HasValue)
                scope.SetExtra("queryString", request.QueryString.Value);
        });

        var response = new ApiErrorResponse(
            Code: LiliaErrorCodes.InternalError,
            Message: "An unexpected error occurred. Please try again or contact support.",
            StatusCode: 500,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow
        );

        context.Result = new ObjectResult(response) { StatusCode = 500 };
        context.ExceptionHandled = true;
    }
}
