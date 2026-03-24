using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IStudioService
{
    // Tree operations
    Task<StudioTreeDto?> GetTreeAsync(Guid documentId);
    Task<StudioBlockDetailDto?> GetBlockDetailAsync(Guid documentId, Guid blockId);
    Task<StudioBlockNodeDto> CreateBlockAsync(Guid documentId, CreateBlockDto dto);
    Task<StudioBlockDetailDto?> UpdateBlockContentAsync(Guid documentId, Guid blockId, UpdateBlockDto dto);
    Task<bool> DeleteBlockAsync(Guid documentId, Guid blockId);
    Task<bool> MoveBlockAsync(Guid documentId, Guid blockId, MoveBlockDto dto);
    Task<bool> UpdateBlockMetadataAsync(Guid documentId, Guid blockId, UpdateBlockMetadataDto dto);

    // Preview
    Task<BlockPreviewDto?> GetBlockPreviewAsync(Guid blockId, string format);
    Task<BlockPreviewDto> RenderBlockPreviewAsync(Guid documentId, Guid blockId, string format);

    // Session
    Task<StudioSessionDto?> GetSessionAsync(string userId, Guid documentId);
    Task<StudioSessionDto> SaveSessionAsync(string userId, Guid documentId, SaveStudioSessionDto dto);
}
