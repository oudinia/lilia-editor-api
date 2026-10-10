using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// The sharing events beyond "shared with you" (Olivia, 10 Oct,
/// design-handoffs/2026-10-10-sharing-notifications): an invite accepted (to the
/// owner), a role changed and access removed (to the person). Each is an in-app
/// notification and an email Lilia writes and sends itself.
///
/// Fire-and-log: a failed notification or email never fails the sharing
/// operation that caused it.
/// </summary>
public interface ISharingNotifier
{
    Task InviteAcceptedAsync(Guid documentId, string inviterUserId, string acceptedUserId);
    Task RoleChangedAsync(Guid documentId, string targetUserId, string changedByUserId, string oldRole, string newRole);
    Task AccessRemovedAsync(Guid documentId, string targetUserId, string removedByUserId, bool allowReply = false);
}

public sealed class SharingNotifier : ISharingNotifier
{
    public const string InviteAccepted = "invite_accepted";
    public const string RoleChanged = "role_changed";
    public const string AccessRemoved = "access_removed";

    /// <summary>Several accepts for one document within this window: one email, every notification.</summary>
    public static readonly TimeSpan AcceptEmailWindow = TimeSpan.FromHours(1);

    private readonly LiliaDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IEmailService _email;
    private readonly EmailSettings _settings;
    private readonly ILogger<SharingNotifier> _logger;

    public SharingNotifier(LiliaDbContext db, INotificationService notifications, IEmailService email, EmailSettings settings, ILogger<SharingNotifier> logger)
    {
        _db = db;
        _notifications = notifications;
        _email = email;
        _settings = settings;
        _logger = logger;
    }

    public async Task InviteAcceptedAsync(Guid documentId, string inviterUserId, string acceptedUserId)
    {
        try
        {
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
            var owner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == inviterUserId);
            var joined = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == acceptedUserId);
            if (doc is null || owner is null) return;
            var who = Name(joined?.Name, joined?.Email);
            var link = $"/d/{documentId}?share=open&flash={Uri.EscapeDataString(acceptedUserId)}";

            // The email folds: one per document per hour, however many join.
            var since = DateTime.UtcNow - AcceptEmailWindow;
            var recentlyEmailed = await _db.Notifications.AsNoTracking().AnyAsync(n =>
                n.UserId == inviterUserId && n.Type == InviteAccepted && n.Link != null
                && n.Link.StartsWith($"/d/{documentId}?") && n.CreatedAt >= since);

            await _notifications.CreateAsync(inviterUserId, InviteAccepted,
                $"{who} accepted your invitation to \"{doc.Title}\"", "They can open it now.", link);

            if (!recentlyEmailed && !string.IsNullOrEmpty(owner.Email))
                await _email.SendInviteAcceptedAsync(owner.Email, who, doc.Title, $"{_settings.BaseUrl}{link}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invite-accepted notice failed for {DocumentId}", documentId);
        }
    }

    public async Task RoleChangedAsync(Guid documentId, string targetUserId, string changedByUserId, string oldRole, string newRole)
    {
        if (string.Equals(RoleNames.Normalize(oldRole), RoleNames.Normalize(newRole), StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
            var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId);
            var by = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == changedByUserId);
            if (doc is null || target is null) return;
            var who = Name(by?.Name, by?.Email);
            var link = $"/d/{documentId}";
            await _notifications.CreateAsync(targetUserId, RoleChanged,
                $"{who} changed your access to \"{doc.Title}\"", $"{RoleWords(oldRole)} → {RoleWords(newRole)}", link);
            if (!string.IsNullOrEmpty(target.Email))
                await _email.SendAccessChangedAsync(target.Email, who, doc.Title, RoleWords(oldRole), RoleWords(newRole), $"{_settings.BaseUrl}{link}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Role-changed notice failed for {DocumentId}", documentId);
        }
    }

    public async Task AccessRemovedAsync(Guid documentId, string targetUserId, string removedByUserId, bool allowReply = false)
    {
        try
        {
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId);
            var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId);
            var by = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == removedByUserId);
            if (doc is null || target is null) return;
            var who = Name(by?.Name, by?.Email);
            // Nothing to open: the row says who removed you.
            await _notifications.CreateAsync(targetUserId, AccessRemoved,
                $"{who} removed your access to \"{doc.Title}\"", null, null);
            if (!string.IsNullOrEmpty(target.Email))
                await _email.SendAccessRemovedAsync(target.Email, who, doc.Title, allowReply ? by?.Email : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Access-removed notice failed for {DocumentId}", documentId);
        }
    }

    private static string Name(string? name, string? email) =>
        !string.IsNullOrWhiteSpace(name) ? name! : !string.IsNullOrWhiteSpace(email) ? email! : "Someone";

    /// <summary>The words the share drawer uses: Can view, Can edit, Owner.</summary>
    public static string RoleWords(string role) => RoleNames.Normalize(role) switch
    {
        "owner" => "Owner",
        "editor" => "Can edit",
        "viewer" => "Can view",
        var other => other,
    };
}
