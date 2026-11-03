using Lilia.Api.Events.Common;
using Lilia.Api.Directories;
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
        IPeopleDirectory directory,
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
            // The recipient has an account: their language, else the sharer's.
            var people = await directory.FindManyAsync([evt.SharedWithUserId, evt.SharedByUserId]);
            var to = people.GetValueOrDefault(evt.SharedWithUserId);
            var by = people.GetValueOrDefault(evt.SharedByUserId);
            await email.SendDocumentSharedAsync(
                toEmail: evt.SharedWithEmail,
                inviter: new EmailActor(inviterName, by?.Email),
                documentTitle: evt.DocumentTitle,
                role: evt.Permission,
                documentUrl: url,
                locale: to?.Locale ?? by?.Locale);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendDocumentSharedAsync failed for {Email}", evt.SharedWithEmail);
        }
    }
}
