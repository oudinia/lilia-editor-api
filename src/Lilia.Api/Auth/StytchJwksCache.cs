using Microsoft.IdentityModel.Tokens;

namespace Lilia.Api.Auth;

/// <summary>
/// Lightweight cache for Stytch's session JWKS. Stytch publishes
/// only a JWKS document (no full OIDC discovery), so we fetch +
/// cache the signing keys ourselves instead of going through
/// <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c>.
///
/// Keys are refetched at most once per 24h. ASP.NET's JWT bearer
/// invokes <see cref="GetKeys"/> on each token validation; the lock
/// + cached `_cached` field keep the steady-state cost ~0.
/// </summary>
public static class StytchJwksCache
{
    private static readonly HttpClient _http = new();
    private static JsonWebKeySet? _cached;
    private static DateTime _fetchedAt = DateTime.MinValue;
    private static readonly object _lock = new();

    /// <summary>
    /// Fetch the keys before anyone signs in (start-up warm-up, 15 Nov 2026): the first signed-in requests after a
    /// restart waited on this fetch together, made synchronously under the lock.
    /// </summary>
    public static async Task PrefetchAsync(string jwksUrl, CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(jwksUrl, ct);
        var keys = JsonWebKeySet.Create(json);
        lock (_lock)
        {
            _cached = keys;
            _fetchedAt = DateTime.UtcNow;
        }
    }

    public static IEnumerable<SecurityKey> GetKeys(string jwksUrl)
    {
        lock (_lock)
        {
            if (_cached == null || (DateTime.UtcNow - _fetchedAt) > TimeSpan.FromHours(24))
            {
                var json = _http.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
                _cached = JsonWebKeySet.Create(json);
                _fetchedAt = DateTime.UtcNow;
            }
            return _cached.GetSigningKeys();
        }
    }
}
