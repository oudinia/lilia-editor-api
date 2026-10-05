using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lilia.Api.Filters;

/// <summary>
/// The caller must be allowed to use the document named in the route
/// (<c>{docId}</c> or <c>{documentId}</c>). Put it on a controller, and on an action to
/// override the default for that action.
///
/// <para>Default permission by method: GET, HEAD and OPTIONS need <c>read</c>; anything else
/// needs <c>write</c>. Pass one explicitly for a POST that only reads (a preview, a
/// render): <c>[RequireDocumentAccess(Permissions.Read)]</c>.</para>
///
/// <para>Why it exists: StudioController, the Typst export, the LaTeX render routes and the
/// document hints answered any signed-in user who knew a document id, because each action
/// had to remember to call <c>HasAccessAsync</c> and these did not (cloud session audit,
/// 3 Oct 2026). This runs as an authorization filter, so it is before model binding and
/// before any compile or AI call. 401 without a user, 403 without access, matching
/// BlocksController. Several attributes may apply (controller and action): the most specific
/// one decides, and the check runs once.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireDocumentAccessAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const string DoneKey = "Lilia.DocumentAccessChecked";

    /// <summary>Null: decide by HTTP method.</summary>
    public string? Permission { get; }

    public RequireDocumentAccessAttribute() { }
    public RequireDocumentAccessAttribute(string permission) => Permission = permission;

    public static string PermissionFor(string? explicitPermission, string httpMethod) =>
        explicitPermission ?? (HttpMethods.IsGet(httpMethod) || HttpMethods.IsHead(httpMethod) || HttpMethods.IsOptions(httpMethod)
            ? Permissions.Read
            : Permissions.Write);

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (http.Items.ContainsKey(DoneKey)) return;   // another instance already decided
        http.Items[DoneKey] = true;

        // The most specific attribute (the action's, else the controller's) decides.
        var effective = context.ActionDescriptor.EndpointMetadata.OfType<RequireDocumentAccessAttribute>().LastOrDefault() ?? this;

        var userId = http.User.FindFirst("sub")?.Value
                  ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) { context.Result = new UnauthorizedResult(); return; }

        var values = context.RouteData.Values;
        var raw = values.TryGetValue("docId", out var a) ? a : values.TryGetValue("documentId", out var b) ? b : null;
        if (raw is null || !Guid.TryParse(raw.ToString(), out var documentId))
        {
            // No document id in this route: a programming error (the attribute is on the wrong
            // route), not something to let through.
            context.Result = new ObjectResult(new { message = "document id missing from the route" }) { StatusCode = 500 };
            return;
        }

        var documents = http.RequestServices.GetRequiredService<IDocumentService>();
        if (!await documents.HasAccessAsync(documentId, userId, PermissionFor(effective.Permission, http.Request.Method)))
            context.Result = new ForbidResult();
    }
}
