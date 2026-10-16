using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lilia.Admin.Security;

/// <summary>
/// Who may open the admin console. Two locks: Cloudflare Access in front (admin.liliaeditor.com,
/// "Only Oussama"), and this app, which checks the token Access adds to every request
/// (<c>Cf-Access-Jwt-Assertion</c>: signature, issuer, audience, expiry) and that its email is in
/// <c>Admin:Emails</c>. A request that didn't come through Access has no token and is refused.
/// </summary>
public static class AdminAccess
{
    public const string Scheme = "CloudflareAccess";
    public const string DevScheme = "Development";
    public const string Policy = "Admin";
    public const string Header = "Cf-Access-Jwt-Assertion";

    public static void AddAdminAccess(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var team = (config["Access:TeamDomain"] ?? "").TrimEnd('/');
        var audience = config["Access:Audience"] ?? "";
        // Development only, and only from this machine: sign in as this email without Access.
        var devUser = builder.Environment.IsDevelopment() ? config["Admin:DevUser"] : null;
        var scheme = string.IsNullOrEmpty(devUser) ? Scheme : DevScheme;
        if (scheme == Scheme && (team.Length == 0 || audience.Length == 0))
            throw new InvalidOperationException(
                "Access:TeamDomain and Access:Audience must be set: the admin console only opens through Cloudflare Access.");

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<CloudflareAccessKeys>();
        builder.Services.AddSingleton<IAccessKeys>(sp => sp.GetRequiredService<CloudflareAccessKeys>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CloudflareAccessKeys>());

        var auth = builder.Services.AddAuthentication(scheme);
        auth.AddJwtBearer(Scheme, o =>
        {
            o.MapInboundClaims = false;
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    ctx.Token = ctx.Request.Headers[Header].FirstOrDefault();
                    return Task.CompletedTask;
                },
            };
        });
        builder.Services.AddOptions<JwtBearerOptions>(Scheme).Configure<IAccessKeys>((o, keys) =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = team,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                IssuerSigningKeyResolver = (_, _, kid, _) => keys.Current(kid),
            };
        });
        if (scheme == DevScheme)
            auth.AddScheme<DevUserOptions, DevUserHandler>(DevScheme, o => o.Email = devUser!);

        var admins = config["Admin:Emails"];
        builder.Services.AddAuthorization(o =>
        {
            var policy = new AuthorizationPolicyBuilder(scheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => IsAdmin(ctx.User, admins))
                .Build();
            o.AddPolicy(Policy, policy);
            o.FallbackPolicy = policy;
        });
        builder.Services.AddCascadingAuthenticationState();
    }

    /// <summary>The signed-in email is one of <c>Admin:Emails</c> (comma or semicolon separated).</summary>
    public static bool IsAdmin(ClaimsPrincipal user, string? admins)
    {
        var email = user.FindFirst("email")?.Value;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(admins)) return false;
        return admins.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(a => string.Equals(a, email, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DevUserOptions : AuthenticationSchemeOptions
{
    public string Email { get; set; } = "";
}

/// <summary>Development on this machine: signs requests from loopback in as <c>Admin:DevUser</c>.</summary>
public sealed class DevUserHandler(IOptionsMonitor<DevUserOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<DevUserOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var remote = Context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim("email", Options.Email)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
