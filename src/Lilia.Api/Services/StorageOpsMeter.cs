using System.Collections.Concurrent;

namespace Lilia.Api.Services;

/// <summary>
/// R2's operations this calendar month (UTC), as this API made them (21 Nov 2026: the owner wants to be told when R2's
/// free tier is not enough: 10 GB, 1 million Class A and 10 million Class B operations a month). Class A is a write or
/// a list, Class B a read; deletes are free. Kept in Redis when the API has it (so a restart doesn't lose the month),
/// else in memory since start-up. Cloudflare's own count, in its dashboard, is the bill; this is the early warning.
/// </summary>
public sealed class StorageOpsMeter
{
    private readonly StackExchange.Redis.IConnectionMultiplexer? _redis;
    private readonly string _prefix;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, long> _memory = new();
    private readonly DateTimeOffset _started;

    public StorageOpsMeter(IServiceProvider services, IConfiguration configuration, TimeProvider? time = null)
    {
        _redis = services.GetService(typeof(StackExchange.Redis.IConnectionMultiplexer)) as StackExchange.Redis.IConnectionMultiplexer;
        _prefix = configuration["Redis:Prefix"] is { Length: > 0 } p ? p : "lilia:";
        _time = time ?? TimeProvider.System;
        _started = _time.GetUtcNow();
    }

    private string Month => _time.GetUtcNow().ToString("yyyy-MM");
    private string KeyFor(string bucket) => $"{_prefix}r2ops:{bucket}:{Month}";

    public void ClassA(string bucket) => Count(bucket, "A");
    public void ClassB(string bucket) => Count(bucket, "B");

    private void Count(string bucket, string cls)
    {
        _memory.AddOrUpdate($"{bucket}:{Month}:{cls}", 1, (_, n) => n + 1);
        if (_redis is null) return;
        try
        {
            var db = _redis.GetDatabase();
            var key = KeyFor(bucket);
            // Fire and forget: a count never slows or fails a file.
            db.HashIncrement(key, cls, 1, StackExchange.Redis.CommandFlags.FireAndForget);
            db.KeyExpire(key, TimeSpan.FromDays(70), StackExchange.Redis.CommandFlags.FireAndForget);
        }
        catch { /* Redis away: the memory count stands */ }
    }

    /// <summary>This month's Class A and B counts, and since when they are counted.</summary>
    public async Task<(long A, long B, string Since)> ThisMonthAsync(string bucket)
    {
        if (_redis is not null)
        {
            try
            {
                var values = await _redis.GetDatabase().HashGetAsync(KeyFor(bucket), ["A", "B"]);
                return ((long?)values[0] ?? 0, (long?)values[1] ?? 0, $"the start of {Month} (UTC), or the first R2 request after it");
            }
            catch { /* fall back to memory */ }
        }
        _memory.TryGetValue($"{bucket}:{Month}:A", out var a);
        _memory.TryGetValue($"{bucket}:{Month}:B", out var b);
        var since = _started > new DateTimeOffset(_time.GetUtcNow().Year, _time.GetUtcNow().Month, 1, 0, 0, 0, TimeSpan.Zero)
            ? $"this API's start-up ({_started:yyyy-MM-dd HH:mm} UTC; no Redis)"
            : $"the start of {Month} (UTC)";
        return (a, b, since);
    }
}
