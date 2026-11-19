using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Interfaces;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// Zayd's task 5 (19 Nov 2026), found while reviewing API #652: a figure whose image is embedded in the block
/// (<c>data:image/png;base64,…</c>) printed a "Missing figure" frame in both PDFs. The LaTeX export and the Typst preview read
/// figures from storage only, where a data: source has no key; only the Word export embedded it. Every such image was also
/// named image-0.png, so a second one was never packed. Now a PNG or JPEG of up to 10 MB is decoded into figures/, named by
/// its content, and the check counts it as present.
/// </summary>
public class BatchEmbeddedImagesTests
{
    internal const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static string Jpeg => "data:image/jpeg;base64," + Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9]);

    [Fact]
    public void A_PNG_embedded_in_the_block_is_decoded()
    {
        var image = FigureFile.EmbeddedImage(Png)!.Value;
        image.Extension.Should().Be(".png");
        image.Bytes.Take(8).Should().Equal(PngSignature);
    }

    [Fact]
    public void A_JPEG_embedded_in_the_block_is_decoded()
    {
        FigureFile.EmbeddedImage(Jpeg)!.Value.Extension.Should().Be(".jpg");
        FigureFile.EmbeddedImage(Jpeg.Replace("image/jpeg", "image/jpg")).Should().NotBeNull("image/jpg is a common spelling");
    }

    [Theory]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAAAAACw=")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("data:text/html;base64,PGgxPkhpPC9oMT4=")]
    [InlineData("data:image/png,not-base64")]
    [InlineData("data:image/png;base64,!!!!")]
    [InlineData("data:image/png;base64,")]
    public void Anything_but_a_base64_PNG_or_JPEG_is_refused(string src) => FigureFile.EmbeddedImage(src).Should().BeNull();

    [Fact]
    public void Bytes_that_are_not_what_the_type_says_are_refused()
    {
        FigureFile.EmbeddedImage(Jpeg.Replace("image/jpeg", "image/png")).Should().BeNull("JPEG bytes under a PNG type");
        FigureFile.EmbeddedImage("data:image/png;base64," + Convert.ToBase64String("<script>alert(1)</script>"u8.ToArray())).Should().BeNull();
    }

    [Fact]
    public void An_embedded_image_over_the_upload_limit_is_refused()
    {
        var big = new byte[FigureFile.MaxEmbeddedBytes + 1];
        PngSignature.CopyTo(big, 0);
        FigureFile.EmbeddedImage("data:image/png;base64," + Convert.ToBase64String(big)).Should().BeNull();
    }

    [Fact]
    public void Each_embedded_image_has_its_own_stable_name()
    {
        var a = FigureFile.Name(Png);
        a.Should().MatchRegex(@"^embedded-[0-9a-f]{16}\.png$");
        FigureFile.Name(Png).Should().Be(a, "the same image gets the same name");
        FigureFile.Name(Jpeg).Should().MatchRegex(@"^embedded-[0-9a-f]{16}\.jpg$").And.NotBe(a);
    }

    [Fact]
    public async Task The_LaTeX_export_writes_an_embedded_image_where_the_LaTeX_includes_it()
    {
        var doc = Doc("article");
        var figure = B("figure", new { src = Png, caption = "Pasted" });
        var blocks = doc.Blocks.Append(figure).ToList();
        var dir = Directory.CreateTempSubdirectory("lilia-embedded-").FullName;
        try
        {
            // Storage is never asked about an embedded image: a null storage service would throw if it were.
            await new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance).WriteFiguresAsync(blocks, dir);

            var name = FigureFile.Name(Png);
            File.ReadAllBytes(Path.Combine(dir, "figures", name)).Take(8).Should().Equal(PngSignature);
            doc.Blocks = blocks;
            var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
            latex.Should().Contain($@"\IfFileExists{{figures/{name}}}").And.NotContain("base64");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Two_embedded_images_are_both_packed()
    {
        var blocks = new List<Lilia.Core.Entities.Block> { B("figure", new { src = Png, caption = "A" }), B("figure", new { src = Jpeg, caption = "B" }) };
        var dir = Directory.CreateTempSubdirectory("lilia-embedded-").FullName;
        try
        {
            await new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance).WriteFiguresAsync(blocks, dir);
            // WriteFiguresAsync writes each image under its draft name and its submission name (fig1.png, fig2.jpg).
            Directory.GetFiles(Path.Combine(dir, "figures")).Select(Path.GetFileName)
                .Should().Contain([FigureFile.Name(Png), FigureFile.Name(Jpeg), "fig1.png", "fig2.jpg"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task The_check_counts_an_embedded_image_that_decodes_as_present()
    {
        var good = B("figure", new { src = Png, caption = "Prints" });
        var bad = B("figure", new { src = "data:image/gif;base64,R0lGODlhAQABAAAAACw=", caption = "A GIF" });

        var missing = await DocumentCheckService.FindMissingFigures(FigureFile.Sources([good, bad]),
            _ => throw new InvalidOperationException("storage is never asked about an embedded image"));

        missing.Should().ContainSingle().Which.Should().Be((bad.Id, FigureFile.EmbeddedLabel));
        FigureFile.MissingAdvice(FigureFile.EmbeddedLabel).Should().Contain("PNG or JPEG of up to 10 MB");
    }
}

/// <summary>Task 5's Typst half: the preview stages an embedded image, even in a document with no uploaded assets at all.</summary>
public class BatchEmbeddedImagesStagerTests : IntegrationTestBase
{
    public BatchEmbeddedImagesStagerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_preview_stages_an_embedded_image_in_a_document_with_no_assets()
    {
        await SeedUserAsync("embedded_stage");
        var doc = await SeedDocumentAsync("embedded_stage", "Pasted figure");
        await SeedBlockAsync(doc.Id, "figure", $$"""{"src":"{{BatchEmbeddedImagesTests.Png}}","caption":"Pasted"}""", 0);
        var storage = new Mock<IStorageService>(MockBehavior.Strict);   // never asked

        await using var db = CreateDbContext();
        var staged = await new DocumentImageStager(db, storage.Object, NullLogger<DocumentImageStager>.Instance).StageAsync(doc.Id);

        var path = staged.Paths.Should().ContainKey(BatchEmbeddedImagesTests.Png).WhoseValue;
        path.Should().Be($"figures/{FigureFile.Name(BatchEmbeddedImagesTests.Png)}");
        staged.Files[path].Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }
}
