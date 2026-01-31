namespace Lilia.Core.DTOs;

public record AssetDto(
    Guid Id,
    Guid DocumentId,
    string FileName,
    string FileType,
    long FileSize,
    string? Url,
    int? Width,
    int? Height,
    DateTime CreatedAt
);

public record CreateAssetDto(
    string FileName,
    string FileType,
    long FileSize,
    int? Width,
    int? Height
);

public record AssetUploadDto(
    Guid Id,
    string UploadUrl,
    string PublicUrl
);
