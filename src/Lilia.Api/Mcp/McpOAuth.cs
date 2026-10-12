using System.Security.Claims;

namespace Lilia.Api.Mcp;

/// <summary>
/// Sign-in for outside apps (MCP step 2; the user, 12 Oct: "go ahead with step 2"). Claude, ChatGPT
/// or another MCP client signs the author in through Stytch Connected Apps:
///
/// <list type="number">
/// <item>The client calls <c>/api/mcp</c> without a token and gets a 401 whose
///   <c>WWW-Authenticate</c> names this server's resource metadata (RFC 9728).</item>
/// <item>The metadata names the authorization server: our Stytch project's domain. The client
///   registers itself there (dynamic client registration) and opens Stytch's authorize URL, which is
///   the editor's consent page, <c>/oauth/authorize</c> (Stytch's <c>IdentityProvider</c>).</item>
/// <item>The author signs in to Lilia if needed and approves; Stytch gives the client an access token:
///   a JWT signed with the same keys as Lilia's session tokens, with a <c>client_id</c> claim.</item>
/// </list>
///
/// <para>Such a token is accepted at <c>/api/mcp</c> only. It is the author's consent to the MCP tools,
/// not to the whole API: the rest of <c>/api</c> refuses it (<see cref="RejectOutsideMcp"/>).</para>
/// </summary>
public static class McpOAuth
{
    public const string Endpoint = "/api/mcp";

    /// <summary>RFC 9728's well-known path for the resource <c>…/api/mcp</c>.</summary>
    public const string MetadataPath = "/.well-known/oauth-protected-resource/api/mcp";

    /// <summary>
    /// The address apps use to reach this server. Apps check that the metadata's resource is the
    /// URL they connected to, so it is set on its own: production's Email:BaseUrl is the machine's
    /// address (12 Oct), and the test copy sets https://test.liliaeditor.com.
    /// </summary>
    private static string PublicOrigin(IConfiguration config) =>
        (config["Mcp:PublicOrigin"] ?? config["Email:BaseUrl"] ?? "https://editor.liliaeditor.com").TrimEnd('/');

    /// <summary>The Stytch project domain that issues the tokens (no trailing slash).</summary>
    public static string? AuthorizationServer(IConfiguration config) =>
        config["Mcp:AuthorizationServer"]?.TrimEnd('/') is { Length: > 0 } s ? s : null;

    public static object Metadata(IConfiguration config) => new Dictionary<string, object?>
    {
        ["resource"] = PublicOrigin(config) + Endpoint,
        ["authorization_servers"] = AuthorizationServer(config) is { } a ? new[] { a } : Array.Empty<string>(),
        ["scopes_supported"] = new[] { "openid", "email", "profile" },
        ["bearer_methods_supported"] = new[] { "header" },
        ["resource_name"] = "Lilia",
        ["resource_documentation"] = PublicOrigin(config) + "/help",
    };

    /// <summary>The 401 at /api/mcp says where the metadata is, whichever scheme refused the request.</summary>
    public static IApplicationBuilder UseMcpChallenge(this IApplicationBuilder app)
    {
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var header = $"Bearer resource_metadata=\"{PublicOrigin(config)}{MetadataPath}\"";
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(Endpoint))
            {
                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                        context.Response.Headers.WWWAuthenticate = header;
                    return Task.CompletedTask;
                });
            }
            await next();
        });
    }

    public static IEndpointRouteBuilder MapMcpMetadata(this IEndpointRouteBuilder app)
    {
        var config = app.ServiceProvider.GetRequiredService<IConfiguration>();
        app.MapGet(MetadataPath, () => Results.Json(Metadata(config))).AllowAnonymous();
        // Clients that read the resource's own path first (older drafts).
        app.MapGet("/.well-known/oauth-protected-resource", () => Results.Json(Metadata(config))).AllowAnonymous();
        return app;
    }

    /// <summary>A Stytch Connected Apps access token: it names the app it was issued to.</summary>
    public static bool IsConnectedAppToken(ClaimsPrincipal? user) => user?.FindFirst("client_id") is not null;

    /// <summary>
    /// An app's token reaches the MCP tools and nothing else: without this, an app the author
    /// allowed to read documents through Claude could call any endpoint, deletes included.
    /// Runs after authentication, whatever scheme signed the request in.
    /// </summary>
    public static IApplicationBuilder UseConnectedAppBoundary(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!AllowedHere(context.User, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "connected_app_token",
                    message = "This token was issued to a connected app; it is only accepted at /api/mcp.",
                });
                return;
            }
            await next();
        });

    public static bool AllowedHere(ClaimsPrincipal? user, PathString path) =>
        !IsConnectedAppToken(user) || path.StartsWithSegments(Endpoint);
}
