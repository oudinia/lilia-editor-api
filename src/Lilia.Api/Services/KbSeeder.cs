using System.Reflection;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Seeds the knowledge base (`kb_articles`) from embedded `Kb/*.md` files at startup.
/// Content is authored as readable, versioned markdown with a small YAML-ish header
/// (slug/title/summary/tool/skill/audience/tags/keywords); the DB is the queryable,
/// FTS-ranked store. The upsert is idempotent (ON CONFLICT by slug) so it is safe to
/// run on every boot and under multiple replicas.
/// </summary>
public interface IKbSeeder
{
    Task PreloadAsync(CancellationToken ct = default);
}

public sealed class KbSeeder : IKbSeeder
{
    private static readonly Assembly Asm = typeof(KbSeeder).Assembly;
    private readonly LiliaDbContext _db;
    private readonly ILogger<KbSeeder> _logger;

    public KbSeeder(LiliaDbContext db, ILogger<KbSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task PreloadAsync(CancellationToken ct = default)
    {
        var names = Asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Kb.", StringComparison.Ordinal) || n.StartsWith("Kb.", StringComparison.Ordinal))
            .Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var seeded = 0;
        foreach (var name in names)
        {
            try
            {
                var article = Parse(ReadResource(name));
                if (article is null) continue;
                await UpsertAsync(article, ct);
                seeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[KbSeeder] Failed to seed KB resource {Name}", name);
            }
        }
        _logger.LogInformation("[KbSeeder] Seeded {Count} knowledge-base articles", seeded);
    }

    private async Task UpsertAsync(Article a, CancellationToken ct) =>
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO kb_articles
                (slug, title, summary, body, tool_slug, skill_id, audience, tags, keywords, sort_order, enabled, updated_at)
            VALUES
                ({a.Slug}, {a.Title}, {a.Summary}, {a.Body}, {a.ToolSlug}, {a.SkillId}, {a.Audience}, {a.Tags}, {a.Keywords}, {a.SortOrder}, true, now())
            ON CONFLICT (slug) DO UPDATE SET
                title = EXCLUDED.title, summary = EXCLUDED.summary, body = EXCLUDED.body,
                tool_slug = EXCLUDED.tool_slug, skill_id = EXCLUDED.skill_id, audience = EXCLUDED.audience,
                tags = EXCLUDED.tags, keywords = EXCLUDED.keywords, sort_order = EXCLUDED.sort_order,
                enabled = true, updated_at = now()
            """, ct);

    private static string ReadResource(string name)
    {
        using var stream = Asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Parses the leading `--- ... ---` header (simple key: value, plus tags: [a, b])
    // and returns the remainder as the body. Returns null without a slug+title.
    private static Article? Parse(string md)
    {
        md = md.Replace("\r\n", "\n");
        if (!md.StartsWith("---\n", StringComparison.Ordinal)) return null;
        var end = md.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return null;

        var header = md[4..end];
        var bodyStart = md.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? "" : md[(bodyStart + 1)..].Trim();

        string? slug = null, title = null, summary = null, tool = null, skill = null,
            audience = "all", keywords = "";
        var tags = Array.Empty<string>();

        foreach (var raw in header.Split('\n'))
        {
            var line = raw.TrimEnd();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var val = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "slug": slug = val; break;
                case "title": title = Unquote(val); break;
                case "summary": summary = Unquote(val); break;
                case "tool": tool = EmptyToNull(val); break;
                case "skill": skill = EmptyToNull(val); break;
                case "audience": audience = string.IsNullOrWhiteSpace(val) ? "all" : val; break;
                case "keywords": keywords = Unquote(val); break;
                case "tags": tags = ParseList(val); break;
            }
        }

        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(title)) return null;
        return new Article(slug!, title!, summary ?? "", body, tool, skill,
            NormalizeAudience(audience), tags, keywords);
    }

    private static string NormalizeAudience(string a) =>
        a is "beginner" or "intermediate" or "advanced" ? a : "all";

    private static string[] ParseList(string val)
    {
        val = val.Trim();
        if (val.StartsWith('[') && val.EndsWith(']')) val = val[1..^1];
        return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Unquote).ToArray();
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1];
        return s;
    }

    private static string? EmptyToNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record Article(
        string Slug, string Title, string Summary, string Body,
        string? ToolSlug, string? SkillId, string Audience, string[] Tags, string Keywords)
    {
        public int SortOrder => 0;
    }
}
