using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// The Document Settings dialog sends the six header/footer slots and the
/// orientation. The update DTO had no slot fields, so they were dropped without
/// an error, and orientation only reached the class-options blob while the
/// preamble builder reads the structured column. Ask Lilia's
/// set_document_settings goes through this same update, so it needs them to
/// stick.
/// </summary>
[Collection("Integration")]
public class SettingsDialogSaveTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";

    public SettingsDialogSaveTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Slots_and_orientation_persist_and_read_back_and_other_fields_stay()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Lecture 3");
        await using (var seed = CreateDbContext())
        {
            var d = await seed.Documents.FirstAsync(x => x.Id == doc.Id);
            d.MarginTop = "2cm"; d.LineSpacing = 1.5;
            await seed.SaveChangesAsync();
        }

        var body = new
        {
            orientation = "landscape",
            headerLeft = "Lecture 3", headerCenter = "Fluid dynamics", headerRight = "Fall 2026",
            footerLeft = "L", footerCenter = "C", footerRight = "R",
            pageNumbering = "roman",
        };
        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}", body);
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
        dto.HeaderCenter.Should().Be("Fluid dynamics");
        dto.FooterRight.Should().Be("R");
        dto.Orientation.Should().Be("landscape");

        await using var db = CreateDbContext();
        var saved = await db.Documents.AsNoTracking().FirstAsync(x => x.Id == doc.Id);
        saved.HeaderLeft.Should().Be("Lecture 3");
        saved.HeaderRight.Should().Be("Fall 2026");
        saved.FooterLeft.Should().Be("L");
        saved.Orientation.Should().Be("landscape");
        saved.LatexDocumentClassOptions.Should().Contain("landscape");
        saved.PageNumbering.Should().Be("roman");
        saved.MarginTop.Should().Be("2cm");      // not in the request: untouched
        saved.LineSpacing.Should().Be(1.5);

        // Back to portrait clears both places.
        (await Client.PutAsJsonAsync($"/api/documents/{doc.Id}", new { orientation = "portrait" })).EnsureSuccessStatusCode();
        await using var db2 = CreateDbContext();
        var back = await db2.Documents.AsNoTracking().FirstAsync(x => x.Id == doc.Id);
        back.Orientation.Should().Be("portrait");
        (back.LatexDocumentClassOptions ?? "").Should().NotContain("landscape");
        back.HeaderLeft.Should().Be("Lecture 3");
    }
}
