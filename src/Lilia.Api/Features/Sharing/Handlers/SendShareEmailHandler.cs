using Lilia.Api.Events.Common;
using Lilia.Api.Services;

namespace Lilia.Api.Features.Sharing.Handlers;

/// <summary>
/// Wolverine handler — subscribes to <see cref="DocumentSharedEvent"/>
/// (published by <c>CollaboratorService.AddUserCollaboratorAsync</c>)
/// and emails the recipient. Fire-and-log: a failed email never throws
/// back to the publisher so the original share operation stays
/// successful even when Resend is down.
///
/// Skips silently when the recipient has no email on file (rare —
/// only happens for users created via M2M tokens or pre-Kinde data).
/// </summary>
public class SendShareEmailHandler
{
    public async Task Handle(
        DocumentSharedEvent evt,
        IEmailService email,
        EmailSettings settings,
        ILogger<SendShareEmailHandler> logger)
    {
        if (string.IsNullOrEmpty(evt.SharedWithEmail))
        {
            logger.LogDebug("DocumentSharedEvent: no email on file for {UserId}, skipping share email", evt.SharedWithUserId);
            return;
        }

        var inviterName = string.IsNullOrWhiteSpace(evt.SharedByName) ? "Someone" : evt.SharedByName!;
        var url = $"{settings.BaseUrl}/document/{evt.DocumentId}";

        try
        {
            await email.SendDocumentSharedAsync(
                toEmail: evt.SharedWithEmail,
                recipientName: null,
                inviterName: inviterName,
                documentTitle: evt.DocumentTitle,
                permission: evt.Permission,
                documentUrl: url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendDocumentSharedAsync failed for {Email}", evt.SharedWithEmail);
        }
    }
}
