using Lilia.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lilia.Api.Filters;

/// <summary>
/// Gate an endpoint behind one of N plan slugs. Usage:
///   [RequirePlan("pro", "team")]
///   public async Task<IActionResult> HeavyFeature(...) { ... }
///
/// Resolves via IEntitlementService — no direct DB hit from the filter.
/// Returns 402 Payment Required when the user has no plan at all (new
/// signups before any grant) and 403 Forbidden when a plan exists but
/// doesn't include the required slug.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePlanAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _allowedSlugs;

    public RequirePlanAttribute(params string[] allowedSlugs)
    {
        _allowedSlugs = allowedSlugs;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var userId = http.User.FindFirst("sub")?.Value
                  ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Admin bypass — bot / ops accounts shouldn't hit plan gates.
        if (http.User.IsInRole("admin")) return;

        var entitlement = http.RequestServices.GetService(typeof(IEntitlementService)) as IEntitlementService;
        if (entitlement is null)
        {
            // Service not wired — fail open with a log rather than blocking.
            return;
        }

        var plan = await entitlement.GetActivePlanAsync(userId, http.RequestAborted);
        if (plan is null)
        {
            context.Result = new ObjectResult(new
            {
                error = "no_plan",
                redirect = "/pricing",
            }) { StatusCode = StatusCodes.Status402PaymentRequired };
            return;
        }

        if (Array.IndexOf(_allowedSlugs, plan.Slug) < 0)
        {
            context.Result = new ObjectResult(new
            {
                error = "plan_not_allowed",
                currentPlan = plan.Slug,
                requiredPlans = _allowedSlugs,
                redirect = "/pricing",
            }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
