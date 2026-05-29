using System.Text.Json;

namespace Lilia.Core.DTOs;

/// <summary>
/// Inbound payload — what the client posts.
/// </summary>
public record CreateDiagnosticCaptureDto(
    /// <summary>Surface tag — e.g. "math-editor". Free-form.</summary>
    string Source,
    /// <summary>Short repro note from the user.</summary>
    string? Note,
    /// <summary>Browser user-agent at capture time.</summary>
    string? UserAgent,
    /// <summary>URL at capture time.</summary>
    string? Url,
    /// <summary>The diagnostic bundle as opaque JSON.</summary>
    JsonElement Payload
);

/// <summary>
/// Light response returned from POST. Hands back the ref token the
/// user pastes into chat or a ticket.
/// </summary>
public record DiagnosticCaptureCreatedDto(
    Guid Id,
    string RefToken,
    DateTime CreatedAt
);

/// <summary>
/// Full capture record returned from GET. The payload is rehydrated
/// from the jsonb column.
/// </summary>
public record DiagnosticCaptureDto(
    Guid Id,
    string? UserId,
    string RefToken,
    string Source,
    string? Note,
    string? UserAgent,
    string? Url,
    JsonElement Payload,
    DateTime CreatedAt
);
