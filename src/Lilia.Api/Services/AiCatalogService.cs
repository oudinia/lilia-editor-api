using System.Collections.Concurrent;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IAiCatalogService
{
    Task PreloadAsync();

    /// <summary>Enabled models, ordered by sort_order then display name.</summary>
    IReadOnlyList<AiModel> Enabled();

    /// <summary>Lookup a single model by id (enabled or not); null if unknown.</summary>
    AiModel? Get(string id);

    /// <summary>The default model id (the is_default row), with a safe fallback.</summary>
    string DefaultModelId();

    /// <summary>Whether a membership tier may select a model id.</summary>
    bool IsAllowedFor(string modelId, string membership);

    /// <summary>
    /// Model-weighted credit cost for a call. Uses the catalog's per-1k-token
    /// rates; falls back to ~1 credit / 1k tokens for an unknown model. Min 1.
    /// </summary>
    int CreditsFor(string modelId, int inputTokens, int outputTokens);
}

/// <summary>
/// Loads the <c>ai_models</c> catalog into memory at startup (mirroring
/// LatexCatalogService / UnicodeShimService) and serves the model picker +
/// resolution synchronously. DB is authoritative; the cache is rebuilt on boot.
/// </summary>
public class AiCatalogService : IAiCatalogService
{
    // free < pro < team
    private static readonly Dictionary<string, int> Rank = new()
    {
        ["free"] = 0, ["pro"] = 1, ["team"] = 2,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiCatalogService> _logger;
    private readonly AiOptions _options;

    private readonly ConcurrentDictionary<string, AiModel> _byId = new(StringComparer.OrdinalIgnoreCase);
    private volatile List<AiModel> _enabled = new();
    private volatile string? _defaultId;
    private volatile bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public AiCatalogService(
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<AiOptions> options,
        ILogger<AiCatalogService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
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
            var rows = await db.AiModels.AsNoTracking().ToListAsync();
            _byId.Clear();
            foreach (var m in rows) _byId[m.Id] = m;
            _enabled = rows.Where(m => m.Enabled)
                .OrderBy(m => m.SortOrder).ThenBy(m => m.DisplayName)
                .ToList();
            _defaultId = rows.FirstOrDefault(m => m.IsDefault && m.Enabled)?.Id;
            _loaded = true;
            _logger.LogInformation("AI model catalog loaded: {Count} models ({Enabled} enabled), default={Default}",
                rows.Count, _enabled.Count, _defaultId ?? "(config fallback)");
        }
        catch (Exception ex)
        {
            // Degrade gracefully — resolution falls back to AiOptions, so AI keeps
            // working even if the catalog can't load.
            _logger.LogWarning(ex, "AI model catalog failed to load; falling back to config");
            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public IReadOnlyList<AiModel> Enabled() => _enabled;

    public AiModel? Get(string id) => _byId.TryGetValue(id, out var m) ? m : null;

    public string DefaultModelId() => _defaultId ?? _options.DefaultModel;

    public bool IsAllowedFor(string modelId, string membership)
    {
        var m = Get(modelId);
        if (m is null || !m.Enabled) return false;
        var need = Rank.GetValueOrDefault(m.MinMembership, 1);
        var have = Rank.GetValueOrDefault(membership, 0);
        return have >= need;
    }

    public int CreditsFor(string modelId, int inputTokens, int outputTokens)
    {
        var m = Get(modelId);
        decimal credits;
        if (m is not null && (m.CreditInPerKTok > 0 || m.CreditOutPerKTok > 0))
        {
            credits = (inputTokens / 1000m) * m.CreditInPerKTok
                    + (outputTokens / 1000m) * m.CreditOutPerKTok;
        }
        else
        {
            // Unknown / unpriced model → model-agnostic fallback (~1 credit/1k).
            credits = (inputTokens + outputTokens) / 1000m;
        }
        return Math.Max(1, (int)Math.Ceiling(credits));
    }
}
