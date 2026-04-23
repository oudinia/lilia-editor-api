using System.Text.Json;

namespace Lilia.Core.DTOs;

// --- Request DTOs ---

public record CreateReviewSessionDto(
    Guid? JobId,
    string DocumentTitle,
    List<CreateReviewBlockDto> Blocks,
    JsonElement? Warnings = null
);

public record CreateReviewBlockDto(
    string Id,
    string Type,
    JsonElement Content,
    int Confidence,
    JsonElement? Warnings,
    int SortOrder,
    int Depth
);

// SessionId / BlockId are accepted for backward compatibility but ignored —
// both come from the route. Clients can omit them.
public record UpdateBlockReviewDto(
    string? Status = null,
    JsonElement? CurrentContent = null,
    string? CurrentType = null,
    string? SessionId = null,
    string? BlockId = null
);

public record BulkActionDto(
    string Action, // approveAll, rejectErrors, approveHighConfidence, resetAll, approveSelected, rejectSelected
    List<string>? BlockIds = null,
    string? SessionId = null
);

public record FinalizeSessionDto(
    string? DocumentTitle = null,
    bool Force = false
);

/// <summary>
/// Tier 1 bulk-convert request against staged ImportBlockReview rows.
/// BlockIds are the frontend-generated string IDs (not DB row Guids).
/// Action: to_list | to_ordered_list | merge_paragraph | reheading.
/// HeadingLevel (1-6) required for reheading.
/// </summary>
public record BatchConvertReviewBlocksDto(
    List<string> BlockIds,
    string Action,
    int? HeadingLevel
);

/// <summary>
/// Source slice for a single review block — powers the "Source" sub-tab
/// on the .tex redesign. When SourceRange was populated at parse time
/// SliceOrigin is "parser"; fallback to renderer round-trip yields
/// SliceOrigin = "render".
/// </summary>
public sealed record BlockSourceDto(
    string BlockId,
    string Latex,
    string SliceOrigin,
    int? Start,
    int? End,
    string? SourceFile,
    // Block content + type so the UI can reuse existing block renderers
    // for the "Preview" sub-tab without a second round-trip.
    string? BlockType = null,
    JsonElement? Content = null
);

/// <summary>
/// Update a single tab's progress state. The tab name is a string — the
/// vocabulary is enforced only at the UI layer so adding new tabs
/// doesn't require a schema change. State is unvisited / in_progress /
/// done.
/// </summary>
public sealed record SetTabProgressDto(
    string Tab,
    string State
);

/// <summary>
/// Session hierarchy built server-side from the flat block list —
/// headings carry their descendant blocks as children. Drives the
/// TreePane in the .tex redesign; also usable by a CLI that wants to
/// print an outline.
/// </summary>
public sealed record SessionTreeNodeDto(
    string BlockId,
    string Type,
    string? Text,           // first ~80 chars of the block content
    int? HeadingLevel,      // null for non-heading blocks
    int? Depth,
    string Status,          // pending / approved / rejected / edited
    int ChildBlockCount,    // descendants including self
    List<SessionTreeNodeDto> Children
);

public sealed record SessionTreeDto(
    Guid SessionId,
    int TotalBlocks,
    List<SessionTreeNodeDto> Roots
);

/// <summary>
/// Per-tab counters + derived "done" state. Pure SQL aggregate so a CLI
/// can re-use it to print the same progress strip.
/// </summary>
public sealed record TabStatsDto(
    Guid SessionId,
    TabStatEntryDto Structure,
    TabStatEntryDto Content,
    TabStatEntryDto Tables,
    TabStatEntryDto Media,
    TabStatEntryDto Math,
    TabStatEntryDto Citations,
    TabStatEntryDto Coverage,
    TabStatEntryDto Diagnostics,
    string? LastFocusedTab
);

public sealed record TabStatEntryDto(
    int Pending,       // blocks/items still needing attention in this tab
    int Done,          // blocks/items resolved
    int Total,         // total blocks/items
    string ProgressState   // unvisited | in_progress | done — merged from tab_progress + derived
);

/// <summary>
/// End-of-review snapshot — what happened during an import. Used for
/// the History list's per-session page and for the CLI to print
/// "here's what I did" in markdown / json / csv.
/// </summary>
public sealed record SessionReportDto(
    Guid SessionId,
    string DocumentTitle,
    string Status,
    string? SourceFormat,
    DateTime CreatedAt,
    DateTime? FinalizedAt,
    double? DurationMinutes,
    Guid? ProducedDocumentId,
    ReportCountsDto Blocks,
    ReportCountsDto Diagnostics,
    int? QualityScore,
    double? CoveragePercent,
    List<ReportTokenDto> TopUnsupported,
    int ActivityEventCount
);

public sealed record ReportCountsDto(
    int Total,
    int Approved,
    int Rejected,
    int Pending,
    int Edited
);

public sealed record ReportTokenDto(
    string Name,
    string Kind,
    string? PackageSlug,
    int Count,
    string CoverageLevel
);

