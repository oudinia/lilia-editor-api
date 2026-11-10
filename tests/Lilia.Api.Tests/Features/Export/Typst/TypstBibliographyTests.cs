using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using static Lilia.Api.Tests.Features.Export.Typst.TypstHarness;

namespace Lilia.Api.Tests.Features.Export.Typst;

/// <summary>
/// A cited paper compiles with Typst (10 Nov 2026: a 20-page paper citing its stored entry "knuth84" failed with
/// "label &lt;knuth84&gt; does not exist", because the PDF path loaded the document without its entries, so the
/// generator left #bibliography out; the Typst engine refused the PDF, and "auto" quietly fell back to pdflatex).
/// </summary>
public class TypstBibliographyTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public TypstBibliographyTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public void The_generator_writes_bibliography_when_told_the_file_is_there()
    {
        var blocks = new List<Lilia.Core.Entities.Block> { Para(@"As shown \cite{knuth84}.", 0), Block("bibliography", new { text = "" }, 1) };
        var exporter = new TypstExportService();

        exporter.BuildTypstDocument(Doc(), blocks, null, new TypstExportOptions { HasBibliography = true })
            .Should().Contain("#bibliography(\"references.bib\")");
        exporter.BuildTypstDocument(Doc(), blocks, null, new TypstExportOptions { HasBibliography = false })
            .Should().NotContain("#bibliography(");
    }

    [Theory]
    [InlineData("stored")]
    [InlineData("in-block")]
    public async Task A_cited_paper_exports_with_the_Typst_engine(string where)
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Cited");
        await SeedBlockAsync(doc.Id, "paragraph", JsonSerializer.Serialize(new { text = @"Literate programming \cite{knuth84} matters." }), 0);
        if (where == "stored")
        {
            await SeedBibliographyEntryAsync(doc.Id, "knuth84", "article",
                """{"title":"Literate Programming","author":"Knuth, Donald","journal":"The Computer Journal","year":"1984"}""");
            await SeedBlockAsync(doc.Id, "bibliography", JsonSerializer.Serialize(new { text = "" }), 1);
        }
        else
        {
            await SeedBlockAsync(doc.Id, "bibliography", JsonSerializer.Serialize(new
            {
                text = "@article{knuth84, title={Literate Programming}, author={Knuth, Donald}, journal={The Computer Journal}, year={1984}}",
            }), 1);
        }

        var res = await Client.GetAsync($"/api/documents/{doc.Id}/export/pdf?engine=typst");

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        res.Headers.GetValues("X-Render-Engine").Should().Contain("typst");
    }
}
