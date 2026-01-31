using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lilia.Api.Services;

public interface IClerkService
{
    Task<ClerkUser?> GetUserAsync(string userId);
}

public class ClerkService : IClerkService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClerkService> _logger;
    private readonly string? _secretKey;

    public ClerkService(HttpClient httpClient, IConfiguration configuration, ILogger<ClerkService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _secretKey = configuration["Clerk:SecretKey"];

        if (!string.IsNullOrEmpty(_secretKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _secretKey);
        }
    }

    public async Task<ClerkUser?> GetUserAsync(string userId)
    {
        if (string.IsNullOrEmpty(_secretKey))
        {
            _logger.LogWarning("Clerk:SecretKey not configured, cannot fetch user data");
            return null;
        }

        try
        {
            var response = await _httpClient.GetAsync($"https://api.clerk.com/v1/users/{userId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch Clerk user {UserId}: {StatusCode}",
                    userId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<ClerkUser>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Clerk user {UserId}", userId);
            return null;
        }
    }
}

public class ClerkUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email_addresses")]
    public List<ClerkEmailAddress> EmailAddresses { get; set; } = new();

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("primary_email_address_id")]
    public string? PrimaryEmailAddressId { get; set; }

    public string? PrimaryEmail =>
        EmailAddresses.FirstOrDefault(e => e.Id == PrimaryEmailAddressId)?.EmailAddress
        ?? EmailAddresses.FirstOrDefault()?.EmailAddress;

    public string? FullName =>
        string.IsNullOrEmpty(FirstName) && string.IsNullOrEmpty(LastName)
            ? null
            : $"{FirstName} {LastName}".Trim();
}

public class ClerkEmailAddress
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email_address")]
    public string EmailAddress { get; set; } = "";
}
