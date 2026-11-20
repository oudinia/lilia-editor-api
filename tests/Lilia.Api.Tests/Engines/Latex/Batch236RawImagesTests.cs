using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #236 (Overleaf round 1, 19 Nov 2026): the APS REVTeX template's figures stay raw LaTeX (#222), and their
/// \includegraphics{fig_1}, {fig_2}, {vid_1a}, {vid_1b} went into the export with no file in the zip and nothing reporting them
/// missing. Only figure blocks' images were packed and checked. Now a raw block's images come from the document's own files
/// (a zip import stores each as an asset under its name) at the path \graphicspath{{./figures/}} finds, and one that no file
/// matches is reported missing, by the check and by a submission.
/// </summary>
public class Batch236RawImagesTests
{
    internal const string ApsFigures = @"\begin{figure*}
\includegraphics{fig_1}% Here is how to import EPS art
\caption{\label{fig:epsart} A figure caption.}
\end{figure*}
\begin{figure}[b]
\includegraphics[width=\columnwidth]{fig_2}
% \includegraphics{commented_out}
\includegraphics*{vid_1a}\hfill\includegraphics{vid_1b}
\includegraphics{example-image-a}
\end{figure}";

    internal static Block Raw(string code, string engine = "latex") => B("embed", new { engine, code });

    [Fact]
    public void Every_image_a_raw_LaTeX_block_includes_is_listed_as_written()
    {
        FigureFile.RawIncludes([Raw(ApsFigures)]).Select(r => r.Name)
            .Should().Equal("fig_1", "fig_2", "vid_1a", "vid_1b");
    }

    [Fact]
    public void A_raw_block_in_another_engine_is_not_read()
    {
        FigureFile.RawIncludes([Raw(@"\includegraphics{x}", "typst")]).Should().BeEmpty();
    }

    [Fact]
    public void A_name_matches_the_document_file_LaTeX_would_find()
    {
        string[] files = ["fig_1.png", "fig_1.pdf", "plot.png", "Vid_1A.JPG"];
        FigureFile.MatchRawInclude("fig_1", files).Should().Be(("fig_1.pdf", "fig_1.pdf"), "a PDF first, as LaTeX tries it");
        FigureFile.MatchRawInclude("./img/plot.png", files).Should().Be(("plot.png", "img/plot.png"));
        FigureFile.MatchRawInclude("vid_1a", files).Should().Be(("Vid_1A.JPG", "Vid_1A.JPG"));
        FigureFile.MatchRawInclude("fig_2", files).Should().BeNull();
    }

    // Zayd's review of #665: the folder part of a raw \includegraphics path is the author's text and becomes a path the API
    // writes, outside the TeX sandbox. {../../x/fig} escaped the figures folder and {/abs/fig} replaced it.

    [Theory]
    [InlineData("../../escape/fig")]
    [InlineData("img/../../fig")]
    [InlineData("/etc/fig")]
    [InlineData("C:/work/fig")]
    [InlineData(@"img\fig")]
    [InlineData("fig;rm -rf")]
    [InlineData("img//fig")]
    public void A_raw_image_path_that_could_leave_the_folder_is_refused(string name)
    {
        FigureFile.MatchRawInclude(name, ["fig.pdf", "fig"]).Should().BeNull();
        var block = Raw($@"\includegraphics{{{name}}}");
        DocumentCheckService.FindMissingRawIncludes(FigureFile.RawIncludes([block]), ["fig.pdf"]).Should().ContainSingle("it is reported missing");
    }

    [Fact]
    public void A_path_is_contained_in_its_folder_or_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "lilia-contained");
        FigureFile.ContainedPath(root, "img/plot.png").Should().Be(Path.Combine(root, "img", "plot.png"));
        FigureFile.ContainedPath(root, "../x.png").Should().BeNull();
        FigureFile.ContainedPath(root, "img/../../x.png").Should().BeNull();
        FigureFile.ContainedPath(root, "/etc/passwd").Should().BeNull();
    }

    [Theory]
    [InlineData("figures/plot.png", true)]
    [InlineData("source/figures/My Figure 1.png", true)]
    [InlineData("../plot.png", false)]
    [InlineData("figures/../../plot.png", false)]
    [InlineData("/plot.png", false)]
    [InlineData("C:/plot.png", false)]
    [InlineData(@"figures\plot.png", false)]
    [InlineData("figures//plot.png", false)]
    public void A_zip_entry_must_unpack_inside_the_project(string path, bool safe) =>
        LaTeXExportService.SafeZipEntry(path).Should().Be(safe);

    [Fact]
    public void The_check_reports_raw_images_no_file_matches_by_their_written_name()
    {
        var block = Raw(ApsFigures);
        var missing = DocumentCheckService.FindMissingRawIncludes(FigureFile.RawIncludes([block]), ["fig_1.pdf", "fig_2.pdf"]);
        missing.Should().Equal((block.Id, "vid_1a"), (block.Id, "vid_1b"));
    }
}

