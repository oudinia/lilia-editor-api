using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lilia.Api.E2E.Infrastructure;

/// <summary>
/// Base class for E2E tests against a remote Lilia API.
/// Handles authenticated HTTP clients and test data cleanup.
/// </summary>
public abstract class E2ETestBase : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    protected readonly E2EConfiguration Config = E2EConfiguration.Instance;
    private readonly List<(string endpoint, string id)> _cleanup = new();

    protected HttpClient CreateClient() => new()
    {
        BaseAddress = new Uri(Config.ApiBaseUrl),
        Timeout = TimeSpan.FromSeconds(60),
    };

    protected async Task<HttpClient> CreateAuthenticatedClientAsync(string userKey = "Owner")
    {
        var client = CreateClient();

        if (!Config.TestUsers.TryGetValue(userKey, out var user))
            throw new InvalidOperationException($"Test user '{userKey}' not found in config");

        if (string.IsNullOrEmpty(user.UserId))
        {
            if (AuthTokenProvider.IsDevHeaderMode)
                throw new InvalidOperationException(
                    "DevHeader mode cannot make anonymous requests: the dev API authenticates every " +
                    "request without an Authorization header as its built-in dev user (the owner's own " +
                    "account). Run anonymous tests with DevJwt/Kinde/StaticToken instead.");
            return client; // Anonymous
        }

        await AuthTokenProvider.AuthenticateAsync(client, user);
        return client;
    }

    protected TestUserConfig GetUser(string key) =>
        Config.TestUsers.TryGetValue(key, out var user)
            ? user
            : throw new InvalidOperationException($"Test user '{key}' not configured");

    /// <summary>
    /// Tracks a resource for cleanup after the test.
    /// </summary>
    protected void TrackForCleanup(string endpoint, string id)
    {
        _cleanup.Add((endpoint, id));
    }

    /// <summary>
    /// Creates a document via API and tracks it for cleanup.
    /// </summary>
    protected async Task<JsonElement> CreateTestDocumentAsync(HttpClient client, string title = "E2E Test Document")
    {
        var response = await client.PostAsJsonAsync("/api/documents", new { title });
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = doc.TryGetProperty("id", out var idProp) ? idProp.GetString()
               : doc.TryGetProperty("document", out var docProp) && docProp.TryGetProperty("id", out var innerIdProp)
                   ? innerIdProp.GetString()
                   : null;

        if (id != null) TrackForCleanup("/api/documents", id);
        return doc;
    }

    /// <summary>
    /// Creates a block on a document and tracks it for cleanup.
    /// </summary>
    protected async Task<JsonElement> CreateTestBlockAsync(HttpClient client, string documentId, string type = "paragraph", object? content = null)
    {
        var payload = new { documentId, type, content = content ?? new { text = "E2E test block" } };
        var response = await client.PostAsJsonAsync($"/api/documents/{documentId}/blocks", payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var client = CreateClient();
        // Try to get an owner token for cleanup
        try
        {
            await AuthTokenProvider.AuthenticateAsync(client, GetUser("Owner"));
        }
        catch
        {
            // If auth fails, try cleanup without auth (works in dev mode)
        }

        // Cleanup in reverse order (blocks before documents)
        foreach (var (endpoint, id) in _cleanup.AsEnumerable().Reverse())
        {
            try
            {
                await client.DeleteAsync($"{endpoint}/{id}");
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