/// <summary>
/// Pre-checkout summary for the import summary sheet (FT-IMP-001 §Summary
/// sheet content). Composed from the same signals as the end-of-review
/// report plus source / format / coverage / estimate fields. Consumed by
/// the upload dialog when the user ticked "show summary before importing"
/// and by the /import-summary/:sessionId page the user lands on.
/// </summary>
public sealed record SessionSummaryDto(
    Guid SessionId,
    string Status,
    // SOURCE
    string? SourceFileName,
    string? SourceFormat,
    int? Lines,
    int? PackageCount,
    // FORMAT (document class + engine hint, LaTeX-specific for now)
    string? DocumentClass,
    string? Engine,
    // CONTENT
    Dictionary<string, int> BlockCountsByType,
    int TotalBlocks,
    // COVERAGE
    double? CoverageMappedPercent,
    int UnsupportedTokenCount,
    // QUALITY
    int ErrorCount,
    int WarningCount,
    int? QualityScore,
    // ESTIMATED REVIEW
    int EstimatedReviewMinutes
);

/// <summary>
/// Dashboard row for the "Reviews in progress" list. Keep projection cheap —
/// no block payloads, just the counters needed to decide which session to
/// resume. Status here is the session-level status (parsing / pending_review
/// / auto_finalized / etc.).
/// </summary>
public record ReviewSessionSummaryDto(
    Guid Id,
    string DocumentTitle,
    string Status,
    int TotalBlocks,
    int ApprovedBlocks,
    int RejectedBlocks,
    int PendingBlocks,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ExpiresAt
);

public record AddReviewCollaboratorDto(
    string SessionId,
    string Email,
    string Role = "reviewer" // reviewer, viewer
);

public record AddReviewCommentDto(
    string BlockId,
    string Content,
    string? SessionId = null
);

public record UpdateReviewCommentDto(
    bool Resolved,
    string? SessionId = null,
    string? CommentId = null
);

// --- Response DTOs ---

public record ReviewSessionInfoDto(
    Guid Id,
    Guid? JobId,
    string OwnerId,
    string DocumentTitle,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ExpiresAt,
    Guid? DocumentId,
    JsonElement? OriginalWarnings,
    bool AutoFinalizeEnabled,
    int? QualityScore,
    string? DocumentCategory = null,
    string SourceFormat = "tex",
    string? LastFocusedTab = null,
    JsonElement? TabProgress = null
);

public record BlockReviewDto(
    Guid Id,
    string BlockId,
    int BlockIndex,
    string Status,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    ReviewUserDto? Reviewer,
    JsonElement OriginalContent,
    string OriginalType,
    JsonElement? CurrentContent,
    string? CurrentType,
    int? Confidence,
    JsonElement? Warnings,
    int SortOrder,
    int Depth,
    int CommentCount,
    int UnresolvedCommentCount
);

public record ReviewUserDto(
    string Id,
    string? Name,
    string? Image
);

public record ReviewUserWithEmailDto(
    string Id,
    string? Name,
    string? Email,
    string? Image
);

public record SessionDataDto(
    ReviewSessionInfoDto Session,
    ReviewUserDto Owner,
    List<BlockReviewDto> Blocks,
    List<CollaboratorInfoDto> Collaborators,
    string UserRole
);

public record CollaboratorInfoDto(
    string UserId,
    string Role,
    DateTime InvitedAt,
    DateTime? LastActiveAt,
    ReviewUserWithEmailDto User
);

public record FinalizeResultDto(
    FinalizedDocumentDto Document,
    FinalizeStatisticsDto Statistics
);

public record FinalizedDocumentDto(
    Guid Id,
    string Title
);

public record FinalizeStatisticsDto(
    int ImportedBlocks,
    int SkippedBlocks
);

public record ImportDiagnosticDto(
    Guid Id,
    Guid SessionId,
    string? BlockId,
    string? ElementPath,
    int? SourceLineStart,
    int? SourceLineEnd,
    int? SourceColStart,
    int? SourceColEnd,
    string? SourceSnippet,
    string Category,
    string Severity,
    string Code,
    string Message,
    string? SuggestedAction,
    bool AutoFixApplied,
    string? DocsUrl,
    bool Dismissed,
    string? DismissedBy,
    DateTime? DismissedAt,
    DateTime CreatedAt
);

public record LatexImportUploadResponseDto(
    Guid SessionId,
    Guid JobId
);

// Word→LaTeX (and LaTeX→LaTeX) transition hints surfaced on a review session.
// Persisted as ImportStructuralFinding rows; this DTO is the projection.
public record ImportStructuralFindingDto(
    Guid Id,
    Guid? SessionId,
    Guid? DocumentId,
    string? BlockId,
    string Kind,
    string Severity,
    string Title,
    string Detail,
    string SuggestedAction,
    string ActionKind,
    JsonElement? ActionPayload,
    string Status,               // pending | applied | dismissed
    string? ResolvedBy,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Source = "rule"       // rule | ai | manual
);

public record SetDocumentCategoryDto(string? Category);

public record ComputeHintsResponseDto(int Count);

public record ReviewCommentDto(
    Guid Id,
    string BlockId,
    string Content,
    bool Resolved,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ReviewUserDto User,
    ReviewUserDto? Resolver
);

public record ReviewActivityDto(
    Guid Id,
    string Action,
    string? BlockId,
    JsonElement? Details,
    DateTime CreatedAt,
    ReviewUserDto User
);

// --- Wrapper response DTOs ---

public record CreateSessionResponseDto(
    ReviewSessionInfoDto Session,
    List<BlockReviewDto> Blocks
);

public record CollaboratorResponseDto(
    CollaboratorInfoDto Collaborator
);

public record CommentResponseDto(
    ReviewCommentDto Comment
);

public record CommentsListDto(
    List<ReviewCommentDto> Comments
);

public record ActivitiesListDto(
    List<ReviewActivityDto> Activities
);
