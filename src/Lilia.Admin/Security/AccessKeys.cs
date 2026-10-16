using Microsoft.IdentityModel.Tokens;

namespace Lilia.Admin.Security;

/// <summary>The keys Cloudflare Access signs its tokens with.</summary>
public interface IAccessKeys
{
    IEnumerable<SecurityKey> Current(string? kid);
}

/// <summary>
/// Cloudflare Access's signing keys, from <c>{team}/cdn-cgi/access/certs</c>: fetched at start,
/// again every hour, and at once when a token names a key we don't have (Cloudflare rotates them).
/// </summary>
public sealed class CloudflareAccessKeys(IHttpClientFactory http, IConfiguration config, ILogger<CloudflareAccessKeys> log)
    : IAccessKeys, IHostedService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private volatile IReadOnlyList<SecurityKey> _keys = [];
    private DateTime _fetchedAt = DateTime.MinValue;

    public IEnumerable<SecurityKey> Current(string? kid)
    {
        var keys = _keys;
        var unknown = kid is not null && keys.All(k => k.KeyId != kid);
        if (unknown || DateTime.UtcNow - _fetchedAt > MaxAge)
            _ = RefreshAsync(CancellationToken.None);
        return keys;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!await _refreshing.WaitAsync(0, ct)) return;
        try
        {
            var team = (config["Access:TeamDomain"] ?? "").TrimEnd('/');
            if (team.Length == 0) return;
            var json = await http.CreateClient().GetStringAsync($"{team}/cdn-cgi/access/certs", ct);
            _keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
            _fetchedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not fetch Cloudflare Access signing keys; sign-in fails until they load");
        }
        finally
        {
            _refreshing.Release();
        }
    }

    public Task StartAsync(CancellationToken ct) => RefreshAsync(ct);
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
