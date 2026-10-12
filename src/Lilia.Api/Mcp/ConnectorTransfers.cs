using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Lilia.Api.Mcp;

/// <summary>
/// Files in and out of Lilia for an app on the connector, without the file passing through the
/// conversation (the user, 12 Oct: "import and export must not consume too many tokens"). The app
/// gets a link; the author's browser carries the file.
///
/// <list type="bullet">
/// <item><b>Export:</b> a download link for one document in one format, good for
///   <see cref="ExportLife"/>. The link is <b>signed, not stored</b> (MCP-71, 12 Oct: links died
///   whenever the API restarted, because they lived in memory): it carries who, which document,
///   which format and until when, under ASP.NET Data Protection (keys on disk). The author's access
///   is checked again on download.</item>
/// <item><b>Import:</b> a link to the editor's drop page, also signed. The page sends the file to
///   Lilia's importer; the result waits in memory for <c>import_status</c> (a restart loses only
///   that result: the document itself is in Documents).</item>
/// </list>
///
/// The signing purpose includes this site's public address, so a link made by the test copy
/// (same machine, same key ring) is never valid on production.
/// </summary>
public sealed class ConnectorTransfers
{
    public static readonly TimeSpan ExportLife = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ImportLife = TimeSpan.FromMinutes(30);

    public static readonly string[] ExportFormats = ["pdf", "tex", "zip", "docx"];

    public sealed record ExportTicket(string UserId, Guid DocumentId, string Format, DateTime ExpiresAt);

    public sealed class ImportTicket
    {
        public required string UserId { get; init; }
        public required string App { get; init; }
        public required DateTime ExpiresAt { get; init; }
        /// <summary>waiting → done (DocumentId set) or failed (Error set).</summary>
        public string Status { get; set; } = "waiting";
        public Guid? DocumentId { get; set; }
        public string? Title { get; set; }
        public string? Error { get; set; }
    }

    private readonly IMemoryCache _cache;
    private readonly ITimeLimitedDataProtector _export;
    private readonly ITimeLimitedDataProtector _import;

    public ConnectorTransfers(IMemoryCache cache, IDataProtectionProvider protection, IConfiguration config)
    {
        _cache = cache;
        var site = McpOAuth.PublicOrigin(config);
        _export = protection.CreateProtector("Lilia.Connector.Export", site).ToTimeLimitedDataProtector();
        _import = protection.CreateProtector("Lilia.Connector.Import", site).ToTimeLimitedDataProtector();
    }

    public (string Token, DateTime ExpiresAt) IssueExport(string userId, Guid documentId, string format)
    {
        var expires = DateTime.UtcNow.Add(ExportLife);
        var token = _export.Protect($"{documentId:N}|{format}|{userId}", new DateTimeOffset(expires));
        return (token, expires);
    }

    public ExportTicket? Export(string? token)
    {
        if (!Plausible(token)) return null;
        try
        {
            var parts = _export.Unprotect(token!, out var expires).Split('|', 3);
            return parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out var doc) && ExportFormats.Contains(parts[1])
                ? new ExportTicket(parts[2], doc, parts[1], expires.UtcDateTime)
                : null;
        }
        catch (CryptographicException)
        {
            return null; // forged, damaged, from another site, or expired
        }
    }

    public (string Token, ImportTicket Ticket) IssueImport(string userId, string app)
    {
        var expires = DateTime.UtcNow.Add(ImportLife);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var token = _import.Protect($"{nonce}|{userId}|{app}", new DateTimeOffset(expires));
        var ticket = new ImportTicket { UserId = userId, App = app, ExpiresAt = expires };
        _cache.Set($"cx-import:{nonce}", ticket, ImportLife);
        return (token, ticket);
    }

    /// <summary>
    /// The ticket, only for the author it was issued to. After a restart the signed link still
    /// works: the ticket comes back as waiting.
    /// </summary>
    public ImportTicket? Import(string? token, string userId)
    {
        if (!Plausible(token)) return null;
        try
        {
            var parts = _import.Unprotect(token!, out var expires).Split('|', 3);
            if (parts.Length != 3 || parts[1] != userId) return null;
            return _cache.GetOrCreate($"cx-import:{parts[0]}", e =>
            {
                e.AbsoluteExpiration = expires;
                return new ImportTicket { UserId = userId, App = parts[2], ExpiresAt = expires.UtcDateTime };
            });
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // Data Protection output is base64url; anything else is refused before decrypting.
    private static bool Plausible(string? token) =>
        token is { Length: > 40 and < 2000 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
