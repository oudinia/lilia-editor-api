using System.Diagnostics;
using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Lilia's preamble, precompiled (the user, 18 Oct): loading it was about 1.7 s of each 1.9 s pass.
/// The format holds Lilia's part only, so the title and the author's own lines never rebuild it.
/// </summary>
public class DocumentFormatsTests
{
    private static string Doc(string title = "Cached", string? yours = null, string body = "Hello.") =>
        string.Join("\n",
        [
            @"\documentclass[11pt]{article}",
            @"\usepackage{amsmath}",
            @"\usepackage{booktabs}",
            "",
            .. yours is null ? Array.Empty<string>() : ["% lilia:your-preamble", yours, "% lilia:end-your-preamble"],
            "% Document info",
            $@"\title{{{title}}}",
            @"\author{}",
            @"\begin{document}",
            @"\maketitle",
            body,
            @"\end{document}",
        ]);

    [Fact]
    public void The_format_stops_before_the_author_s_part_and_the_document_info()
    {
        DocumentFormats.DumpPart(Doc()).Should().Be("\\documentclass[11pt]{article}\n\\usepackage{amsmath}\n\\usepackage{booktabs}\n");
        DocumentFormats.DumpPart(Doc(yours: @"\newcommand{\R}{\mathbb{R}}")).Should().NotContain(@"\newcommand{\R}");
        DocumentFormats.DumpPart(Doc().Replace("% Document info", "% Custom preamble (author-defined macros)\n\\newcommand{\\E}{E}")).Should().NotContain(@"\newcommand{\E}");
        DocumentFormats.DumpPart(Doc()).Should().NotContain("Cached", "the title is read on every compile");
    }

    [Fact]
    public void Editing_the_title_or_the_author_s_lines_keeps_the_same_format()
    {
        var formats = new DocumentFormats(Path.GetTempPath());
        var path = formats.PathFor(DocumentFormats.DumpPart(Doc())!, "pdflatex");
        formats.PathFor(DocumentFormats.DumpPart(Doc(title: "Renamed"))!, "pdflatex").Should().Be(path);
        formats.PathFor(DocumentFormats.DumpPart(Doc(yours: @"\newcommand{\R}{\mathbb{R}}"))!, "pdflatex").Should().Be(path);
        formats.PathFor(DocumentFormats.DumpPart(Doc(body: "Another body."))!, "pdflatex").Should().Be(path);
        formats.PathFor(DocumentFormats.DumpPart(Doc().Replace("11pt", "12pt"))!, "pdflatex").Should().NotBe(path, "a setting in Lilia's part");
    }

    [Fact]
    public void Nothing_is_precompiled_without_a_document_or_for_another_engine()
    {
        DocumentFormats.DumpPart(@"\documentclass{article}\usepackage{amsmath}").Should().BeNull();
        DocumentFormats.DumpPart("% Document info\n\\begin{document}x\\end{document}").Should().BeNull("nothing comes before the author's part");
        new DocumentFormats(Path.GetTempPath())
            .FormatFor(Doc(), "xelatex", (_, _, _) => Task.FromResult(true), NullLogger.Instance).Should().BeNull();
    }

    [Fact]
    public void The_compiled_tex_marks_where_the_format_ends()
    {
        var marked = DocumentFormats.WithEndOfDump(Doc());
        marked.Should().Contain("\\usepackage{booktabs}\n\n" + DocumentFormats.EndOfDump + "\n% Document info");
    }

    [Fact]
    public async Task A_document_compiled_with_its_format_prints_the_same_and_faster()
    {
        var service = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        // Letters only: a TeX macro name stops at the first digit (\u1… would redefine the accent \u).
        var unique = new string(Guid.NewGuid().ToString("N").Select(c => (char)('a' + c % 26)).ToArray());
        // A preamble of its own (a unique macro in Lilia's part), so this test builds its format.
        var tex = Doc(body: "Precompiled zebra $x^2$.").Replace(@"\usepackage{booktabs}", $"\\usepackage{{booktabs}}\n\\usepackage{{tikz}}\n\\newcommand{{\\liliatest{unique[..8]}}}{{}}");
        var formats = DocumentFormats.Shared!;

        var sw = Stopwatch.StartNew();
        var (without, _) = await service.RenderToPdfTolerantWithAuxAsync(tex);
        var withoutMs = sw.ElapsedMilliseconds;
        (await formats.EnsureAsync(tex, "pdflatex", (d, e, p) => service.BuildFormatAsync(d, e, p), NullLogger.Instance))
            .Should().BeTrue("the format builds");

        // A different body, so the compile cache cannot answer: the format does.
        var edited = tex.Replace("zebra", "yak");
        sw.Restart();
        var (with, _) = await service.RenderToPdfTolerantWithAuxAsync(edited);
        var withMs = sw.ElapsedMilliseconds;

        PdfText(with).Should().Contain("Precompiled yak");
        PdfText(with).Should().Be(PdfText(without).Replace("zebra", "yak"));
        withMs.Should().BeLessThan(withoutMs, $"without the format it took {withoutMs} ms");
    }

    private static string PdfText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-fmt-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var p = Process.Start(new ProcessStartInfo("pdftotext", $"\"{path}\" -") { RedirectStandardOutput = true })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text;
        }
        finally { File.Delete(path); }
    }
}
