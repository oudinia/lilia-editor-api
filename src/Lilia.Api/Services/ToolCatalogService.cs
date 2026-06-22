using System.Collections.Concurrent;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IToolCatalogService
{
    Task PreloadAsync();
    IReadOnlyList<Tool> Enabled();
    Tool? Get(string slug);
}

/// <summary>
/// Loads the <c>tools</c> registry into memory at startup (mirrors
/// LatexCatalogService / AiCatalogService) and serves the public tool suite
/// synchronously. DB-authoritative; rebuilt on boot.
/// </summary>
public class ToolCatalogService : IToolCatalogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ToolCatalogService> _logger;

    private readonly ConcurrentDictionary<string, Tool> _bySlug = new(StringComparer.OrdinalIgnoreCase);
    private volatile List<Tool> _enabled = new();
    private volatile bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public ToolCatalogService(IServiceScopeFactory scopeFactory, ILogger<ToolCatalogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PreloadAsync() => await EnsureLoadedAsync();

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await _loadLock.WaitAsync();
        try
        {
            if (_loaded) return;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
            var rows = await db.Tools.AsNoTracking().ToListAsync();
            _bySlug.Clear();
            foreach (var t in rows) _bySlug[t.Slug] = t;
            _enabled = rows.Where(t => t.Enabled).OrderBy(t => t.SortOrder).ThenBy(t => t.Title).ToList();
            _loaded = true;
            _logger.LogInformation("Tool catalog loaded: {Count} tools ({Enabled} enabled)", rows.Count, _enabled.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool catalog failed to load");
            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public IReadOnlyList<Tool> Enabled() => _enabled;

    public Tool? Get(string slug) => _bySlug.TryGetValue(slug, out var t) ? t : null;
}
