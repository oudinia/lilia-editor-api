using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Templates;

/// <summary>
/// A template is a copy, so it follows the rule Duplicate does: it carries the
/// content, class, columns, margins and preamble, and never the collaborators
/// (Olivia, templates handoff 27 Sep, §2).
///
/// <para>Measured 27 Sep: POST /api/templates copied language, paper, font,
/// size and columns and dropped every other setting — class and options,
/// packages, the custom preamble, margins, headers, spacing, the engine — plus
/// the bibliography and block nesting. Using the template dropped them again
/// on the way out, so a document made from a thesis template was an article.</para>
/// </summary>
[Collection("Integration")]
public class TemplateKeepsTheDocumentTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";
    private const string CollaboratorId = "test_user_002";

    public TemplateKeepsTheDocumentTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>A shared paper with every kind of setting, a nested block and a reference.</summary>
    private async Task<Document> SeedSharedPaperAsync()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(CollaboratorId);
        var doc = await SeedDocumentAsync(OwnerId, "Thesis");
        var parent = await SeedBlockAsync(doc.Id, "list", """{"items":["a"]}""", 1);
        await SeedBibliographyEntryAsync(doc.Id, "knuth1984");

        await using var db = CreateDbContext();
        var role = async (string name) => (await db.Roles.FirstOrDefaultAsync(r => r.Name == name))?.Id
            ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = name }).Entity.Id;
        var team = new Team
        {
            Id = Guid.NewGuid(), Name = "Lab", TeamCode = "lab-" + Guid.NewGuid().ToString("N")[..8],
            Slug = "lab-" + Guid.NewGuid().ToString("N")[..8], OwnerId = OwnerId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Teams.Add(team);
        db.DocumentCollaborators.Add(new DocumentCollaborator
        {
            Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = CollaboratorId, RoleId = await role("editor"),
            InvitedBy = OwnerId, CreatedAt = DateTime.UtcNow,
        });
        db.Blocks.Add(new Block
        {
            Id = Guid.NewGuid(), DocumentId = doc.Id, Type = "paragraph",
            Content = JsonDocument.Parse("""{"text":"child"}"""), SortOrder = 2, Depth = 1, ParentId = parent.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var d = await db.Documents.FirstAsync(x => x.Id == doc.Id);
        d.TeamId = team.Id;
        d.LatexDocumentClass = "report";
        d.LatexDocumentClassOptions = "twoside";
        d.LatexPackages = "siunitx";
        d.CustomPreamble = @"\newcommand{\R}{\mathbb{R}}";
        d.Columns = 2;
        d.ColumnGap = 1.2;
        d.Orientation = "landscape";
        d.MarginTop = "2cm";
        d.MarginLeft = "3cm";
        d.LineSpacing = 1.5;
        d.PageNumbering = "roman";
        d.HeaderCenter = "Draft";
        d.LatexEngine = "xelatex";
        d.IsPublic = true;
        d.ShareLink = "abcdefghijklmnopqrstuv";
        d.ShareSlug = "thesis";
        d.ValidationErrorCount = 3;
        await db.SaveChangesAsync();
        return d;
    }

    private static void ShouldKeepThePaper(Document copy)
    {
        copy.LatexDocumentClass.Should().Be("report");
        copy.LatexDocumentClassOptions.Should().Be("twoside");
        copy.LatexPackages.Should().Be("siunitx");
        copy.CustomPreamble.Should().Be(@"\newcommand{\R}{\mathbb{R}}");
        copy.Columns.Should().Be(2);
        copy.ColumnGap.Should().Be(1.2);
        copy.Orientation.Should().Be("landscape");
        copy.MarginTop.Should().Be("2cm");
        copy.MarginLeft.Should().Be("3cm");
        copy.LineSpacing.Should().Be(1.5);
        copy.PageNumbering.Should().Be("roman");
        copy.HeaderCenter.Should().Be("Draft");
        copy.LatexEngine.Should().Be("xelatex");
    }

    private static void ShouldBePrivateToItsMaker(Document copy, string makerId)
    {
        copy.OwnerId.Should().Be(makerId);
        copy.TeamId.Should().BeNull("a copy is private to whoever made it");
        copy.Collaborators.Should().BeEmpty("a copy never carries the collaborators");
        copy.IsPublic.Should().BeFalse("the public link belongs to the original");
        copy.ShareLink.Should().BeNull();
        copy.ShareSlug.Should().BeNull();
        copy.ValidationErrorCount.Should().Be(0);
        copy.DeletedAt.Should().BeNull();
    }

    private async Task ShouldCarryTheContentAsync(LiliaDbContext db, Guid copyId)
    {
        var blocks = await db.Blocks.Where(b => b.DocumentId == copyId).ToListAsync();
        var list = blocks.Single(b => b.Type == "list");
        blocks.Single(b => b.Type == "paragraph").ParentId.Should().Be(list.Id, "nesting follows the copy");
        (await db.BibliographyEntries.Where(e => e.DocumentId == copyId).Select(e => e.CiteKey).ToListAsync())
            .Should().Equal("knuth1984");
    }

    private async Task<Guid> SaveAsTemplateAsync(Guid documentId, HttpClient? client = null)
    {
        var response = await (client ?? Client).PostAsJsonAsync("/api/templates", new
        {
            documentId, name = "Thesis template", description = (string?)null, category = "thesis", isPublic = false,
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TemplateDto>())!.Id;
    }

    [Fact]
    public async Task A_template_keeps_every_setting_and_never_the_collaborators()
    {
        var paper = await SeedSharedPaperAsync();

        var templateId = await SaveAsTemplateAsync(paper.Id);

        await using var db = CreateDbContext();
        var template = await db.Documents.Include(d => d.Collaborators).FirstAsync(d => d.Id == templateId);
        template.IsTemplate.Should().BeTrue();
        template.TemplateName.Should().Be("Thesis template");
        template.Title.Should().Be("Thesis template");
        template.TemplateCategory.Should().Be("thesis");
        template.IsPublicTemplate.Should().BeFalse();
        template.TemplateUsageCount.Should().Be(0);
        ShouldKeepThePaper(template);
        ShouldBePrivateToItsMaker(template, OwnerId);
        await ShouldCarryTheContentAsync(db, templateId);
    }

    [Fact]
    public async Task A_collaborator_who_saves_it_owns_the_template()
    {
        var paper = await SeedSharedPaperAsync();
        using var collaborator = CreateClientAs(CollaboratorId);

        var templateId = await SaveAsTemplateAsync(paper.Id, collaborator);

        await using var db = CreateDbContext();
        var template = await db.Documents.Include(d => d.Collaborators).FirstAsync(d => d.Id == templateId);
        ShouldBePrivateToItsMaker(template, CollaboratorId);
        ShouldKeepThePaper(template);
    }

    [Fact]
    public async Task A_document_made_from_the_template_keeps_its_settings()
    {
        var paper = await SeedSharedPaperAsync();
        var templateId = await SaveAsTemplateAsync(paper.Id);

        var use = await Client.PostAsJsonAsync($"/api/templates/{templateId}/use", new { title = (string?)null });
        use.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var docId = (await use.Content.ReadFromJsonAsync<DocumentDto>())!.Id;

        await using var db = CreateDbContext();
        var doc = await db.Documents.Include(d => d.Collaborators).FirstAsync(d => d.Id == docId);
        doc.Title.Should().Be("Thesis template");
        doc.IsTemplate.Should().BeFalse();
        doc.TemplateName.Should().BeNull();
        doc.TemplateCategory.Should().BeNull();
        doc.TemplateUsageCount.Should().Be(0);
        ShouldKeepThePaper(doc);
        ShouldBePrivateToItsMaker(doc, OwnerId);
        await ShouldCarryTheContentAsync(db, docId);
        (await db.Documents.FirstAsync(d => d.Id == templateId)).TemplateUsageCount.Should().Be(1);
    }
}
