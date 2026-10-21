using System.Text.Json;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data.Seeds;

/// <summary>
/// The launch templates (first run 5b, Olivia, 14 Oct 2026): Article and Thesis chapter, as system
/// templates every account can use. "Build it step by step" is the guided wizard, not a template.
/// Slides waits: the editor has slide blocks switched off (useBlockTypes, DEACTIVATED_BLOCK_TYPES),
/// so a deck from a template couldn't get a new slide. Their sample text is Lorem ipsum, so the
/// check can tell an author it is still there.
///
/// Idempotent: a template is matched by <c>(owner "system", TemplateName)</c> and seeded once. To
/// change one, bump <see cref="Version"/>: the old row is soft-deleted and the new one written.
/// reports/2026-10-21-welcome-onboarding.md
/// </summary>
public static class LaunchTemplateSeeder
{
    public const string SystemUserId = "system";

    /// <summary>Kept in TemplateThumbnail (<c>launch-{category}-v{Version}</c>): a new value re-seeds.</summary>
    public const int Version = 1;

    public static readonly string[] Names = ["Article", "Thesis chapter"];

    public static async Task SeedAsync(LiliaDbContext context, CancellationToken ct = default)
    {
        if (!await context.Users.AnyAsync(u => u.Id == SystemUserId, ct))
        {
            context.Users.Add(new User
            {
                Id = SystemUserId,
                Email = "system@lilia.internal",
                Name = "Lilia",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(ct);
        }

        foreach (var seed in Build())
        {
            var order = 0;
            foreach (var b in seed.Blocks.OrderBy(b => b.SortOrder)) b.SortOrder = order++;
            var existing = await context.Documents
                .Where(d => d.OwnerId == SystemUserId && d.IsTemplate && d.TemplateName == seed.TemplateName && d.DeletedAt == null)
                .ToListAsync(ct);
            if (existing.Any(d => d.TemplateThumbnail == seed.TemplateThumbnail)) continue;
            foreach (var old in existing) old.DeletedAt = DateTime.UtcNow;
            context.Documents.Add(seed);
        }
        await context.SaveChangesAsync(ct);
    }

    /// <summary>Fresh copies of the launch templates, as seeded (also what the compile test prints).</summary>
    public static IReadOnlyList<Document> Build() => [Article(), ThesisChapter()];

    private static Document Template(string name, string description, string category, string documentClass,
        string? classOptions, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = SystemUserId,
            Title = name,
            Language = "auto",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 11,
            Columns = 1,
            LatexDocumentClass = documentClass,
            LatexDocumentClassOptions = classOptions,
            DocumentCategory = category,
            IsTemplate = true,
            IsPublicTemplate = true,
            TemplateName = name,
            TemplateDescription = description,
            TemplateCategory = category,
            // Doubles as the seed's version marker: a new value re-seeds the template.
            TemplateThumbnail = $"launch-{category}-v{Version}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in blocks) doc.Blocks.Add(b);
        return doc;
    }

    private const string Lorem1 = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.";
    private const string Lorem2 = "Lorem ipsum duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident.";
    private const string Lorem3 = "Lorem ipsum curabitur pretium tincidunt lacus. Nulla gravida orci a odio, nullam varius, turpis et commodo pharetra, est eros bibendum elit, nec luctus magna felis sollicitudin mauris.";

    /// <summary>Abstract, sections, an equation, references: the shape of a journal article.</summary>
    private static Document Article() => Template(
        "Article", "Abstract, sections, references", "article", "article", null,
        Title(0, "Your article's title", "Your name"),
        Abstract(1, "Lorem ipsum: in three or four sentences, the question, what you did, what you found and why it matters. Replace this text with your own."),
        Heading(2, 1, "Introduction"),
        Paragraph(3, Lorem1),
        Paragraph(4, Lorem2),
        Heading(5, 1, "Method"),
        Paragraph(6, Lorem3),
        Equation(7, "y = \\beta_0 + \\beta_1 x + \\varepsilon"),
        Heading(8, 1, "Results"),
        Paragraph(9, Lorem1),
        Heading(10, 1, "Discussion"),
        Paragraph(11, Lorem2),
        Heading(12, 1, "Conclusion"),
        Paragraph(13, Lorem3));

    /// <summary>A report-class chapter: numbered, with sections and a list.</summary>
    private static Document ThesisChapter() => Template(
        "Thesis chapter", "Report class, numbered chapter and sections", "thesis", "report", null,
        Heading(0, 1, "Your chapter's title"),
        Paragraph(1, "Lorem ipsum: open the chapter with what it answers and how it connects to the previous one. Replace this text with your own."),
        Heading(2, 2, "Background"),
        Paragraph(3, Lorem1),
        Heading(4, 2, "Approach"),
        Paragraph(5, Lorem2),
        List(6, ordered: true, "Lorem ipsum: the first step", "Lorem ipsum: the second step", "Lorem ipsum: the third step"),
        Heading(7, 2, "Findings"),
        Paragraph(8, Lorem3),
        Equation(9, "\\hat{\\theta} = \\arg\\max_{\\theta} \\; \\mathcal{L}(\\theta \\mid x)"),
        Heading(10, 2, "Summary"),
        Paragraph(11, Lorem1));

    private static Block Title(int order, string title, string author) => MakeBlock(
        "title", order, $$"""{"title": {{Json(title)}}, "author": {{Json(author)}}, "dateMode": "today", "date": ""}""");

    private static Block Heading(int order, int level, string text) => MakeBlock(
        "heading", order, $$"""{"text": {{Json(text)}}, "level": {{level}}}""");

    private static Block Paragraph(int order, string text) => MakeBlock("paragraph", order, $$"""{"text": {{Json(text)}}}""");

    private static Block Abstract(int order, string text) => MakeBlock("abstract", order, $$"""{"text": {{Json(text)}}}""");

    private static Block Equation(int order, string latex) => MakeBlock("equation", order, $$"""{"latex": {{Json(latex)}}}""");

    private static Block List(int order, bool ordered, params string[] items) => MakeBlock(
        "list", order, $$"""{"items":[{{string.Join(",", items.Select(Json))}}],"ordered":{{(ordered ? "true" : "false")}}}""");

    private static Block MakeBlock(string type, int order, string contentJson) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = order,
        Depth = 0,
        Content = JsonDocument.Parse(contentJson),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static string Json(string s) => JsonSerializer.Serialize(s);
}
