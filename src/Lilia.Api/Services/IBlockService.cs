using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IBlockService
{
    Task<List<BlockDto>> GetBlocksAsync(Guid documentId);
    Task<BlockDto?> GetBlockAsync(Guid documentId, Guid blockId);
    Task<BlockDto> CreateBlockAsync(Guid documentId, CreateBlockDto dto);
    Task<BlockDto?> UpdateBlockAsync(Guid documentId, Guid blockId, UpdateBlockDto dto);
    Task<bool> DeleteBlockAsync(Guid documentId, Guid blockId);
    Task<List<BlockDto>> BatchUpdateBlocksAsync(Guid documentId, List<BatchUpdateBlockDto> blocks);
    Task<List<BlockDto>> ReorderBlocksAsync(Guid documentId, List<Guid> blockIds);
}
