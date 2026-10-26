using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// The six Overleaf projects (26 Oct 2026), through the upload the app uses: each becomes a document
/// with its venue, every cross-reference resolves (each figure kept its label: before, every "see
/// Figure 2" printed ??), and its LaTeX export compiles in its class (two failed: siunitx).
/// </summary>
[Collection("Integration")]
public class OverleafProjectRoundTripTests : IntegrationTestBase
{
    public OverleafProjectRoundTripTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("acm-sigconf", "acm-sigconf-final")]
    [InlineData("ieee-conference", "ieee-conference")]
    [InlineData("elsevier-multifile", "elsevier-preprint")]
    [InlineData("springer-lncs", "lncs-submission")]
    [InlineData("aps-revtex", "aps-final")]
    [InlineData("report-multichapter", null)]
    public async Task Imports_with_its_venue_resolves_every_reference_and_compiles(string project, string? venue)
    {
        var user = $"ovl-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(user);
        using var client = CreateClientAs(user);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "overleaf-projects", project + ".zip")));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", project + ".zip");
        var res = await client.PostAsync("/api/lilia/imports/latex?autoFinalize=true", form);
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        var sessionId = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionId").GetGuid();

        Guid? docId = null;
        for (var i = 0; i < 120 && docId is null; i++)
        {
            await using var db0 = Fixture.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<LiliaDbContext>();
            var s = await db0.ImportReviewSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId);
            if (s?.Status == "imported" && s.DocumentId.HasValue) docId = s.DocumentId; else await Task.Delay(500);
        }
        docId.Should().NotBeNull("the import finishes");

        await using var db = Fixture.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<LiliaDbContext>();
        var doc = await db.Documents.AsNoTracking().Include(d => d.Blocks).Include(d => d.BibliographyEntries).FirstAsync(d => d.Id == docId);
        doc.PublisherProfile.Should().Be(venue);
        doc.Title.Should().NotContain(@"\thanks").And.NotContain(@"\\");
        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        var latex = new LaTeXExportService(db, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

        new EmittedLatexAudit(latex).DanglingReferences.Select(r => r.Key).Should().BeEmpty($"{project}: every reference has its label");
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(latex, "pdflatex", timeout: 180);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", $"{project} compiles after import");
    }
}
