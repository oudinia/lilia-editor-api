using Lilia.Core.Models.AiImport;

namespace Lilia.Api.Services;

public interface IAiImportService
{
    /// <summary>
    /// Classify a single block's type using AI or heuristics.
    /// </summary>
    Task<BlockClassification> ClassifyBlockAsync(string content, string currentType);

    /// <summary>
    /// Suggest quality improvements for a block's content.
    /// </summary>
    Task<List<QualitySuggestion>> SuggestImprovementsAsync(string content, string blockType);

    /// <summary>
    /// Auto-fix formatting issues in a block's content.
    /// </summary>
    Task<string> FixFormattingAsync(string content, string blockType);

    /// <summary>
    /// Classify multiple blocks in batch (more efficient than individual calls).
    /// </summary>
    Task<List<BlockClassification>> ClassifyBatchAsync(List<(string content, string currentType)> blocks);
}
