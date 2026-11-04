using System.Security.Claims;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lilia.Api.Filters;

/// <summary>
/// The editor's quick AI actions (generate a block, revise a block or table, improve text, suggest an
/// equation, the grammar and citation checks, an abstract) spend from the author's Ask Lilia allowance and
/// the site's AI budget, like Ask Lilia itself (4 Nov 2026). Before, they were neither checked nor
/// counted: a launch on a fixed, prepaid AI budget had a door left open. Checked before the action (402
/// when there's no room, through <see cref="ExceptionLoggingFilter"/>), and debited after it with what
/// the request's model calls used (<see cref="IAiService.Spent"/>).
/// </summary>
public sealed class MeteredAiAttribute : TypeFilterAttribute
{
    public MeteredAiAttribute() : base(typeof(MeteredAiFilter)) { }

    private sealed class MeteredAiFilter(
        IEntitlementService entitlements, IAiService ai, IConfiguration configuration, ILogger<MeteredAiFilter> logger)
        : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null)
            {
                await next();
                return;
            }

            try
            {
                await entitlements.EnsureQuotaAsync(userId, QuotaResource.AiCredits, delta: 1, context.HttpContext.RequestAborted);
            }
            catch (QuotaExceededException ex) when (ex.Resource == "aiCreditsPerMonth" && !configuration.GetValue("AI:EnforceCredits", false))
            {
                // Allowances only log until AI:EnforceCredits; the site's budget always holds (it rethrows).
                logger.LogInformation("[Ai] User {UserId} over the allowance; credit enforcement off — allowing", userId);
            }

            await next();

            var (model, use) = ai.Spent;
            if (model is null || use.Input + use.Output == 0) return;
            try
            {
                // Not the request's token: an author who closes the page has still spent it.
                await entitlements.RecordAiSpendAsync(userId, model, use, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Ai] Could not record {Model} spend for user {UserId}", model, userId);
            }
        }
    }
}
