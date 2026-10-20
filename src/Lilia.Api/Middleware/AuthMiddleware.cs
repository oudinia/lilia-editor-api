using System.Security.Claims;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.Extensions.Caching.Distributed;

namespace Lilia.Api.Middleware;

/// <summary>
/// Development-only middleware that creates a fake authenticated user
/// when no Authorization header is provided. This allows local testing
/// without requiring authentication.
///
/// IMPORTANT: This middleware only activates when Auth is NOT configured.
/// If Auth:Authority is set, this middleware does nothing, allowing
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

    public DevelopmentAuthMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment env, ILogger<DevelopmentAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        var explicitlyDisabled = configuration.GetValue<bool>("Auth:DevelopmentAuth:Disabled");
        _enabled = IsEnabled(configuration, env.IsDevelopment());

        if (_enabled)
        {
            _logger.LogWarning("DevelopmentAuthMiddleware is ENABLED - Auth:Authority is not configured");
        }
        else if (!explicitlyDisabled && env.IsDevelopment() && !string.IsNullOrEmpty(configuration["Stytch:ProjectId"]))
        {
            _logger.LogInformation("DevelopmentAuthMiddleware is DISABLED - Stytch sign-in is configured (set Auth:DevelopmentAuth:Enabled=true on a test stack)");
        }
        else if (explicitlyDisabled)
        {
            _logger.LogInformation("DevelopmentAuthMiddleware is DISABLED - Auth:DevelopmentAuth:Disabled=true (e.g. E2E run)");
        }
        else
        {
            _logger.LogInformation("DevelopmentAuthMiddleware is DISABLED - Auth provider is configured");
        }
    }

    /// <summary>
    /// Whether requests without a token are signed in as a development user.
    ///
    /// <para>Never when real sign-in (Stytch) is configured, unless a test stack
    /// asks for it with <c>Auth:DevelopmentAuth:Enabled=true</c>. On 10 Oct 2026
    /// the production API, run in Development and reachable through
    /// editor.liliaeditor.com, served anyone's unsigned request as the owner's
    /// account, because only <c>Auth:Authority</c> (unused since Stytch) turned
    /// this off.</para>
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration, bool isDevelopment)
    {
        if (!isDevelopment) return false;
        if (configuration.GetValue<bool>("Auth:DevelopmentAuth:Disabled")) return false;
        if (!string.IsNullOrEmpty(configuration["Auth:Authority"])) return false;
        var realSignIn = !string.IsNullOrEmpty(configuration["Stytch:ProjectId"]);
        return !realSignIn || configuration.GetValue<bool>("Auth:DevelopmentAuth:Enabled");
    }

    private static bool IsPlainEmail(string? s) =>
        !string.IsNullOrEmpty(s) && s.Length <= 200 && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled && !context.Request.Headers.ContainsKey("Authorization"))
        {
            _logger.LogDebug("Using development auth for request {Path}", context.Request.Path);

            // Allow callers to override the dev user via header (useful for test tools)
            var userId = context.Request.Headers["X-Development-User-Id"].FirstOrDefault()
                      ?? DevUserId;
            // Any address for a test person (the test copy's "Sign in as… Other", 11 Oct), so an
            // invitation sent to friend@gmail.com matches the person signing in as it.
            var emailHeader = context.Request.Headers["X-Development-User-Email"].FirstOrDefault()?.Trim();
            var userEmail = userId == DevUserId ? DevUserEmail
                          : IsPlainEmail(emailHeader) ? emailHeader!
                          : $"{userId}@dev.liliaeditor.com";
            // A display name for test people (the test copy's "Sign in as…", 11 Oct).
            var nameHeader = context.Request.Headers["X-Development-User-Name"].FirstOrDefault()?.Trim();
            var userName  = userId == DevUserId ? DevUserName
                          : !string.IsNullOrEmpty(nameHeader) && nameHeader.Length <= 80 ? nameHeader
                          : $"Dev User ({userId})";

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("sub", userId),
                new Claim(ClaimTypes.Email, userEmail),
                new Claim("email", userEmail),
                new Claim(ClaimTypes.Name, userName),
                new Claim("name", userName),
            };

            var identity = new ClaimsIdentity(claims, "Development");
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }
}

