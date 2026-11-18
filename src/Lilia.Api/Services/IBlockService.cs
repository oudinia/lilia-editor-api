using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IBlockService
{
    Task<List<BlockDto>> GetBlocksAsync(Guid documentId);
    Task<BlockDto?> GetBlockAsync(Guid documentId, Guid blockId);
    Task<BlockDto> CreateBlockAsync(Guid documentId, CreateBlockDto dto);
    Task<BlockDto?> UpdateBlockAsync(Guid documentId, Guid blockId, UpdateBlockDto dto);
    Task<bool> DeleteBlockAsync(Guid documentId, Guid blockId);
    /// <summary>
    /// Whole-document block sync. When <paramref name="expectedVersion"/>
    /// is supplied the write is conditional on the document's
    /// optimistic-concurrency token — a stale value throws
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
    /// </summary>
    Task<BatchUpdateResultDto> BatchUpdateBlocksAsync(Guid documentId, List<BatchUpdateBlockDto> blocks, int? expectedVersion = null);
    /// <summary>
    /// New blocks after <paramref name="afterBlockId"/> (null: at the end) without rewriting the other blocks'
    /// sort orders (launch-bot #148, 18 Nov 2026); same version guard as <see cref="BatchUpdateBlocksAsync"/>.
    /// Returns every block of the document in order.
    /// </summary>
    Task<BatchUpdateResultDto> InsertBlocksAsync(Guid documentId, Guid? afterBlockId, List<BatchUpdateBlockDto> newBlocks, int? expectedVersion = null);
    Task<List<BlockDto>> ReorderBlocksAsync(Guid documentId, List<Guid> blockIds);
    Task<BlockDto?> ConvertBlockAsync(Guid documentId, Guid blockId, string newType);

    /// <summary>
    /// Tier 1 bulk-convert primitive: takes N adjacent blocks and transforms
    /// them per <paramref name="action"/>. Actions:
    ///   "to_list"           N blocks → one unordered list
    ///   "to_ordered_list"   N blocks → one ordered list
    ///   "merge_paragraph"   N blocks → one paragraph (texts joined)
    ///   "reheading"         N heading blocks → same count, new level
    /// The first block's SortOrder is preserved to avoid reshuffling the
    /// document. All writes happen in a single EF transaction.
    /// </summary>
    Task<BatchConvertResultDto?> BatchConvertAsync(Guid documentId, BatchConvertBlocksDto dto);
}
