using System.Security.Claims;
using Lilia.Core.Exceptions;
using Lilia.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Sentry;

namespace Lilia.Api.Filters;

/// <summary>
/// Global exception filter — single place where all exceptions are turned into
/// consistent <see cref="ApiErrorResponse"/> JSON, logged correctly, and optionally
/// forwarded to Sentry.
///
/// Two paths:
///
///  1. <see cref="LiliaException"/> — intentional domain errors thrown from services.
///     Already carry their code, HTTP status, log level, and user message.
///
///  2. Everything else — mapped by <see cref="MapToManagedError"/> which recognises
///     common .NET / EF / HTTP infrastructure exceptions and converts them to a known
///     shape. Truly unknown exceptions fall back to INTERNAL_ERROR.
///
/// Rule: NEVER create a new exception subclass just to get a different HTTP code.
/// If a .NET type already signals the right condition, add a mapping row here instead.
/// New subclasses are only warranted when you need to carry domain-specific data
/// (e.g. a list of validation field errors) that can't be expressed any other way.
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
        var action     = context.RouteData.Values["action"]?.ToString()     ?? "unknown";
        var routeParams = context.RouteData.Values
            .Where(kv => kv.Key != "controller" && kv.Key != "action")
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");

        var correlationId = httpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? "unknown";

        // 1. Intentional domain exceptions — already carry everything we need
        if (context.Exception is LiliaException liliaEx)
        {
            Respond(context, liliaEx.ErrorCode, liliaEx.UserMessage, liliaEx.StatusCode,
                    internalDetails: liliaEx.InternalDetails ?? liliaEx.Message,
                    logLevel: liliaEx.LogLevel,
                    captureSentry: liliaEx.CaptureSentry,
                    exception: liliaEx,
                    userId, controller, action, routeParams, correlationId);
            return;
        }

        // 2. Known .NET / infrastructure exceptions — map to a managed shape
        var mapped = MapToManagedError(context.Exception);
        if (mapped is not null)
        {
            Respond(context, mapped.Code, mapped.UserMessage, mapped.StatusCode,
                    internalDetails: context.Exception.Message,
                    logLevel: mapped.LogLevel,
                    captureSentry: mapped.CaptureSentry,
                    exception: context.Exception,
                    userId, controller, action, routeParams, correlationId);
            return;
        }

        // 3. Truly unknown — log everything, return safe 500
        Respond(context, LiliaErrorCodes.InternalError,
                "An unexpected error occurred. Please try again or contact support.",
                statusCode: 500,
                internalDetails: context.Exception.ToString(),
                logLevel: LogLevel.Error,
                captureSentry: true,
                exception: context.Exception,
                userId, controller, action, routeParams, correlationId);
    }

    // ─── Mapping table ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maps common .NET / EF Core / HttpClient exception types to a managed error shape.
    /// Returns null for exceptions that should fall through to the INTERNAL_ERROR path.
    /// Add rows here; don't add new LiliaException subclasses.
    /// </summary>
    private static MappedError? MapToManagedError(Exception ex) => ex switch
    {
        // ── Cancellation / timeout ──────────────────────────────────────────
        OperationCanceledException or TaskCanceledException
            => new(499, LiliaErrorCodes.InternalError,
                   "The request was cancelled.",
                   LogLevel.Information, CaptureSentry: false),

        TimeoutException
            => new(504, LiliaErrorCodes.ExternalServiceError,
                   "The request timed out. Please try again.",
                   LogLevel.Warning, CaptureSentry: true),

        // ── Validation / bad input ──────────────────────────────────────────
        ArgumentNullException or ArgumentException or ArgumentOutOfRangeException
            => new(400, LiliaErrorCodes.ValidationFailed,
                   "Invalid request parameters.",
                   LogLevel.Warning, CaptureSentry: false),

        // ── IO / file system ────────────────────────────────────────────────
        FileNotFoundException
            => new(404, LiliaErrorCodes.NotFound,
                   "A required file was not found.",
                   LogLevel.Warning, CaptureSentry: false),

        IOException
            => new(500, LiliaErrorCodes.InternalError,
                   "A file system error occurred. Please try again.",
                   LogLevel.Error, CaptureSentry: true),

        // ── HTTP client (external API calls) ───────────────────────────────
        HttpRequestException httpEx when (int)(httpEx.StatusCode ?? 0) == 401
            => new(502, LiliaErrorCodes.ExternalServiceError,
                   "An external service rejected our request. Please contact support.",
                   LogLevel.Error, CaptureSentry: true),

        HttpRequestException httpEx when (int)(httpEx.StatusCode ?? 0) == 429
            => new(503, LiliaErrorCodes.RateLimited,
                   "An external service is rate-limiting requests. Please try again in a few minutes.",
                   LogLevel.Warning, CaptureSentry: true),

        HttpRequestException
            => new(502, LiliaErrorCodes.ExternalServiceError,
                   "An external service is temporarily unavailable. Please try again.",
                   LogLevel.Error, CaptureSentry: true),

        // ── Entity Framework / database ─────────────────────────────────────
        DbUpdateConcurrencyException
            => new(409, LiliaErrorCodes.Conflict,
                   "The resource was modified by another request. Please reload and try again.",
                   LogLevel.Warning, CaptureSentry: false),

        DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx)
            => new(409, LiliaErrorCodes.Conflict,
                   "A resource with this identifier already exists.",
                   LogLevel.Warning, CaptureSentry: false),

        DbUpdateException
            => new(500, LiliaErrorCodes.InternalError,
                   "A database error occurred. Please try again.",
                   LogLevel.Error, CaptureSentry: true),

        // ── Access / security ────────────────────────────────────────────────
        UnauthorizedAccessException
            => new(403, LiliaErrorCodes.Forbidden,
                   "You do not have permission to perform this action.",
                   LogLevel.Warning, CaptureSentry: false),

        // ── Out-of-range / overflow (likely a bug) ───────────────────────────
        OverflowException or IndexOutOfRangeException or InvalidCastException
            => new(500, LiliaErrorCodes.InternalError,
                   "An unexpected error occurred. Please try again.",
                   LogLevel.Error, CaptureSentry: true),

        _ => null
    };

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // PostgreSQL error code 23505 = unique_violation
        var inner = ex.InnerException?.Message ?? "";
        return inner.Contains("23505") || inner.Contains("unique constraint") || inner.Contains("duplicate key");
    }

    // ─── Response writer ───────────────────────────────────────────────────────

    private void Respond(
        ExceptionContext context,
        string code, string userMessage, int statusCode,
        string internalDetails, LogLevel logLevel, bool captureSentry,
        Exception exception,
        string userId, string controller, string action,
        Dictionary<string, string> routeParams, string correlationId)
    {
        var request = context.HttpContext.Request;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["ErrorCode"]     = code,
            ["UserId"]        = userId,
            ["Controller"]    = controller,
            ["Action"]        = action,
            ["Method"]        = request.Method,
            ["Path"]          = request.Path.Value,
            ["CorrelationId"] = correlationId,
        }))
        {
            _logger.Log(logLevel, exception,
                "[{ErrorCode}] {Controller}.{Action} [{Method} {Path}] user={UserId} — {InternalDetails}",
                code, controller, action, request.Method, request.Path.Value, userId, internalDetails);
        }

        if (captureSentry)
        {
            SentrySdk.CaptureException(exception, scope =>
            {
                scope.SetTag("error.code", code);
                scope.SetTag("controller", controller);
                scope.SetTag("action", action);
                scope.SetTag("http.method", request.Method);
                scope.User = new SentryUser { Id = userId };
                foreach (var (key, value) in routeParams)
                    scope.SetExtra($"route.{key}", value);
                if (request.QueryString.HasValue)
                    scope.SetExtra("queryString", request.QueryString.Value);
            });
        }

        context.Result = new ObjectResult(new ApiErrorResponse(
            Code: code,
            Message: userMessage,
            StatusCode: statusCode,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow
        )) { StatusCode = statusCode };

        context.ExceptionHandled = true;
    }

    // ─── Internal mapping record ───────────────────────────────────────────────

    private sealed record MappedError(
        int StatusCode,
        string Code,
        string UserMessage,
        LogLevel LogLevel,
        bool CaptureSentry);
}