/// <summary>#236's export half, on a real database: the export packs a raw block's image from the document's files.</summary>
public class Batch236RawImagesExportTests : IntegrationTestBase
{
    public Batch236RawImagesExportTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_export_packs_a_raw_blocks_images_where_graphicspath_finds_them()
    {
        await SeedUserAsync("raw_figs");
        var doc = await SeedDocumentAsync("raw_figs", "APS raw figures");
        await SeedBlockAsync(doc.Id, "embed", System.Text.Json.JsonSerializer.Serialize(new { engine = "latex", code = Batch236RawImagesTests.ApsFigures }), 0);
        await using (var db = CreateDbContext())
        {
            foreach (var name in new[] { "fig_1.pdf", "fig_2.pdf" })
                db.Assets.Add(new Asset
                {
                    Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = "raw_figs", FileName = name, FileType = "application/pdf",
                    FileSize = 4, StorageKey = $"raw_figs/documents/{doc.Id}/import-assets/{name}", Url = "https://files.example/" + name,
                    CreatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DownloadAsync(It.IsAny<string>())).ReturnsAsync(() => new MemoryStream("%PDF"u8.ToArray()));

        var dir = Directory.CreateTempSubdirectory("lilia-raw-").FullName;
        try
        {
            await using var ctx = CreateDbContext();
            var blocks = ctx.Blocks.Where(b => b.DocumentId == doc.Id).ToList();
            await new LaTeXExportService(ctx, storage.Object, NullLogger<LaTeXExportService>.Instance).WriteFiguresAsync(blocks, dir);

            File.Exists(Path.Combine(dir, "figures", "fig_1.pdf")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "figures", "fig_2.pdf")).Should().BeTrue();
            Directory.GetFiles(Path.Combine(dir, "figures")).Should().HaveCount(2, "vid_1a and vid_1b have no file: missing, not packed");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task The_writer_never_writes_outside_its_folder_for_a_path_from_the_authors_text()
    {
        // Zayd's review of #665, through the writer itself: matching files exist, so only the path checks stand in the way.
        await SeedUserAsync("raw_escape");
        var doc = await SeedDocumentAsync("raw_escape", "Escaping figures");
        var root = Directory.CreateTempSubdirectory("lilia-escape-").FullName;
        var target = Path.Combine(root, "victim");
        var code = $@"\includegraphics{{../../escape/fig_x}}" + "\n" + $@"\includegraphics{{{target}/fig_y}}";
        await SeedBlockAsync(doc.Id, "embed", System.Text.Json.JsonSerializer.Serialize(new { engine = "latex", code }), 0);
        await using (var db = CreateDbContext())
        {
            foreach (var name in new[] { "fig_x.pdf", "fig_y.pdf" })
                db.Assets.Add(new Asset
                {
                    Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = "raw_escape", FileName = name, FileType = "application/pdf",
                    FileSize = 4, StorageKey = $"raw_escape/documents/{doc.Id}/import-assets/{name}", Url = "https://files.example/" + name,
                    CreatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DownloadAsync(It.IsAny<string>())).ReturnsAsync(() => new MemoryStream("%PDF"u8.ToArray()));

        var dir = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(dir);
        try
        {
            await using var ctx = CreateDbContext();
            var blocks = ctx.Blocks.Where(b => b.DocumentId == doc.Id).ToList();
            await new LaTeXExportService(ctx, storage.Object, NullLogger<LaTeXExportService>.Instance).WriteFiguresAsync(blocks, dir);

            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty("nothing is written, inside or outside");
            Directory.Exists(Path.Combine(root, "a", "escape")).Should().BeFalse();
            Directory.Exists(target).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }
}
