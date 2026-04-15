namespace Lilia.Core.Exceptions;

/// <summary>
/// Stable, machine-readable error codes used in API responses.
/// Frontend maps these to locale strings — the API never sends translated text.
/// Format: DOMAIN_SPECIFIC_CONDITION (all caps, underscores).
/// </summary>
public static class LiliaErrorCodes
{
    // ── Generic ──────────────────────────────────────────────────────────────
    public const string InternalError          = "INTERNAL_ERROR";
    public const string ValidationFailed       = "VALIDATION_FAILED";
    public const string NotFound               = "NOT_FOUND";
    public const string Forbidden              = "FORBIDDEN";
    public const string Unauthorized           = "UNAUTHORIZED";
    public const string Conflict               = "CONFLICT";
    public const string RateLimited            = "RATE_LIMITED";

    // ── Documents ─────────────────────────────────────────────────────────────
    public const string DocumentNotFound       = "DOCUMENT_NOT_FOUND";
    public const string DocumentAccessDenied   = "DOCUMENT_ACCESS_DENIED";
    public const string DocumentTrashed        = "DOCUMENT_TRASHED";

    // ── Blocks ───────────────────────────────────────────────────────────────
    public const string BlockNotFound          = "BLOCK_NOT_FOUND";
    public const string BlockTypeMismatch      = "BLOCK_TYPE_MISMATCH";
    public const string InvalidBlockContent    = "INVALID_BLOCK_CONTENT";

    // ── Import ───────────────────────────────────────────────────────────────
    public const string ImportFileTooLarge     = "IMPORT_FILE_TOO_LARGE";
    public const string ImportUnsupportedFormat = "IMPORT_UNSUPPORTED_FORMAT";
    public const string ImportParsingFailed    = "IMPORT_PARSING_FAILED";
    public const string ImportSessionNotFound  = "IMPORT_SESSION_NOT_FOUND";
    public const string ImportSessionFinalized = "IMPORT_SESSION_FINALIZED";
    public const string ImportBlocksUnreviewed = "IMPORT_BLOCKS_UNREVIEWED";

    // ── Export ───────────────────────────────────────────────────────────────
    public const string ExportFailed           = "EXPORT_FAILED";
    public const string ExportLatexError       = "EXPORT_LATEX_ERROR";
    public const string ExportEmptyDocument    = "EXPORT_EMPTY_DOCUMENT";

    // ── External services ─────────────────────────────────────────────────────
    public const string MathpixNotConfigured   = "MATHPIX_NOT_CONFIGURED";
    public const string MathpixAuthFailed      = "MATHPIX_AUTH_FAILED";
    public const string MathpixRateLimited     = "MATHPIX_RATE_LIMITED";
    public const string MathpixTimeout         = "MATHPIX_TIMEOUT";
    public const string MathpixProcessingFailed = "MATHPIX_PROCESSING_FAILED";
    public const string MineruUnavailable      = "MINERU_UNAVAILABLE";
    public const string MineruTimeout          = "MINERU_TIMEOUT";
    public const string StorageUploadFailed    = "STORAGE_UPLOAD_FAILED";
    public const string StorageReadFailed      = "STORAGE_READ_FAILED";
    public const string ExternalServiceError   = "EXTERNAL_SERVICE_ERROR";

    // ── Collaboration ─────────────────────────────────────────────────────────
    public const string CollaboratorNotFound   = "COLLABORATOR_NOT_FOUND";
    public const string CollaboratorAlreadyExists = "COLLABORATOR_ALREADY_EXISTS";
    public const string InvitationExpired      = "INVITATION_EXPIRED";

    // ── Bibliography ─────────────────────────────────────────────────────────
    public const string BibliographyEntryNotFound = "BIBLIOGRAPHY_ENTRY_NOT_FOUND";
    public const string DoiLookupFailed        = "DOI_LOOKUP_FAILED";
    public const string IsbnLookupFailed       = "ISBN_LOOKUP_FAILED";

    // ── AI ────────────────────────────────────────────────────────────────────
    public const string AiServiceUnavailable   = "AI_SERVICE_UNAVAILABLE";
    public const string AiQuotaExceeded        = "AI_QUOTA_EXCEEDED";
}
