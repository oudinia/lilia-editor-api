using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// The launch bot's #113 (15 Nov 2026): with Typst chosen on the preview, a raw-LaTeX block (a tblr table) printed its
/// LaTeX source as text. Typst can't print LaTeX; the block is a short note in its place.
/// </summary>
public class TypstRawLatexNoteTests
{
    private const string Tblr = @"\begin{tblr}{colspec={lll}} A & B & C \\ 1 & 2 & 3 \end{tblr}";

    private static JsonElement Content(object o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void A_raw_LaTeX_block_is_a_note_not_its_source()
    {
        var typst = TypstRenderService.RenderEmbedToTypst(Content(new { engine = "latex", code = Tblr }));

        typst.Should().NotContain("tblr").And.NotContain(@"\begin");
        typst.Should().Contain("raw LaTeX").And.StartWith("#block(");
    }

    [Fact]
    public void An_embed_without_an_engine_is_LaTeX_too()
        => TypstRenderService.RenderEmbedToTypst(Content(new { code = Tblr })).Should().NotContain("tblr");

    [Fact]
    public void A_Typst_embed_is_written_as_is()
        => TypstRenderService.RenderEmbedToTypst(Content(new { engine = "typst", code = "#table(columns: 2)[a][b]" }))
            .Should().Contain("#table(columns: 2)[a][b]");

    [Fact]
    public void An_empty_block_prints_nothing()
        => TypstRenderService.RenderEmbedToTypst(Content(new { engine = "latex", code = "  " })).Should().BeEmpty();

    [Fact]
    public async Task The_note_compiles()
    {
        var (path, _) = await TypstCompileService.DescribeBinaryAsync();
        if (path is null) return; // no Typst on this machine
        var dir = Directory.CreateTempSubdirectory("typst-note-");
        try
        {
            var src = Path.Combine(dir.FullName, "note.typ");
            await File.WriteAllTextAsync(src, TypstRenderService.RenderEmbedToTypst(Content(new { engine = "latex", code = Tblr })));
            var psi = new ProcessStartInfo(path) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "compile", "--root", dir.FullName, src, Path.Combine(dir.FullName, "note.pdf") }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            p.ExitCode.Should().Be(0, err);
        }
        finally { dir.Delete(true); }
    }
}
