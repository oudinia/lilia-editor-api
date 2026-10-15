using Lilia.Api.Events.Common;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Exports.Handlers;

/// <summary>Wolverine handler: builds an "export everything" archive. A failure is recorded on the job.</summary>
public class RunAccountExportHandler
{
    public async Task Handle(RunAccountExportEvent evt, AccountExportService exports, ILogger<RunAccountExportHandler> logger, CancellationToken ct)
    {
        logger.LogInformation("Running account export {JobId}", evt.JobId);
        await exports.RunAsync(evt.JobId, ct);
    }
}
