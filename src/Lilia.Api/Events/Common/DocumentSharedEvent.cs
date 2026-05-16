namespace Lilia.Api.Events.Common;

/// <summary>
/// Published when a user is added as a collaborator on a document via
/// the by-id path (<c>CollaboratorService.AddUserCollaboratorAsync</c>).
/// The email-invite path (<c>InviteByEmailAsync</c>) already sends its
/// own email synchronously and does NOT publish this event — yet; that
/// migration is a follow-up.
///
/// Cross-slice fan-out via Wolverine: <c>Features/Sharing/Handlers</c>
/// subscribes to send the "X shared a doc with you" email. Future
/// subscribers might fire an in-app notification, an audit log entry,
/// or a digest aggregator.
///
/// Idempotency contract: handlers MUST tolerate duplicate deliveries.
/// We don't guard against double-sends because Wolverine local mode is
/// fire-once and the share-by-id endpoint is itself idempotent (returns
/// existing row if collaborator already present, which means we won't
/// publish twice for the same pair).
/// </summary>
public record DocumentSharedEvent(
    Guid DocumentId,
    string DocumentTitle,
    string SharedByUserId,
    string? SharedByName,
    string SharedWithUserId,
    string? SharedWithEmail,
    string Permission);
