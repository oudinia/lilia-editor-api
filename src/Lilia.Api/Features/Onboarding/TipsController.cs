using System.Net;
using Lilia.Api.Services.Email;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Onboarding;

/// <summary>
/// "Don't send me tips" from the day-3 email, without signing in: the link carries the random token
/// stored when the tip was sent. GET shows a short page; POST is the mail apps' one-click
/// unsubscribe (List-Unsubscribe-Post, RFC 8058). Either way the answer is the same for an unknown
/// token, so nothing can be learnt from it.
/// </summary>
[ApiController]
[Route("api/onboarding/tips-off")]
[AllowAnonymous]
public class TipsController(LiliaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ContentResult> Page([FromQuery] string? t, CancellationToken ct)
    {
        var locale = await OptOutAsync(t, ct);
        return Content(Html(EmailTemplates.Language(locale)), "text/html; charset=utf-8");
    }

    [HttpPost]
    public async Task<IActionResult> OneClick([FromQuery] string? t, CancellationToken ct)
    {
        await OptOutAsync(t, ct);
        return Ok();
    }

    /// <summary>Turns tips off for the token's account; its interface language, or null.</summary>
    private async Task<string?> OptOutAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
        var row = await db.UserOnboarding.Where(o => o.TipsToken == token)
            .Select(o => new { o.UserId, o.User.Locale }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        await db.UserOnboarding.Where(o => o.UserId == row.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.TipsOptOut, true).SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
        return row.Locale;
    }

    private static readonly Dictionary<string, (string Title, string Body, string Open)> Copy = new()
    {
        ["en"] = ("No more tips", "Lilia won't send you tips. Emails you ask for, like sign-in links, still arrive.", "Open Lilia"),
        ["fr"] = ("Plus de conseils", "Lilia ne vous enverra plus de conseils. Les e-mails que vous demandez, comme les liens de connexion, arrivent toujours.", "Ouvrir Lilia"),
        ["es"] = ("Sin más consejos", "Lilia no te enviará más consejos. Los correos que pidas, como los enlaces para iniciar sesión, siguen llegando.", "Abrir Lilia"),
    };

    private const string Style = """
        <style>body{margin:0;font:16px/1.6 -apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;color:#111922;background:#fff}
        main{max-width:480px;margin:12vh auto;padding:0 20px}h1{font:600 26px/1.3 Charter,Georgia,serif;margin:0 0 12px}
        p{color:#3B4757;margin:0 0 24px}a{display:inline-block;background:#08597F;color:#fff;text-decoration:none;padding:10px 18px;border-radius:8px;font-weight:600}
        @media (prefers-color-scheme:dark){body{background:#1B1B1B;color:#ECECEC}p{color:#BDBDBD}a{background:#33ABEC;color:#07222F}}</style>
        """;

    private static string Html(string lang)
    {
        var (title, body, open) = Copy[lang];
        return $$"""
            <!doctype html>
            <html lang="{{lang}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}} · Lilia</title>
            {{Style}}</head>
            <body><main data-testid="tips-off"><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(body)}}</p>
            <a href="/">{{WebUtility.HtmlEncode(open)}}</a></main></body></html>
            """;
    }
}
