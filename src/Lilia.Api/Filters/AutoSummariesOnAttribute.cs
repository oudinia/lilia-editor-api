using System.Security.Claims;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lilia.Api.Filters;

/// <summary>
/// Automatic document summaries are opt-in (Oussama, 26 Nov 2026, under GDPR): while the person hasn't turned them on
/// in Settings, the action answers 403 <c>summaries_off</c> and nothing reaches the AI provider. It runs before
/// <see cref="MeteredAiAttribute"/> (lower Order), so the allowance is neither checked nor touched and a Free account
/// gets the same answer as any other.
/// </summary>
public sealed class AutoSummariesOnAttribute : TypeFilterAttribute
{
    public AutoSummariesOnAttribute() : base(typeof(AutoSummariesOnFilter)) { Order = -100; }

    private sealed class AutoSummariesOnFilter(IPreferencesService preferences) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                context.Result = new UnauthorizedResult();
                return;
            }
            if (!await preferences.AutoDocumentSummariesAsync(userId, context.HttpContext.RequestAborted))
            {
                context.Result = new ObjectResult(new
                {
                    error = "summaries_off",
                    message = "Automatic document summaries are off. Turn them on in Settings to see a one-line summary of each document.",
                }) { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
            await next();
        }
    }
}
