using System.Collections.Concurrent;
using System.Security.Cryptography;
using Lilia.Api.Filters;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Open in Overleaf (25 Oct 2026). Overleaf opens a new project from a ZIP at a public address
/// ("Open in Overleaf" API: overleaf.com/docs?snip_uri=…, the person signed in to Overleaf). Lilia
/// builds the project (Overleaf's folder layout; one main.tex for a publisher paper), keeps it under an
/// unguessable link for ten minutes, and gives the address to open. The link serves that one ZIP and
/// nothing else; it is not stored anywhere but this process's memory.
/// </summary>
[ApiController]
[Authorize]
public class OverleafController(ILaTeXExportService latex, LiliaDbContext db, IConfiguration config) : ControllerBase
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (byte[] Zip, string Name, DateTime Expires)> Links = new(StringComparer.Ordinal);

    /// <summary>Builds the project and returns the Overleaf address that opens it.</summary>
    [HttpPost("api/documents/{docId:guid}/overleaf")]
    [RequireDocumentAccess(Permissions.Read)]
    public async Task<IActionResult> Create(Guid docId)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId);
        if (doc is null) return NotFound();
        var publisher = Lilia.Engines.Publishers.PublisherClasses.Is(LaTeXPreambleBuilder.ResolveClassName(doc));
        var options = new LaTeXExportOptions { Structure = publisher ? "single" : "multi", MultiFileLayout = "overleaf" };
        await using var zip = await latex.ExportToZipAsync(docId, options);
        using var ms = new MemoryStream();
        await zip.CopyToAsync(ms);

        foreach (var (key, link) in Links) if (link.Expires < DateTime.UtcNow) Links.TryRemove(key, out _);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        Links[token] = (ms.ToArray(), $"{System.Text.RegularExpressions.Regex.Replace(doc.Title, @"[^\w\- ]", "").Trim()}.zip", DateTime.UtcNow + Lifetime);

        var zipUrl = $"{McpOAuth.PublicOrigin(config)}/api/public/overleaf/{token}.zip";
        var engine = string.IsNullOrWhiteSpace(doc.LatexEngine) ? "pdflatex" : doc.LatexEngine;
        return Ok(new
        {
            url = $"https://www.overleaf.com/docs?snip_uri={Uri.EscapeDataString(zipUrl)}&engine={Uri.EscapeDataString(engine)}",
            expiresInSeconds = (int)Lifetime.TotalSeconds,
        });
    }

    /// <summary>The ZIP, for Overleaf's servers to fetch: only with the link's token, only for ten minutes.</summary>
    [HttpGet("api/public/overleaf/{token}.zip")]
    [AllowAnonymous]
    public IActionResult Fetch(string token)
    {
        if (!Links.TryGetValue(token, out var link) || link.Expires < DateTime.UtcNow)
        {
            Links.TryRemove(token, out _);
            return NotFound();
        }
        return File(link.Zip, "application/zip", link.Name.Length > 4 ? link.Name : "project.zip");
    }
}
