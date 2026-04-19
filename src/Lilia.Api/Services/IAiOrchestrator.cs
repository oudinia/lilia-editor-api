namespace Lilia.Api.Services;

/// <summary>
/// Single entry point for new AI features. Wraps a provider call with:
///   1. Opt-in gate — refuses unless the document (or owner) has AI enabled.
///   2. Redaction — PII is stripped from the prompt before it leaves the box.
///   3. Rate limiting — per-user budget, enforced pre-call.
///   4. Audit — every call writes an <see cref="Lilia.Core.Entities.AiRequest"/> row.
///
/// Controllers should call this, not a provider SDK directly. If a new
/// feature needs raw provider access, add the guarantee here first.
/// </summary>
public interface IAiOrchestrator
{
    Task<AiOrchestratorResult> RunAsync(AiOrchestratorRequest request, CancellationToken ct = default);
}

public record AiOrchestratorRequest(
    string UserId,
    Guid? DocumentId,
    string? BlockId,
    string Purpose,            // matches ai_requests.purpose CHECK vocabulary
    string UserPrompt,
    string? SystemPrompt = null,
    string Model = "claude-opus-4-7",
    int MaxTokens = 1024,
    double Temperature = 0.2);

public record AiOrchestratorResult(
    Guid AiRequestId,
    string Status,             // success | redacted_refused | error | rate_limited | disabled
    string? Text,
    string? ErrorMessage,
    int PromptTokens,
    int CompletionTokens,
    int TotalRedactions);
