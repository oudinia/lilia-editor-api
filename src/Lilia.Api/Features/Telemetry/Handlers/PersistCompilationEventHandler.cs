using Lilia.Api.Events.Common;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;

namespace Lilia.Api.Features.Telemetry.Handlers;

/// <summary>
/// Wolverine handler — writes one <see cref="LaTeXCompilationEvent"/> row per
/// compile.
///
/// <para><b>Why the DbContext parameter matters.</b> Wolverine resolves it from
/// a scope it owns, per message. The code this replaces had to build that scope
/// by hand (<c>_scopeFactory.CreateScope()</c>) precisely because reusing the
/// request-scoped context raced with the request's own save. Here the isolation
/// is a property of how the message is dispatched rather than something the
/// caller has to remember, which is the actual improvement — the old comment
/// explaining the race can go, because the race cannot recur.</para>
///
/// <para><b>Failures are swallowed on purpose.</b> This is telemetry attached to
/// a user-facing compile: if recording the outcome fails, the author has already
/// been given their answer and nothing about their document is wrong. Logged at
/// warning so it is visible without being alarming. Note this differs from the
/// old behaviour only in where the try/catch sits — it was fire-and-forget
/// before too, just without a queue's retry or error handling around it.</para>
/// </summary>
public class PersistCompilationEventHandler
{
    public async Task Handle(
        CompilationRecordedEvent evt,
        LiliaDbContext db,
        ILogger<PersistCompilationEventHandler> logger,
        CancellationToken ct)
    {
        try
        {
            db.LaTeXCompilationEvents.Add(ToEntity(evt));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to persist LaTeXCompilationEvent for {EventType} (non-critical)",
                evt.EventType);
        }
    }

    /// <summary>
    /// Event to row. Pulled out of <see cref="Handle"/> so it can be tested
    /// without a database — which here is not a preference but a necessity: the
    /// EF in-memory provider cannot map this context at all, because
    /// <c>AiChat.Messages</c> is a <c>JsonDocument</c> and the provider has no
    /// mapping for it. Model validation then throws on first DbSet access. (The
    /// other in-memory tests in this suite pass only because they construct a
    /// context and never persist through it.)
    ///
    /// Field-by-field copying is exactly the kind of code where a wrong or
    /// omitted line is invisible — telemetry nobody reads synchronously — so it
    /// is worth asserting directly.
    /// </summary>
    public static LaTeXCompilationEvent ToEntity(CompilationRecordedEvent evt) => new()
    {
        DocumentId = evt.DocumentId,
        BlockId = evt.BlockId,
        BlockType = evt.BlockType,
        EventType = evt.EventType,
        Success = evt.Success,
        ErrorRaw = evt.ErrorRaw,
        ErrorCategory = evt.ErrorCategory,
        ErrorToken = evt.ErrorToken,
        ErrorLine = evt.ErrorLine,
        WarningCount = evt.WarningCount,
        DurationMs = evt.DurationMs,
        UserId = evt.UserId,
    };
}
