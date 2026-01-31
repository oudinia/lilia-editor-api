using System.Security.Claims;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Middleware;

/// <summary>
/// Development-only middleware that creates a fake authenticated user
/// when no Authorization header is provided. This allows local testing
/// without requiring Clerk authentication.
///
/// IMPORTANT: This middleware only activates when Clerk is NOT configured.
/// If Clerk:SecretKey is set, this middleware does nothing, allowing
/// unauthenticated requests to properly fail with 401.
/// </summary>
public class DevelopmentAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DevelopmentAuthMiddleware> _logger;
    private readonly bool _enabled;
    private const string DevUserId = "dev_user_001";
    private const string DevUserEmail = "dev@lilia.local";
    private const string DevUserName = "Development User";

    public DevelopmentAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<DevelopmentAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        // Only enable dev auth when Clerk is NOT configured
        var clerkSecretKey = configuration["Clerk:SecretKey"];
        _enabled = string.IsNullOrEmpty(clerkSecretKey);

        if (_enabled)
        {
            _logger.LogWarning("DevelopmentAuthMiddleware is ENABLED - Clerk:SecretKey is not configured");
        }
        else
        {
            _logger.LogInformation("DevelopmentAuthMiddleware is DISABLED - Clerk authentication is configured");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply if:
        // 1. Dev auth is enabled (Clerk is not configured)
        // 2. No Authorization header is present
        if (_enabled && !context.Request.Headers.ContainsKey("Authorization"))
        {
            _logger.LogDebug("Using development auth for request {Path}", context.Request.Path);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, DevUserId),
                new Claim("sub", DevUserId),
                new Claim(ClaimTypes.Email, DevUserEmail),
                new Claim("email", DevUserEmail),
                new Claim(ClaimTypes.Name, DevUserName),
                new Claim("name", DevUserName),
            };

            var identity = new ClaimsIdentity(claims, "Development");
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }
}

public class ClerkUserSyncMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClerkUserSyncMiddleware> _logger;

    public ClerkUserSyncMiddleware(RequestDelegate next, ILogger<ClerkUserSyncMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserService userService, IClerkService clerkService)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Try to get email/name from JWT claims first
            var email = context.User.FindFirst("email")?.Value
                     ?? context.User.FindFirst(ClaimTypes.Email)?.Value;
            var name = context.User.FindFirst("name")?.Value
                    ?? context.User.FindFirst(ClaimTypes.Name)?.Value;
            var image = context.User.FindFirst("picture")?.Value;

            _logger.LogDebug("JWT Claims - UserId: {UserId}, Email: {Email}, Name: {Name}",
                userId ?? "NULL", email ?? "NULL", name ?? "NULL");

            // If email is missing from JWT, fetch from Clerk API
            if (!string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(email))
            {
                _logger.LogInformation("Email not in JWT, fetching from Clerk API for user {UserId}", userId);

                var clerkUser = await clerkService.GetUserAsync(userId);
                if (clerkUser != null)
                {
                    email = clerkUser.PrimaryEmail;
                    name ??= clerkUser.FullName;
                    image ??= clerkUser.ImageUrl;

                    _logger.LogInformation("Fetched from Clerk API - Email: {Email}, Name: {Name}",
                        email ?? "NULL", name ?? "NULL");
                }
                else
                {
                    _logger.LogWarning("Could not fetch user data from Clerk API for {UserId}", userId);
                }
            }

            // Sync user to database if we have the required data
            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(email))
            {
                try
                {
                    await userService.CreateOrUpdateUserAsync(new CreateOrUpdateUserDto(
                        userId,
                        email,
                        name,
                        image
                    ));
                    _logger.LogDebug("User {UserId} synced to database", userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync user {UserId} to database", userId);
                    throw;
                }
            }
            else
            {
                _logger.LogWarning("Cannot sync user - missing required data. UserId: {UserId}, Email: {Email}",
                    userId ?? "NULL", email ?? "NULL");
            }
        }

        await _next(context);
    }
}

public static class ClerkAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseClerkUserSync(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ClerkUserSyncMiddleware>();
    }

    public static IApplicationBuilder UseDevelopmentAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DevelopmentAuthMiddleware>();
    }
}

public static class ClerkClaimTypes
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Image = "picture";
}
