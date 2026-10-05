using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// Changing orientation keeps the class options the popover does not own (5 Oct review): the options
/// string was rebuilt from twoside, titlepage and landscape only, so fleqn or draft vanished.
/// </summary>
[Collection("Integration")]
public class ClassOptionsKeptTests : FourCallersTestBase
{
    public ClassOptionsKeptTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Turning_a_document_landscape_keeps_its_other_class_options()
    {
        var s = await SeedSharedDocumentAsync();
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(s.DocumentId);
            d!.LatexDocumentClassOptions = "fleqn,twoside,draft";
            await db.SaveChangesAsync();
        }
        using var owner = As(OwnerId);
        (await owner.PutAsJsonAsync($"/api/documents/{s.DocumentId}", new { orientation = "landscape" })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var check = CreateDbContext();
        var after = (await check.Documents.FindAsync(s.DocumentId))!.LatexDocumentClassOptions!.Split(',');
        after.Should().Contain(new[] { "fleqn", "draft", "twoside", "landscape" });
    }

    [Fact]
    public async Task Turning_it_back_to_portrait_removes_landscape_only()
    {
        var s = await SeedSharedDocumentAsync();
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(s.DocumentId);
            d!.LatexDocumentClassOptions = "fleqn,landscape";
            d.Orientation = "landscape";
            await db.SaveChangesAsync();
        }
        using var owner = As(OwnerId);
        await owner.PutAsJsonAsync($"/api/documents/{s.DocumentId}", new { orientation = "portrait" });
        await using var check = CreateDbContext();
        (await check.Documents.FindAsync(s.DocumentId))!.LatexDocumentClassOptions.Should().Be("fleqn");
    }
}
