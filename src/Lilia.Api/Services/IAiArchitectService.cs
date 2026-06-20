using Lilia.Api.Models.AiArchitect;

namespace Lilia.Api.Services;

/// <summary>
/// Hosted AI Document-Architect. Converses with the author about a document's
/// structure and returns proposed typed-block operations. Read-only with
/// respect to the document; metering is persisted to the credit ledger.
/// </summary>
public interface IAiArchitectService
{
    Task<AiArchitectOutcome> ArchitectAsync(
        string userId, AiArchitectRequest request, CancellationToken ct = default);
}

/// <summary>
/// Discriminated result: either a successful 200 payload or a gated 403 lock.
/// The controller maps this to the HTTP response without needing to know the
/// gating rules.
/// </summary>
public class AiArchitectOutcome
{
    public bool IsLocked { get; private init; }
    public AiArchitectResponse? Response { get; private init; }
    public AiArchitectLocked? Lock_ { get; private init; }

    public static AiArchitectOutcome Ok(AiArchitectResponse response)
        => new() { IsLocked = false, Response = response };

    public static AiArchitectOutcome Lock(string reason, string message)
        => new() { IsLocked = true, Lock_ = new AiArchitectLocked(true, reason, message) };
}
