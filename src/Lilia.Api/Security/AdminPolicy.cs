using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Lilia.Api.Security;

/// <summary>
/// "Admin": an operator of this installation, not a customer. There was no such
/// concept: the Typst coverage admin routes and the compile-queue metrics asked only
/// for a login, so any signed-in user could read them.
///
/// <para>An admin is a user in the <c>admin</c> role (the claim the plan gate already
/// honours), or one listed in configuration: <c>Auth:Admins</c>, a comma-separated list
/// of user ids or e-mail addresses (<c>Auth__Admins</c> as an environment variable).
/// Nobody is an admin by default: with the list empty these routes answer 403 to
/// everyone, which is the safe failure.</para>
/// </summary>
public static class AdminPolicy
{
    public const string Name = "Admin";

    public static void Register(AuthorizationOptions options, IConfiguration config) =>
        options.AddPolicy(Name, p => p.RequireAuthenticatedUser()
            .RequireAssertion(ctx => IsAdmin(ctx.User, config["Auth:Admins"])));

    public static bool IsAdmin(ClaimsPrincipal user, string? configured)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (user.IsInRole("admin")) return true;
        var admins = (configured ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (admins.Length == 0) return false;
        var ids = new[]
        {
            user.FindFirst("sub")?.Value,
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            user.FindFirst("email")?.Value,
            user.FindFirst(ClaimTypes.Email)?.Value,
        };
        return ids.Any(i => !string.IsNullOrEmpty(i) && admins.Contains(i, StringComparer.OrdinalIgnoreCase));
    }
}
