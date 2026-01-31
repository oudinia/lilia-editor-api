using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IAssetService
{
    Task<List<AssetDto>> GetAssetsAsync(Guid documentId);
    Task<AssetDto?> GetAssetAsync(Guid documentId, Guid assetId);
    Task<AssetUploadDto> CreateAssetAsync(Guid documentId, string userId, CreateAssetDto dto);
    Task<bool> DeleteAssetAsync(Guid documentId, Guid assetId);
}
