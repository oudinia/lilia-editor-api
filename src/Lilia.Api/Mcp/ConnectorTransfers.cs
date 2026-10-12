using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Lilia.Api.Mcp;

/// <summary>
/// Files in and out of Lilia for an app on the connector, without the file passing through the
/// conversation (the user, 12 Oct: "import and export must not consume too many tokens"). The app
/// gets a link; the author's browser carries the file.
///
/// <list type="bullet">
/// <item><b>Export:</b> a download link for one document in one format, good for
///   <see cref="ExportLife"/>. Unguessable, and the author's access is checked again on download.</item>
/// <item><b>Import:</b> a link to the editor's drop page (signed in as usual). The page sends the
///   file to Lilia's importer and marks the ticket done; the app reads the new document's id with
///   <c>import_status</c>.</item>
/// </list>
///
/// Tickets live in memory: a restart forgets them, and the app simply asks for a new link.
/// </summary>
public sealed class ConnectorTransfers(IMemoryCache cache)
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

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public (string Token, DateTime ExpiresAt) IssueExport(string userId, Guid documentId, string format)
    {
        var token = NewToken();
        var expires = DateTime.UtcNow.Add(ExportLife);
        cache.Set($"cx-export:{token}", new ExportTicket(userId, documentId, format, expires), ExportLife);
        return (token, expires);
    }

    public ExportTicket? Export(string token) =>
        IsToken(token) && cache.TryGetValue($"cx-export:{token}", out ExportTicket? t) ? t : null;

    public (string Token, ImportTicket Ticket) IssueImport(string userId, string app)
    {
        var token = NewToken();
        var ticket = new ImportTicket { UserId = userId, App = app, ExpiresAt = DateTime.UtcNow.Add(ImportLife) };
        cache.Set($"cx-import:{token}", ticket, ImportLife);
        return (token, ticket);
    }

    /// <summary>The ticket, only for the author it was issued to.</summary>
    public ImportTicket? Import(string token, string userId) =>
        IsToken(token) && cache.TryGetValue($"cx-import:{token}", out ImportTicket? t) && t!.UserId == userId ? t : null;

    private static bool IsToken(string? token) =>
        token is { Length: 48 } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
