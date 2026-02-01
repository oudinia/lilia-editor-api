namespace Lilia.Api.Services;

public interface IPreviewCacheService
{
    Task<string?> GetCachedPreviewAsync(Guid docId, string format, int? page = null);
    Task SetCachedPreviewAsync(Guid docId, string format, string content, int? page = null);
    Task InvalidateCacheAsync(Guid docId);
}
