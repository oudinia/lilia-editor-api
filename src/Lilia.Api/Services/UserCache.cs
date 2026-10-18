using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace Lilia.Api.Services;

/// <summary>
/// Whose cache a request reads and fills (the user, 18 Oct: "add redis and cache in it per user").
/// Set once per request from the signed-in user; compiled PDFs, bibliographies and checks are kept
/// under that user only. Outside a request (background work, tests) there is no owner and only
/// the process's own memory is used.
/// </summary>
public static class CacheOwner
{
    private static readonly AsyncLocal<string?> CurrentUser = new();

    public static string? Current => CurrentUser.Value;

    public static void Set(string? userId) => CurrentUser.Value = string.IsNullOrWhiteSpace(userId) ? null : userId;

    /// <summary>The user's cache namespace: a hash, so keys never carry an account id.</summary>
    public static string Scope(string? userId) => userId is null
        ? "-"
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lilia-cache\u0001" + userId)))[..20].ToLowerInvariant();
}

/// <summary>Where per-user cache entries outlive the process: Redis, when configured.</summary>
public interface IUserCacheStore
{
    Task<byte[]?> GetAsync(string scope, string kind, string key);
    Task SetAsync(string scope, string kind, string key, byte[] value, TimeSpan ttl);
    /// <summary>Delete everything kept for this user; the number of entries removed.</summary>
    Task<long> PurgeAsync(string scope);
}

/// <summary>
/// Redis keys <c>{prefix}u:{scope}:{kind}:{key}</c>, each with a time to live. Redis itself is
/// local, password-protected, capped at 512 MB (least recently used out first) and writes nothing
/// to disk (ops/redis.sh).
/// </summary>
public sealed class RedisUserCacheStore : IUserCacheStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;

    public RedisUserCacheStore(IConnectionMultiplexer redis, string prefix)
    {
        _redis = redis;
        _prefix = prefix;
    }

    private string K(string scope, string kind, string key) => $"{_prefix}u:{scope}:{kind}:{key}";

    public async Task<byte[]?> GetAsync(string scope, string kind, string key)
    {
        var v = await _redis.GetDatabase().StringGetAsync(K(scope, kind, key));
        return v.HasValue ? (byte[]?)v : null;
    }

    public Task SetAsync(string scope, string kind, string key, byte[] value, TimeSpan ttl) =>
        _redis.GetDatabase().StringSetAsync(K(scope, kind, key), value, ttl);

    public async Task<long> PurgeAsync(string scope)
    {
        long removed = 0;
        var db = _redis.GetDatabase();
        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            var batch = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(db.Database, $"{_prefix}u:{scope}:*", pageSize: 500))
            {
                batch.Add(key);
                if (batch.Count == 500) { removed += await db.KeyDeleteAsync(batch.ToArray()); batch.Clear(); }
            }
            if (batch.Count > 0) removed += await db.KeyDeleteAsync(batch.ToArray());
        }
        return removed;
    }
}

/// <summary>
/// The per-user cache the compile, bibliography and check caches read through after their own
/// memory: kept for <see cref="Ttl"/>. A Redis that is down or slow is never a failed request:
/// the entry is compiled as if it were missing, and the problem is logged (at most once a minute).
/// </summary>
public static class UserCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(3);
    public static IUserCacheStore? Store { get; set; }
    public static ILogger? Logger { get; set; }
    private static long _lastWarning;

    private static void Warn(Exception ex, string what)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastWarning) < 60_000) return;
        Interlocked.Exchange(ref _lastWarning, now);
        Logger?.LogWarning(ex, "[Cache] Redis {What} failed; using memory only", what);
    }

    public static async Task<byte[]?> GetAsync(string kind, string key)
    {
        if (Store is null || CacheOwner.Current is not { } user) return null;
        try { return await Store.GetAsync(CacheOwner.Scope(user), kind, key); }
        catch (Exception ex) { Warn(ex, "read"); return null; }
    }

    public static async Task SetAsync(string kind, string key, byte[] value)
    {
        if (Store is null || CacheOwner.Current is not { } user) return;
        try { await Store.SetAsync(CacheOwner.Scope(user), kind, key, value, Ttl); }
        catch (Exception ex) { Warn(ex, "write"); }
    }

    /// <summary>Everything kept for <paramref name="userId"/>, in Redis and in this process's memory.</summary>
    public static async Task<long> PurgeUserAsync(string userId)
    {
        var scope = CacheOwner.Scope(userId);
        long removed = CompileCache.Shared.RemoveScope(scope) + CompileCache.Versions.RemoveScope(scope)
            + CompileCache.Checks.RemoveScope(scope) + LaTeXRenderService.ForgetScope(scope);
        if (Store is not null)
        {
            try { removed += await Store.PurgeAsync(scope); }
            catch (Exception ex) { Warn(ex, "purge"); }
        }
        return removed;
    }
}
