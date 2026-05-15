using System.Text.Json;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data.Seeds;

/// <summary>
/// Seeds the four curated starter documents (CV, Article, Book, Report)
/// owned by the <c>sample-content</c> user. New accounts get a clone of
/// each on first sign-in via
/// <c>DocumentService.CloneStarterDocumentsAsync</c>.
///
/// Idempotent: re-runs only insert docs that aren't already present
/// (matched by <c>(owner_id, title, is_starter)</c>). Use this seeder
/// from <c>Program.cs</c> startup right after the EF migrations apply.
/// To re-curate a starter doc, soft-delete the old row by hand and
/// rename it here so the next startup writes the new one.
/// </summary>
public static class StarterDocumentSeeder
{
    private const string SampleUserId = "sample-content";

    public static async Task SeedAsync(LiliaDbContext context, CancellationToken ct = default)
    {
        // Ensure the sample-content "user" row exists. UserSyncMiddleware
        // never creates it (no one logs in as sample-content), so without
        // this the starter docs would FK-fail on insert.
        var sampleUser = await context.Users.FirstOrDefaultAsync(u => u.Id == SampleUserId, ct);
        if (sampleUser is null)
        {
            context.Users.Add(new User
            {
                Id = SampleUserId,
                Email = "sample-content@lilia.internal",
                Name = "Lilia Starter Content",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(ct);
        }

        var seeds = new[] { Article(), Book(), Report(), Cv() };
        foreach (var seed in seeds)
        {
            var existing = await context.Documents
                .FirstOrDefaultAsync(d =>
                    d.OwnerId == SampleUserId &&
                    d.Title == seed.Title &&
                    d.IsStarter &&
                    d.DeletedAt == null, ct);
            if (existing is not null) continue;
            context.Documents.Add(seed);
        }
        await context.SaveChangesAsync(ct);
    }

    // ── Doc factories ────────────────────────────────────────────────

    /// <summary>Academic article — abstract + introduction + methods + results + bibliography.</summary>
    private static Document Article() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = SampleUserId,
        Title = "Sample Article",
        Language = "en",
        PaperSize = "a4",
        FontFamily = "serif",
        FontSize = 12,
        Columns = 1,
        LatexDocumentClass = "article",
        IsStarter = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Blocks =
        {
            Heading(0, 1, "Sample Article"),
            Abstract(1, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. This abstract summarises the motivation, approach, and key results of the study in three or four sentences. Replace the lorem text with your own one-paragraph synopsis."),
            Heading(2, 2, "1. Introduction"),
            Paragraph(3, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat."),
            Paragraph(4, "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."),
            Heading(5, 2, "2. Methods"),
            Paragraph(6, "Curabitur pretium tincidunt lacus. Nulla gravida orci a odio. Nullam varius, turpis et commodo pharetra, est eros bibendum elit, nec luctus magna felis sollicitudin mauris."),
            List(7, ordered: true, "Define the study population", "Collect baseline measurements", "Apply the intervention", "Record outcomes at 0, 4, and 12 weeks"),
            Heading(8, 2, "3. Results"),
            Paragraph(9, "Phasellus ultrices nulla quis nibh. Quisque a lectus. Donec consectetuer ligula vulputate sem tristique cursus. Nam nulla quam, gravida non, commodo a, sodales sit amet, nisi."),
            Equation(10, "E = mc^2"),
            Heading(11, 2, "4. Discussion"),
            Paragraph(12, "Integer in sapien. Fusce convallis, mauris imperdiet gravida bibendum, nisl turpis suscipit mauris, sed placerat ipsum urna sed risus. In convallis tellus a mauris."),
            Heading(13, 2, "5. Conclusion"),
            Paragraph(14, "Curabitur sed felis at est porta lobortis. Ut nec dui. Nulla facilisi. Sed ligula. Donec et metus. Vivamus cursus blandit purus."),
        },
    };

    /// <summary>Book — front matter + two short chapters with sections.</summary>
    private static Document Book() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = SampleUserId,
        Title = "Sample Book",
        Language = "en",
        PaperSize = "a4",
        FontFamily = "serif",
        FontSize = 11,
        Columns = 1,
        LatexDocumentClass = "book",
        IsStarter = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Blocks =
        {
            Heading(0, 1, "Sample Book"),
            Paragraph(1, "By Anonymous Author"),
            PageBreak(2),
            Heading(3, 1, "Preface"),
            Paragraph(4, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. This preface introduces the book's scope, intended audience, and the journey readers can expect over the chapters that follow."),
            PageBreak(5),
            Heading(6, 1, "Chapter 1. Origins"),
            Heading(7, 2, "1.1 Setting the Stage"),
            Paragraph(8, "Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae vitae dicta sunt explicabo."),
            Paragraph(9, "Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit, sed quia consequuntur magni dolores eos qui ratione voluptatem sequi nesciunt."),
            Heading(10, 2, "1.2 Early Influences"),
            Paragraph(11, "Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet, consectetur, adipisci velit, sed quia non numquam eius modi tempora incidunt ut labore et dolore magnam aliquam quaerat voluptatem."),
            PageBreak(12),
            Heading(13, 1, "Chapter 2. Growth"),
            Heading(14, 2, "2.1 Turning Points"),
            Paragraph(15, "Ut enim ad minima veniam, quis nostrum exercitationem ullam corporis suscipit laboriosam, nisi ut aliquid ex ea commodi consequatur. Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse quam nihil molestiae consequatur."),
            Heading(16, 2, "2.2 Reflections"),
            Paragraph(17, "At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis praesentium voluptatum deleniti atque corrupti quos dolores et quas molestias excepturi sint occaecati cupiditate non provident."),
        },
    };

    /// <summary>Technical report — title page + executive summary + numbered sections.</summary>
    private static Document Report() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = SampleUserId,
        Title = "Sample Report",
        Language = "en",
        PaperSize = "a4",
        FontFamily = "sans",
        FontSize = 11,
        Columns = 1,
        LatexDocumentClass = "report",
        IsStarter = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Blocks =
        {
            Heading(0, 1, "Sample Report"),
            Paragraph(1, "Prepared by: Anonymous Author"),
            Paragraph(2, "Date: \\today"),
            PageBreak(3),
            Heading(4, 1, "Executive Summary"),
            Paragraph(5, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. This executive summary distils the report's findings into one paragraph for stakeholders who only have time for the headline takeaway."),
            Heading(6, 1, "1. Background"),
            Paragraph(7, "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat."),
            Heading(8, 1, "2. Findings"),
            List(9, ordered: false, "Finding A: lorem ipsum dolor sit amet", "Finding B: consectetur adipiscing elit", "Finding C: sed do eiusmod tempor incididunt"),
            Paragraph(10, "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur."),
            Heading(11, 1, "3. Recommendations"),
            List(12, ordered: true, "Action 1: lorem ipsum dolor sit amet", "Action 2: consectetur adipiscing elit", "Action 3: sed do eiusmod tempor incididunt"),
            Heading(13, 1, "4. Next Steps"),
            Paragraph(14, "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."),
        },
    };

    /// <summary>CV / résumé — uses the dedicated PersonalInfo / CvSection / CvEntry blocks.</summary>
    private static Document Cv() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = SampleUserId,
        Title = "Sample CV",
        Language = "en",
        PaperSize = "a4",
        FontFamily = "sans",
        FontSize = 11,
        Columns = 1,
        // Class-agnostic rendering — CV blocks use textbf/hfill, no
        // moderncv/awesome-cv preamble required. Article works fine.
        LatexDocumentClass = "article",
        IsStarter = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Blocks =
        {
            PersonalInfo(0, name: "Anonymous Author", headline: "Software Engineer · Lorem Ipsum Specialist",
                email: "anonymous@example.com", phone: "+1 555 0100", location: "Earth",
                homepage: "https://example.com"),
            CvSection(1, "Profile"),
            Paragraph(2, "Lorem ipsum dolor sit amet, consectetur adipiscing elit. A two-line professional summary goes here — the elevator pitch a recruiter reads in five seconds before deciding whether to keep going."),
            CvSection(3, "Experience"),
            CvEntry(4, period: "2023 – Present", role: "Senior Role", org: "Company Name", location: "City",
                description: "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."),
            CvEntry(5, period: "2020 – 2023", role: "Mid Role", org: "Previous Company", location: "City",
                description: "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur."),
            CvSection(6, "Education"),
            CvEntry(7, period: "2018 – 2020", role: "M.Sc. in Field", org: "University Name", location: "City",
                description: "Thesis: lorem ipsum dolor sit amet, consectetur adipiscing elit."),
            CvEntry(8, period: "2014 – 2018", role: "B.Sc. in Field", org: "University Name", location: "City",
                description: "Final project: ut enim ad minim veniam, quis nostrud exercitation."),
            CvSection(9, "Skills"),
            List(10, ordered: false, "Languages: lorem, ipsum, dolor", "Tools: sit, amet, consectetur", "Methods: adipiscing, elit"),
        },
    };

    // ── Block helpers ────────────────────────────────────────────────

    private static Block Heading(int order, int level, string text) => MakeBlock(
        "heading", order, $$"""{"text": {{JsonEncode(text)}}, "level": {{level}}}""");

    private static Block Paragraph(int order, string text) => MakeBlock(
        "paragraph", order, $$"""{"text": {{JsonEncode(text)}}}""");

    private static Block Abstract(int order, string text) => MakeBlock(
        "abstract", order, $$"""{"text": {{JsonEncode(text)}}}""");

    private static Block Equation(int order, string latex) => MakeBlock(
        "equation", order, $$"""{"latex": {{JsonEncode(latex)}}}""");

    private static Block PageBreak(int order) => MakeBlock("pagebreak", order, "{}");

    private static Block List(int order, bool ordered, params string[] items)
    {
        var encoded = string.Join(",", items.Select(JsonEncode));
        return MakeBlock("list", order, $$"""{"items":[{{encoded}}],"ordered":{{(ordered ? "true" : "false")}}}""");
    }

    private static Block PersonalInfo(int order, string name, string headline, string email,
        string phone, string location, string homepage) => MakeBlock(
        "personalInfo", order, $$"""
        {
            "name": {{JsonEncode(name)}},
            "headline": {{JsonEncode(headline)}},
            "email": {{JsonEncode(email)}},
            "phones": [{"number": {{JsonEncode(phone)}}}],
            "location": {{JsonEncode(location)}},
            "homepage": {{JsonEncode(homepage)}},
            "socials": [],
            "extra": ""
        }
        """);

    private static Block CvSection(int order, string title) => MakeBlock(
        "cvSection", order, $$"""{"title": {{JsonEncode(title)}}}""");

    private static Block CvEntry(int order, string period, string role, string org, string location, string description) => MakeBlock(
        "cvEntry", order, $$"""
        {
            "period": {{JsonEncode(period)}},
            "role": {{JsonEncode(role)}},
            "org": {{JsonEncode(org)}},
            "location": {{JsonEncode(location)}},
            "description": {{JsonEncode(description)}},
            "tech": []
        }
        """);

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

    private static string JsonEncode(string s) => JsonSerializer.Serialize(s);
}
