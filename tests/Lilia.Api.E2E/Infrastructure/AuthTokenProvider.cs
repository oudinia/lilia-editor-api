using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Lilia.Api.E2E.Infrastructure;

/// <summary>
/// Provides auth tokens for E2E tests.
/// - DevJwt mode: generates self-signed JWTs (for local/staging with no Auth:Authority)
/// - Kinde mode: uses M2M client credentials to get real tokens from Kinde
/// </summary>
public static class AuthTokenProvider
{
    private static readonly Dictionary<string, string> _tokenCache = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<string> GetTokenAsync(TestUserConfig user)
    {
        var config = E2EConfiguration.Instance;
        var cacheKey = $"{config.AuthMode}:{user.UserId}";

        await _lock.WaitAsync();
        try
        {
            if (_tokenCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var token = config.AuthMode switch
            {
                "Kinde" => await GetKindeTokenAsync(config.Kinde),
                "StaticToken" => GetStaticToken(config.StaticToken),
                _ => GenerateDevJwt(user),
            };

            _tokenCache[cacheKey] = token;
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Generates a self-signed JWT with user claims.
    /// Works when the API has no Auth:Authority configured (accepts any JWT).
    /// </summary>
    private static string GenerateDevJwt(TestUserConfig user)
    {
        var key = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("e2e-test-signing-key-not-for-production-use-only-for-local-dev"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", user.UserId),
            new Claim("email", user.Email),
            new Claim("name", user.Name),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: "lilia-e2e-tests",
            audience: "lilia-api",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Returns a pre-issued JWT supplied via config (E2E__StaticToken).
    /// Useful when you have a user token from a browser session and want
    /// to run read-heavy tests without provisioning M2M credentials.
    /// The same token is used for every test user — so multi-user
    /// authorization tests will not behave correctly in this mode.
    /// </summary>
    private static string GetStaticToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "StaticToken mode selected but E2E__StaticToken is empty. " +
                "Set it to a valid bearer JWT.");
        return token;
    }

    /// <summary>
    /// Gets an M2M access token from Kinde using client credentials grant.
    /// For CI: set E2E__Kinde__ClientId, E2E__Kinde__ClientSecret env vars.
    /// </summary>
    private static async Task<string> GetKindeTokenAsync(KindeConfig kinde)
    {
        if (string.IsNullOrEmpty(kinde.ClientId) || string.IsNullOrEmpty(kinde.ClientSecret))
            throw new InvalidOperationException(
                "Kinde M2M credentials not configured. Set E2E__Kinde__ClientId and E2E__Kinde__ClientSecret.");

        using var http = new HttpClient();
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", kinde.ClientId),
            new("client_secret", kinde.ClientSecret),
        };
        if (!string.IsNullOrEmpty(kinde.Audience))
            form.Add(new("audience", kinde.Audience));

        var response = await http.PostAsync($"{kinde.Domain}/oauth2/token", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<KindeTokenResponse>();
        return result?.AccessToken ?? throw new InvalidOperationException("No access_token in Kinde response");
    }

    private sealed class KindeTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
