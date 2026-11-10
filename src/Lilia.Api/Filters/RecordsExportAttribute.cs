using System.Security.Claims;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lilia.Api.Filters;

/// <summary>
/// Records a download from the export screens as an operation (<see cref="OperationLog"/>; paying per operation, 10 Nov
/// 2026). Only when the request says it is a download (<c>download=true</c>): the editor's live preview fetches the same
/// PDF route and is not an export. Only a file answer counts, not a 202 "still preparing" or an error. The LaTeX route
/// names its format by <c>mode</c>: <c>zip</c> (the default) or <c>tex</c>; <c>preview</c> shows the source and isn't one.
/// </summary>
public sealed class RecordsExportAttribute(string format) : TypeFilterAttribute(typeof(RecordsExportFilter))
{
    public string Format { get; } = format;

    private sealed class RecordsExportFilter(OperationLog operations) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var executed = await next();
            var request = context.HttpContext.Request;
            if (executed.Exception is not null || executed.Result is not FileResult) return;
            if (!bool.TryParse(request.Query["download"], out var download) || !download) return;

            var attribute = context.ActionDescriptor.EndpointMetadata.OfType<RecordsExportAttribute>().First();
            var format = attribute.Format;
            if (format == "latex")
            {
                var mode = ((string?)request.Query["mode"])?.Trim().ToLowerInvariant();
                if (mode == "preview") return;
                format = mode == "tex" ? "tex" : "zip";
            }

            var user = context.HttpContext.User;
            var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return;
            var docId = context.RouteData.Values.TryGetValue("docId", out var raw) && Guid.TryParse(raw?.ToString(), out var id) ? id : (Guid?)null;
            await operations.RecordExportAsync(userId, docId, format, "screens");
        }
    }
}
