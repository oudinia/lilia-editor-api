using Lilia.Api.Events.Common;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Imports.Handlers;

/// <summary>
/// Wolverine handler — runs a queued LaTeX import.
///
/// <para>The executor body is unchanged; this only moves <i>when</i> and
/// <i>where</i> it runs. That was the point of choosing a real message library
/// early: the work becomes durable and retryable without the code that does it
/// being rewritten.</para>
///
/// <para><b>Exceptions are allowed to escape.</b> This is the opposite decision
/// from the compilation-telemetry handler, and deliberately so: a lost telemetry
/// row is a rounding error, while a lost import is a document the author
/// believes they uploaded. Letting it throw is what engages the retry the
/// <c>Job</c> row has always advertised — and the executor's idempotency guard
/// is what makes a retry safe to take.</para>
/// </summary>
public class RunImportJobHandler
{
    public async Task Handle(
        RunImportJobEvent evt,
        ILatexImportJobExecutor executor,
        ILogger<RunImportJobHandler> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Running import job {JobId} for session {SessionId}", evt.JobId, evt.SessionId);

        await executor.RunAsync(evt.JobId, evt.SessionId, ct);
    }
}
