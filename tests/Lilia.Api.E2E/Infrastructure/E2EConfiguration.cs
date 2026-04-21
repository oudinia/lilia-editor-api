using Microsoft.Extensions.Configuration;

namespace Lilia.Api.E2E.Infrastructure;

/// <summary>
/// Configuration for E2E tests. Reads from appsettings.e2e.json and environment variables.
/// Environment variables override JSON settings (e.g., E2E__ApiBaseUrl, E2E__AuthMode).
/// </summary>
public sealed class E2EConfiguration
{
    private static readonly Lazy<E2EConfiguration> _instance = new(Load);
    public static E2EConfiguration Instance => _instance.Value;

    public string ApiBaseUrl { get; init; } = "http://localhost:5001";
    public string AuthMode { get; init; } = "DevJwt"; // "DevJwt" | "Kinde" | "StaticToken"
    public string StaticToken { get; init; } = "";
    public KindeConfig Kinde { get; init; } = new();
    public Dictionary<string, TestUserConfig> TestUsers { get; init; } = new();

    private static E2EConfiguration Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.e2e.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var e2e = new E2EConfiguration();
        config.GetSection("E2E").Bind(e2e);
        return e2e;
    }
}

public sealed class KindeConfig
{
    public string Domain { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Audience { get; set; } = "";
}

public sealed class TestUserConfig
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}
