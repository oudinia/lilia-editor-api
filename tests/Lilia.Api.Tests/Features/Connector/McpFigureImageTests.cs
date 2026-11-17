using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Engines.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The connector test of 17 Nov 2026, item C: a figure could only be TikZ. An image link printed "Missing figure" and
/// there was no upload, so a plot the assistant made had no way into the paper. add_figure_image takes the file in the
/// call (nothing is fetched), stores it as the editor's upload does (the same asset rows and storage, the same 10 MB),
/// and adds or fills the figure; the check finds the image and the PDF prints it.
/// </summary>
public class McpFigureImageTests : IntegrationTestBase
{
    public McpFigureImageTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private HttpClient App(string userId)
    {
        var c = CreateClientAs(userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        c.DefaultRequestHeaders.Add("X-Test-Scope", "openid documents:read documents:write");
        return c;
    }

    private async Task<byte[]> ExportPdfAsync(string userId, Guid docId, string? engine)
    {
        var response = await CreateClientAs(userId).GetAsync($"/api/documents/{docId}/export/pdf{(engine is null ? "" : $"?engine={engine}")}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<int> AssetCountAsync(Guid docId)
    {
        await using var db = CreateDbContext();
        return await db.Assets.CountAsync(a => a.DocumentId == docId);
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_PNG_the_app_sends_is_a_figure_the_check_finds_and_the_PDF_prints()
    {
        await SeedUserAsync("mcpfig_png");
        var doc = await SeedDocumentAsync("mcpfig_png", "With a plot");
        var intro = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"See Figure~(fig:plot)."}""", 0);
        var app = App("mcpfig_png");

        var r = await CallAsync(app, "add_figure_image", new
        {
            documentId = doc.Id.ToString(), filename = "plot.png", contentBase64 = "data:image/png;base64," + CheckHintsTests.OnePixelPng,
            caption = "The measured curve.", label = "fig:plot", width = 0.5, afterId = intro.Id.ToString(),
        });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r.GetProperty("changedBlocks").GetArrayLength().Should().Be(1);
        r.GetProperty("image").GetProperty("contentType").GetString().Should().Be("image/png");
        r.GetProperty("undo").GetProperty("versionId").GetGuid().Should().NotBeEmpty();

        await using (var db = CreateDbContext())
        {
            var asset = await db.Assets.SingleAsync(a => a.DocumentId == doc.Id);
            var figure = await db.Blocks.SingleAsync(b => b.DocumentId == doc.Id && b.Type == "figure");
            figure.SortOrder.Should().Be(1, "it goes after the paragraph it was asked to follow");
            var content = figure.Content.RootElement;
            content.GetProperty("src").GetString().Should().Be(asset.Url);
            content.GetProperty("assetId").GetString().Should().Be(asset.Id.ToString());
            content.GetProperty("caption").GetString().Should().Be("The measured curve.");
            content.GetProperty("label").GetString().Should().Be("fig:plot");
            content.GetProperty("width").GetDouble().Should().Be(0.5);
            asset.StorageKey.Should().StartWith("mcpfig_png/documents/" + doc.Id, "the editor's upload keeps it there too");
        }

        var check = await CallAsync(app, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("compiles").GetBoolean().Should().BeTrue(check.ToString());
        check.TryGetProperty("missingFigures", out var missing).Should().BeFalse(missing.ToString());
        check.GetProperty("printsCorrectly").GetBoolean().Should().BeTrue(check.ToString());
        check.GetProperty("pages").GetInt32().Should().Be(1);

        var pdf = Encoding.Latin1.GetString(await ExportPdfAsync("mcpfig_png", doc.Id, "pdflatex"));
        pdf.Should().Contain("/Image", "the image is embedded in the PDF");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_PDF_drawing_fills_an_existing_TikZ_figure_and_prints_keeping_its_caption()
    {
        await SeedUserAsync("mcpfig_pdf");
        var doc = await SeedDocumentAsync("mcpfig_pdf", "With a drawing");
        var figure = await SeedBlockAsync(doc.Id, "figure",
            """{"kind":"tikz","source":"\\begin{tikzpicture}\\draw (0,0) -- (1,1);\\end{tikzpicture}","caption":"Kept caption.","label":"fig:drawing"}""", 0);
        var drawing = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(
            "\\documentclass[border=4pt]{standalone}\\begin{document}Quokka drawing text\\end{document}", "pdflatex", timeout: 60);
        var app = App("mcpfig_pdf");

        var r = await CallAsync(app, "add_figure_image", new
        {
            documentId = doc.Id.ToString(), filename = "drawing.png", contentBase64 = Convert.ToBase64String(drawing), blockId = figure.Id.ToString(),
        });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r.GetProperty("image").GetProperty("file").GetString().Should().EndWith(".pdf", "the bytes are a PDF, whatever the name said");

        await using (var db = CreateDbContext())
        {
            var content = (await db.Blocks.SingleAsync(b => b.Id == figure.Id)).Content.RootElement;
            content.TryGetProperty("kind", out _).Should().BeFalse("the drawing would print instead of the image");
            content.TryGetProperty("source", out _).Should().BeFalse();
            content.GetProperty("caption").GetString().Should().Be("Kept caption.");
            content.GetProperty("label").GetString().Should().Be("fig:drawing");
            content.GetProperty("alt").GetString().Should().Be("drawing.pdf");
        }

        var check = await CallAsync(app, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("printsCorrectly").GetBoolean().Should().BeTrue(check.ToString());

        foreach (var engine in new[] { "pdflatex", null })
        {
            var text = await Tool("pdftotext", await ExportPdfAsync("mcpfig_pdf", doc.Id, engine), "-");
            text.Should().Contain("Quokka drawing text", $"the drawing's page prints in the {engine ?? "Automatic"} PDF");
            text.Should().Contain("Kept caption.").And.NotContain("Missing figure");
        }
    }

    [Fact]
    public async Task Files_that_are_not_PNG_JPEG_or_PDF_or_too_large_are_refused_and_nothing_is_stored()
    {
        await SeedUserAsync("mcpfig_refused");
        var doc = await SeedDocumentAsync("mcpfig_refused", "No images");
        var paragraph = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Text."}""", 0);
        var app = App("mcpfig_refused");
        async Task<string> Refusal(object args)
        {
            var r = await CallAsync(app, "add_figure_image", args);
            r.TryGetProperty("changedBlocks", out _).Should().BeFalse(r.ToString());
            return r.GetProperty("error").GetString()!;
        }
        var id = doc.Id.ToString();

        (await Refusal(new { documentId = id, filename = "plot.svg", contentBase64 = Convert.ToBase64String("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray()) }))
            .Should().Contain("an SVG drawing").And.Contain("PNG, JPEG or PDF");
        (await Refusal(new { documentId = id, filename = "anim.gif", contentBase64 = Convert.ToBase64String("GIF89a\u0001\u0000"u8.ToArray()) }))
            .Should().Contain("a GIF");
        (await Refusal(new { documentId = id, filename = "plot.png", contentBase64 = "https://example.org/plot.png" }))
            .Should().Contain("base64").And.Contain("does not fetch links");
        var huge = new byte[LiliaMcpWriteToolsLimit + 1];
        Convert.FromBase64String(CheckHintsTests.OnePixelPng).CopyTo(huge, 0);
        (await Refusal(new { documentId = id, filename = "huge.png", contentBase64 = Convert.ToBase64String(huge) }))
            .Should().Contain("10 MB");
        (await Refusal(new { documentId = id, filename = "plot.png", contentBase64 = CheckHintsTests.OnePixelPng, blockId = paragraph.Id.ToString() }))
            .Should().Contain("not a figure");
        (await Refusal(new { documentId = id, filename = "plot.png", contentBase64 = CheckHintsTests.OnePixelPng, width = 3 }))
            .Should().Contain("0.05 to 1");

        (await AssetCountAsync(doc.Id)).Should().Be(0);
        await using var db = CreateDbContext();
        (await db.Blocks.CountAsync(b => b.DocumentId == doc.Id)).Should().Be(1);
    }

    private const int LiliaMcpWriteToolsLimit = Lilia.Api.Mcp.LiliaMcpWriteTools.FigureImageMaxBytes;
}
