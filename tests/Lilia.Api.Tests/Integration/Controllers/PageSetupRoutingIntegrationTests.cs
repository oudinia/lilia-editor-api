using System.IO.Compression;
using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// End to end: which engine renders the preview, and whether the LaTeX export follows the document's
/// own size and paper (5 Oct review).
/// </summary>
[Collection("Integration")]
public class PageSetupRoutingIntegrationTests : FourCallersTestBase
{
    public PageSetupRoutingIntegrationTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<string?> PreviewEngineAsync(Guid documentId)
    {
        using var owner = As(OwnerId);
        var res = await owner.PostAsync($"/api/latex/{documentId}/pdf", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return res.Headers.TryGetValues("X-Render-Engine", out var v) ? v.FirstOrDefault() : null;
    }

    [Fact]
    public async Task A_plain_document_previews_with_Typst()
    {
        var s = await SeedSharedDocumentAsync();
        (await PreviewEngineAsync(s.DocumentId)).Should().Be("typst");
    }

    [Fact]
    public async Task A_document_with_margins_and_a_header_previews_with_LaTeX()
    {
        var s = await SeedSharedDocumentAsync();
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(s.DocumentId);
            d!.MarginLeft = "3cm"; d.HeaderLeft = "Lecture 8";
            await db.SaveChangesAsync();
        }
        (await PreviewEngineAsync(s.DocumentId)).Should().NotBe("typst");
    }

    [Fact]
    public async Task The_LaTeX_download_uses_the_documents_own_size_and_paper()
    {
        var s = await SeedSharedDocumentAsync();
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(s.DocumentId);
            d!.FontSize = 12; d.PaperSize = "letter";
            await db.SaveChangesAsync();
        }
        using var owner = As(OwnerId);
        var res = await owner.GetAsync($"/api/documents/{s.DocumentId}/export/latex?structure=single&includeImages=true");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var zip = new ZipArchive(await res.Content.ReadAsStreamAsync());
        var main = zip.Entries.First(e => e.Name == "main.tex");
        using var r = new StreamReader(main.Open());
        var tex = await r.ReadToEndAsync();
        var classLine = tex.Split('\n').First(l => l.TrimStart().StartsWith("\\documentclass"));
        classLine.Should().Contain("12pt").And.Contain("letterpaper").And.NotContain("11pt").And.NotContain("a4paper");
    }
}
