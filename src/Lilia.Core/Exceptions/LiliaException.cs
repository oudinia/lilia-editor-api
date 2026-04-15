using Microsoft.Extensions.Logging;

namespace Lilia.Core.Exceptions;

/// <summary>
/// Base class for all domain exceptions in the Lilia platform.
///
/// Carrying contract:
///   - ErrorCode      → stable machine-readable string (maps to i18n key on the frontend)
///   - StatusCode     → HTTP status code to return to the client
///   - UserMessage    → safe, non-technical message — OK to send to clients
///   - InternalDetails → extra context for logs only — NEVER sent to clients
///   - LogLevel       → how urgently this should be logged
///   - CaptureSentry  → whether to forward to Sentry (true for system faults, false for user errors)
/// </summary>
public class LiliaException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }
    public string UserMessage { get; }
    public string? InternalDetails { get; init; }
    public LogLevel LogLevel { get; init; } = LogLevel.Error;
    public bool CaptureSentry { get; init; } = true;

    public LiliaException(
        string errorCode,
        string userMessage,
        int statusCode = 500,
        string? internalDetails = null,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
        StatusCode = statusCode;
        InternalDetails = internalDetails;
    }
}

/// <summary>400 — invalid input from the caller.</summary>
public class LiliaValidationException : LiliaException
{
    public LiliaValidationException(
        string userMessage,
        string errorCode = LiliaErrorCodes.ValidationFailed,
        string? internalDetails = null)
        : base(errorCode, userMessage, 400, internalDetails)
    {
        LogLevel = LogLevel.Warning;
        CaptureSentry = false;
    }
}

/// <summary>404 — requested resource does not exist or is not visible to the caller.</summary>
public class LiliaNotFoundException : LiliaException
{
    public LiliaNotFoundException(
        string userMessage,
        string errorCode = LiliaErrorCodes.NotFound,
        string? internalDetails = null)
        : base(errorCode, userMessage, 404, internalDetails)
    {
        LogLevel = LogLevel.Warning;
        CaptureSentry = false;
    }
}

/// <summary>403 — caller is authenticated but lacks permission.</summary>
public class LiliaForbiddenException : LiliaException
{
    public LiliaForbiddenException(
        string userMessage,
        string errorCode = LiliaErrorCodes.Forbidden,
        string? internalDetails = null)
        : base(errorCode, userMessage, 403, internalDetails)
    {
        LogLevel = LogLevel.Warning;
        CaptureSentry = false;
    }
}

/// <summary>409 — state conflict (e.g. session already finalized, collaborator already exists).</summary>
public class LiliaConflictException : LiliaException
{
    public LiliaConflictException(
        string userMessage,
        string errorCode = LiliaErrorCodes.Conflict,
        string? internalDetails = null)
        : base(errorCode, userMessage, 409, internalDetails)
    {
        LogLevel = LogLevel.Warning;
        CaptureSentry = false;
    }
}

/// <summary>
/// 502/503 — a third-party API call failed.
/// Use for Mathpix, MinerU, bibliography lookups, storage, AI providers, etc.
/// <c>InternalDetails</c> may include HTTP status / response body — safe to log, not to send to clients.
/// </summary>
public class LiliaExternalServiceException : LiliaException
{
    public string ServiceName { get; }

    public LiliaExternalServiceException(
        string serviceName,
        string errorCode,
        string userMessage,
        int statusCode = 502,
        string? internalDetails = null,
        Exception? innerException = null)
        : base(errorCode, userMessage, statusCode, internalDetails, innerException)
    {
        ServiceName = serviceName;
        LogLevel = LogLevel.Error;
        CaptureSentry = true;
    }
}

/// <summary>
/// 503 — a required service or integration is not configured (missing API keys, etc.).
/// These are operator errors — always captured in Sentry at Critical level.
/// </summary>
public class LiliaConfigurationException : LiliaException
{
    public LiliaConfigurationException(
        string errorCode,
        string userMessage,
        string? internalDetails = null)
        : base(errorCode, userMessage, 503, internalDetails)
    {
        LogLevel = LogLevel.Critical;
        CaptureSentry = true;
    }
}
