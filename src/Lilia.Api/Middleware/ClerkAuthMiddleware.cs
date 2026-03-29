using System.Security.Claims;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.Extensions.Caching.Distributed;

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
    private const string DevUserId = "user_2wh3MeURGR4xLFPfiWWlx9OoWQ8";
    private const string DevUserEmail = "oussama.dinia@gmail.com";
    private const string DevUserName = "Oussama Dinia";

    public DevelopmentAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<DevelopmentAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        var authAuthority = configuration["Auth:Authority"] ?? configuration["Clerk:Authority"];
        _enabled = string.IsNullOrEmpty(authAuthority);

        if (_enabled)
        {
            _logger.LogWarning("DevelopmentAuthMiddleware is ENABLED - Auth:Authority is not configured");
        }
        else
        {
            _logger.LogInformation("DevelopmentAuthMiddleware is DISABLED - Auth provider is configured");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
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
    private static readonly TimeSpan SyncCacheDuration = TimeSpan.FromMinutes(30);

    public ClerkUserSyncMiddleware(RequestDelegate next, ILogger<ClerkUserSyncMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserService userService, IDistributedCache cache)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // Check if user was recently synced (skip all DB and API calls)
                var cacheKey = $"usersync:{userId}";
                var cached = await cache.GetAsync(cacheKey);
                if (cached != null)
                {
                    _logger.LogDebug("User {UserId} was recently synced, skipping", userId);
                    await _next(context);
                    return;
                }

                // Try to get email/name from JWT claims (supports Kinde, Clerk, and standard OIDC)
                var email = context.User.FindFirst("email")?.Value
                         ?? context.User.FindFirst(ClaimTypes.Email)?.Value
                         ?? context.User.FindFirst("preferred_username")?.Value;
                var name = context.User.FindFirst("name")?.Value
                        ?? context.User.FindFirst(ClaimTypes.Name)?.Value
                        ?? context.User.FindFirst("given_name")?.Value;
                var image = context.User.FindFirst("picture")?.Value;

                _logger.LogDebug("JWT Claims - UserId: {UserId}, Email: {Email}, Name: {Name}",
                    userId, email ?? "NULL", name ?? "NULL");

                // If email is missing from JWT, check if user already exists in DB
                if (string.IsNullOrEmpty(email))
                {
                    var existingUser = await userService.GetUserAsync(userId);
                    if (existingUser != null)
                    {
                        // User already in DB, no need to sync — just cache and move on
                        await cache.SetAsync(cacheKey, [1], new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = SyncCacheDuration
                        });
                        await _next(context);
                        return;
                    }

                    // User not in DB and no email in JWT — use a placeholder email
                    _logger.LogWarning("Email not in JWT for new user {UserId}, using placeholder", userId);
                    email = $"{userId}@auth.liliaeditor.com";
                }

                // Sync user to database if we have the required data
                if (!string.IsNullOrEmpty(email))
                {
                    try
                    {
                        await userService.CreateOrUpdateUserAsync(new CreateOrUpdateUserDto(
                            userId,
                            email,
                            name,
                            image
                        ));

                        // Mark as synced in distributed cache (TTL handles cleanup)
                        await cache.SetAsync(cacheKey, [1], new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = SyncCacheDuration
                        });

                        _logger.LogDebug("User {UserId} synced to database and cached", userId);
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
                        userId, email ?? "NULL");
                }
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