public class UserSyncMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserSyncMiddleware> _logger;
    private static readonly TimeSpan SyncCacheDuration = TimeSpan.FromMinutes(30);

    public UserSyncMiddleware(RequestDelegate next, ILogger<UserSyncMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Whether <paramref name="email"/> is free, or already this user's: emails are unique.</summary>
    private static async Task<bool> EmailFreeForAsync(IUserService users, string email, string userId) =>
        await users.GetUserByEmailAsync(email) is not { } holder || holder.Id == userId;

    public async Task InvokeAsync(HttpContext context, IUserService userService, IDistributedCache cache, IStytchUserLookup? stytchUsers = null)
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

                // Try to get email/name from JWT claims (supports Stytch + standard OIDC)
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
                    // Stytch's tokens carry no email by default: ask Stytch (when Lilia has its secret) for a
                    // new user, and for one still on the stand-in address (the first Google sign-up, 20 Oct).
                    if ((existingUser is null || StytchUserLookup.IsPlaceholder(existingUser.Email))
                        && stytchUsers is not null && await stytchUsers.FindAsync(userId, context.RequestAborted) is { Email: { Length: > 0 } found } info
                        // Emails are unique: one another account already holds stays with it.
                        && await EmailFreeForAsync(userService, found, userId))
                    {
                        email = found;
                        if (string.IsNullOrWhiteSpace(name)) name = info.Name;
                        _logger.LogInformation("[Stytch] email for {UserId} read from Stytch", userId);
                        existingUser = null;   // sync below, with the real address
                    }
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

                    if (string.IsNullOrEmpty(email))
                    {
                        // User not in DB, no email in JWT, and Stytch can't say: a placeholder email
                        _logger.LogWarning("Email not in JWT for new user {UserId}, using placeholder", userId);
                        email = $"{userId}{StytchUserLookup.PlaceholderDomain}";
                    }
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

/// <summary>
/// Maps M2M (client_credentials) JWT tokens to a service account user.
/// M2M tokens have no "sub" claim — this middleware injects one so that
/// downstream middleware and controllers can identify the caller.
/// Only activates when Auth:M2M:Enabled is true.
/// </summary>
public class M2MAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<M2MAuthMiddleware> _logger;
    private readonly bool _enabled;
    private readonly string _serviceUserId;
    private readonly string _serviceUserEmail;
    private readonly string _serviceUserName;

    public M2MAuthMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<M2MAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _enabled = configuration.GetValue<bool>("Auth:M2M:Enabled");
        _serviceUserId = configuration["Auth:M2M:ServiceUserId"] ?? "svc_m2m_e2e";
        _serviceUserEmail = configuration["Auth:M2M:ServiceUserEmail"] ?? "m2m@liliaeditor.com";
        _serviceUserName = configuration["Auth:M2M:ServiceUserName"] ?? "M2M Service Account";

        if (_enabled)
            _logger.LogInformation("M2M auth middleware enabled — service user: {UserId}", _serviceUserId);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled
            && context.User.Identity?.IsAuthenticated == true
            && string.IsNullOrEmpty(context.User.FindFirst("sub")?.Value))
        {
            // Check if this is a client_credentials token (has "gty" claim with "client_credentials")
            var gtyClaim = context.User.FindFirst("gty")?.Value;
            if (gtyClaim != null && gtyClaim.Contains("client_credentials"))
            {
                _logger.LogDebug("M2M token detected, mapping to service user {UserId}", _serviceUserId);

                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, _serviceUserId),
                    new Claim("sub", _serviceUserId),
                    new Claim(ClaimTypes.Email, _serviceUserEmail),
                    new Claim("email", _serviceUserEmail),
                    new Claim(ClaimTypes.Name, _serviceUserName),
                    new Claim("name", _serviceUserName),
                };

                var identity = new ClaimsIdentity(claims, "M2M");
                context.User.AddIdentity(identity);
            }
        }

        await _next(context);
    }
}

public static class AuthMiddlewareExtensions
{
    public static IApplicationBuilder UseUserSync(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserSyncMiddleware>();
    }

    public static IApplicationBuilder UseDevelopmentAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DevelopmentAuthMiddleware>();
    }

    public static IApplicationBuilder UseM2MAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<M2MAuthMiddleware>();
    }
}

public static class OidcClaimTypes
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Image = "picture";
}
