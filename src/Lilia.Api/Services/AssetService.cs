using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class AssetService : IAssetService
{
    private readonly LiliaDbContext _context;
    private readonly IStorageService _storageService;

    public AssetService(LiliaDbContext context, IStorageService storageService)
    {
        _context = context;
        _storageService = storageService;
    }

    public async Task<List<AssetDto>> GetAssetsAsync(Guid documentId)
    {
        var assets = await _context.Assets
            .Where(a => a.DocumentId == documentId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return assets.Select(MapToDto).ToList();
    }

    public async Task<AssetDto?> GetAssetAsync(Guid documentId, Guid assetId)
    {
        var asset = await _context.Assets
            .FirstOrDefaultAsync(a => a.DocumentId == documentId && a.Id == assetId);

        return asset == null ? null : MapToDto(asset);
    }

    public async Task<AssetUploadDto> CreateAssetAsync(Guid documentId, string userId, CreateAssetDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null)
            throw new ArgumentException("Document not found");

        var assetId = Guid.NewGuid();
        var extension = Path.GetExtension(dto.FileName);
        var storageKey = $"{userId}/documents/{documentId}/images/{assetId}{extension}";

        var asset = new Asset
        {
            Id = assetId,
            DocumentId = documentId,
            FileName = dto.FileName,
            FileType = dto.FileType,
            FileSize = dto.FileSize,
            StorageKey = storageKey,
            Url = _storageService.GetPublicUrl(storageKey),
            Width = dto.Width,
            Height = dto.Height,
            CreatedAt = DateTime.UtcNow
        };

        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        var uploadUrl = await _storageService.GeneratePresignedUploadUrl(storageKey, dto.FileType);

        return new AssetUploadDto(asset.Id, uploadUrl, asset.Url ?? "");
    }

    public async Task<bool> DeleteAssetAsync(Guid documentId, Guid assetId)
    {
        var asset = await _context.Assets
            .FirstOrDefaultAsync(a => a.DocumentId == documentId && a.Id == assetId);

        if (asset == null) return false;

        // Delete from storage
        try
        {
            await _storageService.DeleteAsync(asset.StorageKey);
        }
        catch
        {
            // Log but continue - storage file may not exist
        }

        _context.Assets.Remove(asset);
        await _context.SaveChangesAsync();

        return true;
    }

    private static AssetDto MapToDto(Asset a)
    {
        return new AssetDto(
            a.Id,
            a.DocumentId,
            a.FileName,
            a.FileType,
            a.FileSize,
            a.Url,
            a.Width,
            a.Height,
            a.CreatedAt
        );
    }
}
