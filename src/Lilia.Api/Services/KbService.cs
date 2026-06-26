using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Read access to the Lilia knowledge base (`kb_articles`) — the screenshot-free,
/// per-tool / per-concept help catalog. Backed by Postgres full-text search so the
/// AI ("Ask Lilia") can discover the right article by intent and point authors to it,
/// and the public site can list/render articles.
///
/// Queried via raw SQL (the table is intentionally outside the EF model — see the
/// AddKnowledgeBase migration). Read-mostly; content is seeded at startup by
/// <see cref="IKbSeeder"/> from embedded markdown.
/// </summary>
public interface IKbService
{
    Task<IReadOnlyList<KbArticleSummary>> ListAsync(string? tool, string? audience, int limit = 100, CancellationToken ct = default);
    Task<KbArticleDetail?> GetAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<KbArticleSummary>> SearchAsync(string query, int limit = 8, CancellationToken ct = default);
    Task<IReadOnlyList<KbArticleSummary>> ForToolAsync(string toolSlug, CancellationToken ct = default);
}

public sealed record KbArticleSummary(
    string Slug, string Title, string Summary,
    string? ToolSlug, string? SkillId, string Audience, IReadOnlyList<string> Tags);

public sealed record KbArticleDetail(
    string Slug, string Title, string Summary, string Body,
    string? ToolSlug, string? SkillId, string Audience, IReadOnlyList<string> Tags);

public sealed class KbService : IKbService
{
    private readonly LiliaDbContext _db;
    public KbService(LiliaDbContext db) => _db = db;

    // Every Row property must have a matching column for SqlQueryRaw materialization,
    // so all queries select the same set. Tags are flattened to a delimited string to
    // keep the raw-SQL materialization simple and provider-agnostic.
    private const string Cols =
        """slug AS "Slug", title AS "Title", summary AS "Summary", body AS "Body", tool_slug AS "ToolSlug", skill_id AS "SkillId", audience AS "Audience", array_to_string(tags, ',') AS "TagsRaw" """;

    public async Task<IReadOnlyList<KbArticleSummary>> ListAsync(string? tool, string? audience, int limit = 100, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM kb_articles WHERE enabled";
        var args = new List<object>();
        if (!string.IsNullOrWhiteSpace(tool)) { sql += $" AND tool_slug = {{{args.Count}}}"; args.Add(tool); }
        if (!string.IsNullOrWhiteSpace(audience) && audience != "all")
        {
            sql += $" AND audience IN ('all', {{{args.Count}}})"; args.Add(audience);
        }
        sql += $" ORDER BY sort_order, title LIMIT {{{args.Count}}}"; args.Add(Clamp(limit, 200));
        var rows = await _db.Database.SqlQueryRaw<Row>(sql, args.ToArray()).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<KbArticleDetail?> GetAsync(string slug, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM kb_articles WHERE enabled AND slug = {{0}} LIMIT 1";
        var row = await _db.Database.SqlQueryRaw<Row>(sql, slug).ToListAsync(ct);
        return row.Count == 0 ? null : ToDetail(row[0]);
    }

    public async Task<IReadOnlyList<KbArticleSummary>> SearchAsync(string query, int limit = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        // websearch_to_tsquery is forgiving with raw user/AI text; rank by relevance.
        var sql = $@"
            SELECT {Cols}
            FROM kb_articles
            WHERE enabled AND search_vector @@ websearch_to_tsquery('english', {{0}})
            ORDER BY ts_rank(search_vector, websearch_to_tsquery('english', {{0}})) DESC, sort_order
            LIMIT {{1}}";
        var rows = await _db.Database.SqlQueryRaw<Row>(sql, query, Clamp(limit, 25)).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<KbArticleSummary>> ForToolAsync(string toolSlug, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM kb_articles WHERE enabled AND tool_slug = {{0}} ORDER BY sort_order, title";
        var rows = await _db.Database.SqlQueryRaw<Row>(sql, toolSlug).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    private static int Clamp(int n, int max) => n < 1 ? 1 : (n > max ? max : n);

    private static string[] SplitTags(string raw) =>
        string.IsNullOrEmpty(raw) ? [] : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static KbArticleSummary ToSummary(Row r) =>
        new(r.Slug, r.Title, r.Summary, r.ToolSlug, r.SkillId, r.Audience, SplitTags(r.TagsRaw));

    private static KbArticleDetail ToDetail(Row r) =>
        new(r.Slug, r.Title, r.Summary, r.Body, r.ToolSlug, r.SkillId, r.Audience, SplitTags(r.TagsRaw));

    // Materialization target for SqlQueryRaw — settable props matched to aliased columns.
    private sealed class Row
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Body { get; set; } = "";
        public string? ToolSlug { get; set; }
        public string? SkillId { get; set; }
        public string Audience { get; set; } = "all";
        public string TagsRaw { get; set; } = "";
    }
}
