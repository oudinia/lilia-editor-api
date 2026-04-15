namespace Lilia.Api.Models;

/// <summary>
/// Canonical error response shape returned by all API error paths.
///
/// The frontend uses <c>code</c> as an i18n key to show a translated message.
/// <c>message</c> is a safe English fallback — never contains internals.
/// </summary>
public record ApiErrorResponse(
    string Code,
    string Message,
    int StatusCode,
    string CorrelationId,
    DateTimeOffset Timestamp
)
{
    /// <summary>Optional field-level validation errors (key → message).</summary>
    public Dictionary<string, string[]>? Errors { get; init; }
}
