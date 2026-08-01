using Lilia.Api.Events.Common;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Versioning.Handlers;

/// <summary>
/// Wolverine handler — takes an auto-version snapshot when one is due.
///
/// <para>Whether one is due is <c>VersionService</c>'s decision, not this
/// handler's: it throttles to one per document per five minutes, so most
/// deliveries are a single query and a return. Duplicating that check here
/// would put the same rule in two places and let them drift.</para>
///
/// <para><b>Failures are swallowed.</b> Auto-versioning is a convenience — the
/// next edit produces another chance within minutes, and no user action depends
/// on it. That makes it the same call as compilation telemetry and the opposite
/// of the import job, where a failure loses a document the author believes they
/// uploaded. Logged at warning so a persistent failure is still visible.</para>
/// </summary>
public class AutoVersionHandler
{
    public async Task Handle(
        DocumentEditedEvent evt,
        IVersionService versions,
        ILogger<AutoVersionHandler> logger)
    {
        try
        {
            await versions.CreateAutoVersionAsync(evt.DocumentId, evt.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Auto-version failed for document {DocumentId} (non-critical)", evt.DocumentId);
        }
    }
}
