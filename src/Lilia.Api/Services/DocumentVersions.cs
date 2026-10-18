using System.Data;
using System.Security.Cryptography;
using System.Text;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lilia.Api.Services;

/// <summary>
/// A document's version, content-addressed (the user, 18 Oct: "Ideally versioning is immutable?
/// Like state management in the front end?"): the hash of its blocks' hashes, in order, with its own
/// settings, bibliography, figures and linked tables. The same document always has the same version
/// and any change makes a new one, so what is cached by version is never invalidated, only aged
/// out. Each block's hash is kept by the database itself (trg_blocks_content_hash), so the version
/// is one query that reads no content.
///
/// <para>What changes nothing in the PDF is left out of the document row (when it was opened, saved,
/// checked, its share link, the label numbers the last compile wrote); anything else, including
/// columns added later, is in. A column that does not matter only costs a recompile; one missing
/// would serve a stale PDF, so the list is of what to leave out, not of what to keep.</para>
/// </summary>
public sealed class DocumentVersions
{
    private static readonly string[] NotInThePdf =
    [
        "updated_at", "last_opened_at", "last_auto_saved_at", "search_text", "template_usage_count",
        "ValidationCheckedAt", "ValidationErrorCount", "ValidationWarningCount", "version",
        "AiSummary", "AiSummaryGeneratedAt", "current_version_id", "LabelNumbers", "LabelNumbersAt",
        "is_public", "share_link", "share_slug", "link_expires_at",
    ];

    private const string Sql = """
        SELECT concat_ws(chr(2),
          (SELECT (to_jsonb(d) - @drop)::text FROM documents d WHERE d.id = @id),
          (SELECT string_agg(concat_ws(':', b.id, b.sort_order, b.parent_id, b.depth, b.status, b.content_hash), ',' ORDER BY b.sort_order, b.id)
             FROM blocks b WHERE b.document_id = @id),
          (SELECT string_agg(concat_ws(':', e.cite_key, e.entry_type, md5(coalesce(e.data::text, '')), md5(coalesce(e.formatted_text, ''))), ',' ORDER BY e.cite_key, e.id)
             FROM bibliography_entries e WHERE e.document_id = @id),
          (SELECT string_agg(concat_ws(':', a.id, a.file_name, a.storage_key, a.content_hash, a.file_size, a.width, a.height), ',' ORDER BY a.id)
             FROM assets a WHERE a.document_id = @id),
          (SELECT string_agg(concat_ws(':', t.id, t.updated_at, md5(coalesce(t.content::text, ''))), ',' ORDER BY t.id)
             FROM document_tables dt JOIN tables t ON t.id = dt.table_id WHERE dt.document_id = @id))
        """;

    private readonly LiliaDbContext _db;

    public DocumentVersions(LiliaDbContext db) => _db = db;

    /// <summary>The document's version (64 hex), or null when there is no such document.</summary>
    private readonly Dictionary<Guid, string?> _seen = new();

    /// <summary>The version, computed once per request (this service is scoped).</summary>
    public async Task<string?> OfAsync(Guid documentId, CancellationToken ct = default)
    {
        if (_seen.TryGetValue(documentId, out var known)) return known;
        return _seen[documentId] = await ComputeAsync(documentId, ct);
    }

    private async Task<string?> ComputeAsync(Guid documentId, CancellationToken ct)
    {
        using var timed = Lilia.Core.Diagnostics.Timing.Measure("doc.version");
        var conn = _db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Sql;
            var id = cmd.CreateParameter(); id.ParameterName = "id"; id.Value = documentId; cmd.Parameters.Add(id);
            var drop = new Npgsql.NpgsqlParameter("drop", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = NotInThePdf };
            cmd.Parameters.Add(drop);
            if (_db.Database.CurrentTransaction?.GetDbTransaction() is { } tx) cmd.Transaction = tx;
            var raw = await cmd.ExecuteScalarAsync(ct) as string;
            if (string.IsNullOrEmpty(raw) || !raw.Contains('{')) return null; // no document row
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    /// <summary>
    /// The cache key of one output of one version: what was asked (PDF and its engine, look,
    /// print-safe; or the check), the server build (rendering code changes re-render once, the
    /// compile caches below then usually answer), and today's date (a \today in the document).
    /// </summary>
    public static string OutputKey(string version, string output, params string?[] variant) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u0001",
            new[] { version, output, BuildToken, CompileCache.ProcedureVersion, DateTime.UtcNow.ToString("yyyy-MM-dd") }
                .Concat(variant.Select(v => v ?? ""))))))
            .ToLowerInvariant();

    private static readonly string BuildToken = typeof(DocumentVersions).Assembly.ManifestModule.ModuleVersionId.ToString("N");
}
