using Lilia.Api.Security;
using Lilia.Core.Interfaces;
using Microsoft.Net.Http.Headers;

namespace Lilia.Api.Features.Platform;

/// <summary>
/// <c>GET /uploads/{key}</c>: an uploaded file, streamed from the configured store (21 Nov 2026).
///
/// <para>Documents store their images as <c>/uploads/{key}</c> (Storage:LocalPublicUrl is "/uploads" in production),
/// and the editor loads them with plain &lt;img&gt; tags. The route was static files over the local folder; production's
/// files now live in a private R2 bucket, so the API reads each one from the store and streams it. Every stored URL
/// keeps working, and no file is reachable from outside except through here.</para>
///
/// <para>The rules: no sign-in (an image tag sends none; the key, a person's id and two random ids, is the permission,
/// as before). A key with "..", a backslash or an empty segment is refused. The kept originals of imports
/// (<c>imports/</c>, <c>bulk-imports/</c>, <c>bulk-staging/</c>) are not served: the API reads them itself and no page loads them by URL.
/// A file is never run as a page on the editor's domain: a sandboxing Content-Security-Policy, and anything that is not
/// an image or a PDF comes as a download.</para>
/// </summary>
public static class UploadsEndpoint
{
    public static IEndpointConventionBuilder MapUploads(this IEndpointRouteBuilder app) =>
        app.MapMethods("/uploads/{**key}", [HttpMethods.Get, HttpMethods.Head], ServeAsync).AllowAnonymous();

    internal static async Task<IResult> ServeAsync(string? key, IStorageService storage, HttpContext http, ILoggerFactory logs)
    {
        if (!StorageKeys.IsServed(key)) return Results.NotFound();
        StoredFile? file;
        try
        {
            file = await storage.OpenReadAsync(key!, http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logs.CreateLogger("Lilia.Uploads").LogWarning(ex, "[Uploads] Could not read {Key} from storage", key);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (file is null) return Results.NotFound();

        http.Response.RegisterForDisposeAsync(file);
        var headers = http.Response.Headers;
        // Keys never change content (each upload gets a new id): a browser may keep the file a day, a shared cache never.
        headers[HeaderNames.CacheControl] = "private, max-age=86400";
        var pdf = file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
        var image = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        // An SVG opened on its own runs no script. Not on a PDF: Chrome won't show a PDF in a sandboxed document.
        if (!pdf) headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";
        if (!image && !pdf) headers[HeaderNames.ContentDisposition] = "attachment";

        return Results.Stream(file.Content, file.ContentType,
            lastModified: file.LastModified,
            entityTag: file.ETag is { } e && EntityTagHeaderValue.TryParse(e, out var tag) ? tag : null,
            enableRangeProcessing: file.Content.CanSeek);
    }
}
