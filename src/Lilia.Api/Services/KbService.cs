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
    /// <summary>The article in <paramref name="lang"/>, or in English when it has no translation yet.</summary>
    Task<KbArticleDetail?> GetAsync(string slug, string lang = "en", CancellationToken ct = default);
    /// <summary>Searches the language's articles (stemmed in that language); English when none match.</summary>
    Task<IReadOnlyList<KbArticleSummary>> SearchAsync(string query, int limit = 8, string lang = "en", CancellationToken ct = default);
    Task<IReadOnlyList<KbArticleSummary>> ForToolAsync(string toolSlug, CancellationToken ct = default);
}

public sealed record KbArticleSummary(
    string Slug, string Title, string Summary,
    string? ToolSlug, string? SkillId, string Audience, IReadOnlyList<string> Tags);

public sealed record KbArticleDetail(
    string Slug, string Title, string Summary, string Body,
    string? ToolSlug, string? SkillId, string Audience, IReadOnlyList<string> Tags)
{
    /// <summary>The language this text is in (English when the asked one has no translation yet).</summary>
    public string Lang { get; init; } = "en";
    /// <summary>The steps everyone follows.</summary>
    public string Steps { get; init; } = "";
    /// <summary>"What this means": plain words, for a beginner.</summary>
    public string Beginner { get; init; } = "";
    /// <summary>"For LaTeX users": the LaTeX underneath.</summary>
    public string Expert { get; init; } = "";
}

public sealed class KbService : IKbService
{
    private readonly LiliaDbContext _db;
    public KbService(LiliaDbContext db) => _db = db;

    // Every Row property must have a matching column for SqlQueryRaw materialization,
    // so all queries select the same set. Tags are flattened to a delimited string to
    // keep the raw-SQL materialization simple and provider-agnostic.
    private const string Cols =
        """slug AS "Slug", lang AS "Lang", title AS "Title", summary AS "Summary", body AS "Body", steps AS "Steps", beginner AS "Beginner", expert AS "Expert", tool_slug AS "ToolSlug", skill_id AS "SkillId", audience AS "Audience", array_to_string(tags, ',') AS "TagsRaw" """;

    public async Task<IReadOnlyList<KbArticleSummary>> ListAsync(string? tool, string? audience, int limit = 100, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM kb_articles WHERE enabled AND lang = 'en'";
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

    public async Task<KbArticleDetail?> GetAsync(string slug, string lang = "en", CancellationToken ct = default)
    {
        lang = KbLanguage.Normalize(lang);
        // The asked language first, English as the fallback.
        var sql = $"SELECT {Cols} FROM kb_articles WHERE enabled AND slug = {{0}} AND lang IN ({{1}}, 'en') ORDER BY (lang = {{1}}) DESC LIMIT 1";
        var row = await _db.Database.SqlQueryRaw<Row>(sql, slug, lang).ToListAsync(ct);
        return row.Count == 0 ? null : ToDetail(row[0]);
    }

    public async Task<IReadOnlyList<KbArticleSummary>> SearchAsync(string query, int limit = 8, string lang = "en", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        lang = KbLanguage.Normalize(lang);
        if (lang != "en")
        {
            var own = await SearchInAsync(query, limit, lang, ct);
            if (own.Count > 0) return own;
        }
        return await SearchInAsync(query, limit, "en", ct);
    }

    private async Task<IReadOnlyList<KbArticleSummary>> SearchInAsync(string query, int limit, string lang, CancellationToken ct)
    {
        var config = KbLanguage.SearchConfig(lang);
        // websearch_to_tsquery safely lexes/stems raw user+AI text but ANDs the terms,
        // which kills recall on natural phrasing ("make a table from csv" → make&tabl&csv,
        // and no article contains "make"). Rewrite the parsed AND query to OR so ANY term
        // can hit, then rank by ts_rank so the best-matching article still floats to the
        // top. Recall-first is the right bias for "point the AI to the right doc".
        var sql = $@"
            SELECT {Cols}
            FROM kb_articles
            WHERE enabled AND lang = {{2}} AND search_vector @@ replace(websearch_to_tsquery('{config}', {{0}})::text, '&', '|')::tsquery
            ORDER BY ts_rank(search_vector, websearch_to_tsquery('{config}', {{0}})) DESC, sort_order
            LIMIT {{1}}";
        var rows = await _db.Database.SqlQueryRaw<Row>(sql, query, Clamp(limit, 25), lang).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<KbArticleSummary>> ForToolAsync(string toolSlug, CancellationToken ct = default)
    {
        var sql = $"SELECT {Cols} FROM kb_articles WHERE enabled AND lang = 'en' AND tool_slug = {{0}} ORDER BY sort_order, title";
        var rows = await _db.Database.SqlQueryRaw<Row>(sql, toolSlug).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    private static int Clamp(int n, int max) => n < 1 ? 1 : (n > max ? max : n);

    private static string[] SplitTags(string raw) =>
        string.IsNullOrEmpty(raw) ? [] : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static KbArticleSummary ToSummary(Row r) =>
        new(r.Slug, r.Title, r.Summary, r.ToolSlug, r.SkillId, r.Audience, SplitTags(r.TagsRaw));

    private static KbArticleDetail ToDetail(Row r) =>
        new(r.Slug, r.Title, r.Summary, r.Body, r.ToolSlug, r.SkillId, r.Audience, SplitTags(r.TagsRaw))
        {
            Lang = r.Lang,
            Steps = r.Steps,
            Beginner = r.Beginner,
            Expert = r.Expert,
        };

    // Materialization target for SqlQueryRaw — settable props matched to aliased columns.
    private sealed class Row
    {
        public string Slug { get; set; } = "";
        public string Lang { get; set; } = "en";
        public string Steps { get; set; } = "";
        public string Beginner { get; set; } = "";
        public string Expert { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Body { get; set; } = "";
        public string? ToolSlug { get; set; }
        public string? SkillId { get; set; }
        public string Audience { get; set; } = "all";
        public string TagsRaw { get; set; } = "";
    }
}

/// <summary>The knowledge base's languages: English, French, Spanish (the interface's).</summary>
public static class KbLanguage
{
    public static string Normalize(string? lang)
    {
        var l = (lang ?? "").Trim().ToLowerInvariant();
        return l.StartsWith("fr") ? "fr" : l.StartsWith("es") ? "es" : "en";
    }

    /// <summary>Postgres text-search configuration; constant strings only (it is spliced into SQL).</summary>
    public static string SearchConfig(string lang) => Normalize(lang) switch
    {
        "fr" => "french",
        "es" => "spanish",
        _ => "english",
    };
}
