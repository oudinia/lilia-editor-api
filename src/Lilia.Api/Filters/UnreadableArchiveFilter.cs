using Lilia.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lilia.Api.Filters;

/// <summary>
/// An uploaded archive the server can't or won't read is the upload's fault:
/// a 400 with a plain reason, not a 500. <see cref="UnsafeZipException"/> is
/// a zip-bomb limit (<see cref="ZipLimitsOptions"/>); InvalidDataException
/// and XmlException are an archive or document that isn't what it claims.
/// </summary>
/// <remarks>Worded for books: it guards the ePub routes.</remarks>
public sealed class UnreadableArchiveFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var logger = context.HttpContext.RequestServices.GetService<ILogger<UnreadableArchiveFilterAttribute>>();
        switch (context.Exception)
        {
            case UnsafeZipException ex:
                logger?.LogWarning("Upload refused by zip limits: {Detail}", ex.Message);
                context.Result = new BadRequestObjectResult(new { error = UnsafeZipException.BookMessage });
                context.ExceptionHandled = true;
                break;
            case InvalidDataException or System.Xml.XmlException:
                logger?.LogWarning(context.Exception, "Upload is not a readable archive");
                context.Result = new BadRequestObjectResult(new { error = "This book isn't readable: it is damaged or not an ePub." });
                context.ExceptionHandled = true;
                break;
        }
    }
}
