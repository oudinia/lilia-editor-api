using Lilia.Api.Events.Common;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Exports.Handlers;

/// <summary>
/// Wolverine handler — produces a queued document export.
///
/// <para><b>Exceptions escape.</b> Same call as the import handler and the
/// opposite of telemetry: an export is something the author asked for and is
/// waiting on, so a failure should engage the retry the <c>Job</c> row has always
/// advertised rather than be logged and forgotten. <c>RunExportJobAsync</c>
/// records FAILED with the message before rethrowing, so the row stays truthful
/// either way — and its idempotency guard is what makes the retry safe.</para>
/// </summary>
public class RunExportJobHandler
{
    public async Task Handle(
        RunExportJobEvent evt,
        IJobService jobs,
        ILogger<RunExportJobHandler> logger,
        CancellationToken ct)
    {
        logger.LogInformation("Running export job {JobId}", evt.JobId);
        await jobs.RunExportJobAsync(evt.JobId, ct);
    }
}
