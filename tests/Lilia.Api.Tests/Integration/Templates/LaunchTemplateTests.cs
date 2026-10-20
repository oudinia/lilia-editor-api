using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data.Seeds;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Templates;

/// <summary>
/// First run 5b (reports/2026-10-21-welcome-onboarding.md): the launch templates are there for
/// everyone, seeded once, and using one gives a document of one's own with the template's blocks.
/// </summary>
public class LaunchTemplateTests : IntegrationTestBase
{
    public LaunchTemplateTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();
        await LaunchTemplateSeeder.SeedAsync(db);
    }

    [Fact]
    public async Task Seeding_twice_keeps_one_of_each()
    {
        await SeedAsync();
        await SeedAsync();
        await using var db = CreateDbContext();
        var launch = await db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == "system" && d.IsTemplate && d.DeletedAt == null && d.TemplateThumbnail!.StartsWith("launch-"))
            .Select(d => d.TemplateName).ToListAsync();
        launch.Should().BeEquivalentTo(LaunchTemplateSeeder.Names);
    }

    [Fact]
    public async Task A_new_person_sees_them_and_gets_a_document_of_their_own()
    {
        await SeedAsync();
        await SeedUserAsync("newcomer", "newcomer@lilia.test", "Nadia");
        var c = CreateClientAs("newcomer", "newcomer@lilia.test", "Nadia");

        var list = await c.GetFromJsonAsync<List<TemplateListDto>>("/api/templates");
        var chapter = list!.Should().ContainSingle(t => t.Name == "Thesis chapter").Subject;
        chapter.IsSystem.Should().BeTrue();

        var res = await c.PostAsJsonAsync($"/api/templates/{chapter.Id}/use", new { });
        res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var doc = await db.Documents.AsNoTracking().Include(d => d.Blocks)
            .Where(d => d.OwnerId == "newcomer").SingleAsync();
        doc.IsTemplate.Should().BeFalse();
        doc.LatexDocumentClass.Should().Be("report");
        doc.Blocks.Should().HaveCountGreaterThan(5);
        doc.Blocks.OrderBy(b => b.SortOrder).First().Type.Should().Be("heading");
    }
}
