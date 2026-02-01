using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

public class PreviewCacheService : IPreviewCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<PreviewCacheService> _logger;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);

    public PreviewCacheService(IDistributedCache cache, ILogger<PreviewCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetCachedPreviewAsync(Guid docId, string format, int? page = null)
    {
        var key = BuildCacheKey(docId, format, page);
        try
        {
            var cached = await _cache.GetStringAsync(key);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for preview {Key}", key);
            }
            return cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached preview for {Key}", key);
            return null;
        }
    }

    public async Task SetCachedPreviewAsync(Guid docId, string format, string content, int? page = null)
    {
        var key = BuildCacheKey(docId, format, page);
        try
        {
            await _cache.SetStringAsync(key, content, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheDuration
            });
            _logger.LogDebug("Cached preview for {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache preview for {Key}", key);
        }
    }

    public async Task InvalidateCacheAsync(Guid docId)
    {
        try
        {
            // Invalidate all cached formats for this document
            var formats = new[] { "html", "latex" };
            var tasks = new List<Task>();

            foreach (var format in formats)
            {
                tasks.Add(_cache.RemoveAsync(BuildCacheKey(docId, format, null)));
            }

            // Also invalidate page caches (assume max 100 pages cached)
            for (int i = 1; i <= 100; i++)
            {
                tasks.Add(_cache.RemoveAsync(BuildCacheKey(docId, "html", i)));
            }

            await Task.WhenAll(tasks);
            _logger.LogDebug("Invalidated cache for document {DocumentId}", docId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for document {DocumentId}", docId);
        }
    }

    private static string BuildCacheKey(Guid docId, string format, int? page)
    {
        var key = $"preview:{docId}:{format}";
        if (page.HasValue)
        {
            key += $":page{page}";
        }
        return key;
    }
}
