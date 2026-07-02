namespace Lilia.Core.DTOs;

// Phase B — cross-document sharing aggregation for the editor's
// Sharing surface (/shared). "Shared with me" is served by the
// existing document list (role != owner); the two DTOs below back the
// "Shared by me" and "People" tabs, which need owner-scoped
// aggregation the per-document collaborators API can't express.
// See lilia-docs/design-handoffs/2026-07-01-sharing-settings-*.md.

/// <summary>
/// One row in the "Shared by me" tab: a document I own that is exposed
/// to anyone — via a public link and/or explicit collaborators/invites.
/// Documents I own that are private and unshared are excluded.
/// </summary>
public record SharedByMeDto(
    Guid Id,
    string Title,
    DateTime UpdatedAt,
    DateTime? LastOpenedAt,
    bool IsPublic,
    bool HasShareLink,
    int CollaboratorCount,
    int PendingInviteCount,
    // Top-N active collaborators for the avatar stack; the counts above
    // carry the full totals so the UI can render "+N".
    List<SharedCollaboratorSummaryDto> Collaborators
);

public record SharedCollaboratorSummaryDto(
    string UserId,
    string? Name,
    string? Email,
    string? Image,
    string Role
);

/// <summary>
/// One person in the "People" tab: a distinct human reached across all
/// the documents I own, whether they've accepted (active) or not
/// (pending). Keyed by email so a registered collaborator and a lingering
/// pending row for the same address collapse into one entry.
/// </summary>
public record SharedPersonDto(
    // Stable identity for the row: the user id when registered, else
    // "email:{address}" for an unregistered pending invite.
    string Key,
    string? UserId,
    string? Name,
    string Email,
    string? Image,
    // "active" if they collaborate on at least one of my documents,
    // otherwise "pending".
    string Status,
    int DocumentCount,
    List<SharedPersonDocDto> Documents
);

public record SharedPersonDocDto(
    Guid DocumentId,
    string Title,
    string Role,
    // Per-(person, document) status — a person can be active on one
    // document and merely invited on another.
    string Status
);

/// <summary>
/// POST /api/shared/resend — re-send a pending invitation email for a
/// document I own. The role is read back from the existing pending row,
/// so the caller only needs to identify the (document, email) pair.
/// </summary>
public record ResendInviteDto(
    Guid DocumentId,
    string Email
);
