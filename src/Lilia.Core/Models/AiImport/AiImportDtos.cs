namespace Lilia.Core.Models.AiImport;

/// <summary>
/// Response for batch block classification of an import review session.
/// </summary>
public record ClassifySessionResponse(
    Guid SessionId,
    List<BlockClassification> Classifications,
    int TotalBlocks,
    int SuggestedChanges
);

/// <summary>
/// Response for quality suggestions on a single block.
/// </summary>
public record BlockSuggestionsResponse(
    string BlockId,
    string BlockType,
    List<QualitySuggestion> Suggestions
);

/// <summary>
/// Response for auto-fix formatting on a single block.
/// </summary>
public record BlockFixResponse(
    string BlockId,
    string OriginalContent,
    string FixedContent,
    bool WasModified,
    List<string> ChangesApplied
);
