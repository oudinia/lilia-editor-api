using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Engines.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// A document compiles from what its last compile left (PassState, 16 Nov 2026): one LaTeX pass when the
/// kept .aux and the rest come back unchanged, the passes as before when anything moved. The PDF must be the
/// one an empty folder gives, whatever changed between the two compiles.
/// </summary>
public class KeptPassStateTests
{
    private static LaTeXRenderService Render() => new(NullLogger<LaTeXRenderService>.Instance);

    private static async Task<string> Text(byte[] pdf) => (await Tool("pdftotext", pdf, "-")).Replace("\f", "\n");

    // ---- the store ----

    [Fact]
    public void Only_what_a_pass_reads_is_kept()
    {
        var dir = Directory.CreateTempSubdirectory("lilia-passstate-").FullName;
        try
        {
            foreach (var ext in new[] { ".tex", ".pdf", ".log", ".aux", ".toc", ".out", ".bbl", ".synctex.gz" })
                File.WriteAllText(Path.Combine(dir, "document" + ext), ext);
            File.WriteAllText(Path.Combine(dir, "figure.aux"), "another job");
            PassState.Read(dir).Keys.Should().BeEquivalentTo(".aux", ".toc", ".out", ".bbl");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Same_means_the_same_files_with_the_same_bytes()
    {
        var a = new Dictionary<string, byte[]> { [".aux"] = "x"u8.ToArray(), [".toc"] = "y"u8.ToArray() };
        PassState.Same(a, new Dictionary<string, byte[]> { [".aux"] = "x"u8.ToArray(), [".toc"] = "y"u8.ToArray() }).Should().BeTrue();
        PassState.Same(a, new Dictionary<string, byte[]> { [".aux"] = "x"u8.ToArray(), [".toc"] = "z"u8.ToArray() }).Should().BeFalse();
        PassState.Same(a, new Dictionary<string, byte[]> { [".aux"] = "x"u8.ToArray() }).Should().BeFalse("a file pass one did not write back");
        PassState.Same(new Dictionary<string, byte[]> { [".aux"] = "x"u8.ToArray() }, a).Should().BeFalse("a file pass one wrote that was not kept");
    }

    [Fact]
    public void Only_a_compile_marked_with_its_document_has_a_key_and_the_preamble_is_in_it()
    {
        const string v1 = "\\documentclass{article}\n\\begin{document}\nOne.\n\\end{document}\n";
        PassState.KeyFor(v1, "pdflatex", true).Should().BeNull("no document: compiled from an empty folder as before");

        var id = Guid.NewGuid();
        using (PassState.ForDocument(id))
        {
            var key = PassState.KeyFor(v1, "pdflatex", true);
            key.Should().NotBeNull();
            PassState.KeyFor(v1.Replace("One.", "Two."), "pdflatex", true).Should().Be(key, "an edit to the body keeps the state");
            PassState.KeyFor(v1.Replace("article", "report"), "pdflatex", true).Should().NotBe(key, "a kept .aux is read only under its own preamble");
            PassState.KeyFor(v1, "lualatex", true).Should().NotBe(key);
            using (PassState.ForDocument(Guid.NewGuid()))
                PassState.KeyFor(v1, "pdflatex", true).Should().NotBe(key, "another document");
        }
        PassState.KeyFor(v1, "pdflatex", true).Should().BeNull("the mark ends with its scope");
    }

    [Fact]
    public void The_store_is_bounded_and_forgets_a_closed_account()
    {
        var store = new PassState();
        store.Put("u1:a", new() { [".aux"] = new byte[10] });
        store.Put("u2:b", new() { [".aux"] = new byte[10] });
        store.Put("u1:big", new() { [".aux"] = new byte[PassState.MaxEntryBytes + 1] });
        store.Get("u1:big").Should().BeNull("one entry above the limit is not kept");
        store.RemoveScope("u1").Should().Be(1);
        store.Get("u1:a").Should().BeNull();
        store.Get("u2:b").Should().NotBeNull();
        for (var i = 0; i < 80; i++) store.Put($"u3:{i}", new() { [".aux"] = new byte[PassState.MaxEntryBytes / 2] });
        store.Bytes.Should().BeLessThanOrEqualTo(PassState.MaxBytes);
    }

    // ---- with TeX ----

    [Fact]
    public async Task An_edit_that_moves_nothing_compiles_in_one_pass_and_prints_the_edit()
    {
        const string v1 = @"\documentclass{article}
\begin{document}
\tableofcontents
\section{Intro}\label{intro}
First words. See Section~\ref{end}.
\section{End}\label{end}
The end, on page \pageref{end}.
\end{document}";
        using var _ = PassState.ForDocument(Guid.NewGuid());
        await Render().RenderToPdfTolerantAsync(v1);
        var before = PassState.OnePassCompiles;

        var pdf = await Render().RenderToPdfTolerantAsync(v1.Replace("First words.", "First revised words."));

        PassState.OnePassCompiles.Should().BeGreaterThan(before, "the kept .aux came back unchanged");
        var text = await Text(pdf);
        text.Should().Contain("First revised words. See Section 2.").And.Contain("2 End").And.NotContain("??");
    }

    [Fact]
    public async Task A_reference_that_moved_prints_its_new_number()
    {
        const string v1 = @"\documentclass{article}
\begin{document}
\tableofcontents
\section{Intro}
See Section~\ref{target}.
\section{Target}\label{target}
Here.
\end{document}";
        using var _ = PassState.ForDocument(Guid.NewGuid());
        (await Text(await Render().RenderToPdfTolerantAsync(v1))).Should().Contain("See Section 2.");

        var v2 = v1.Replace(@"\section{Target}", @"\section{Inserted}" + "\nMore.\n" + @"\section{Target}");
        var text = await Text(await Render().RenderToPdfTolerantAsync(v2));

        text.Should().Contain("See Section 3.", "the kept .aux said 2; pass one wrote 3, so the passes ran as before")
            .And.Contain("3 Target").And.Contain("2 Inserted");
    }

    [Fact]
    public async Task A_bibliography_prints_the_right_numbers_after_a_new_citation()
    {
        const string bib = @"@book{knuth, author={Donald Knuth}, title={The TeXbook}, year={1984}, publisher={Addison-Wesley}}
@book{lamport, author={Leslie Lamport}, title={LaTeX: A Document Preparation System}, year={1994}, publisher={Addison-Wesley}}";
        static Task Assets(string dir) => File.WriteAllTextAsync(Path.Combine(dir, "references.bib"), bib);
        const string v1 = @"\documentclass{article}
\begin{document}
Text \cite{lamport}.
\bibliographystyle{unsrt}
\bibliography{references}
\end{document}";
        using var _ = PassState.ForDocument(Guid.NewGuid());
        (await Text(await Render().RenderToPdfTolerantAsync(v1, materialiseAssets: Assets))).Should().Contain("Text [1].");

        var before = PassState.OnePassCompiles;
        var same = await Text(await Render().RenderToPdfTolerantAsync(v1.Replace("Text", "Words"), materialiseAssets: Assets));
        same.Should().Contain("Words [1].").And.Contain("Lamport");
        PassState.OnePassCompiles.Should().BeGreaterThan(before, "BibTeX wrote the same .bbl and pass one the same .aux");

        // unsrt numbers by first citation: Knuth cited first becomes [1], Lamport [2].
        var cited = await Text(await Render().RenderToPdfTolerantAsync(v1.Replace(@"Text \cite{lamport}", @"First \cite{knuth}, then \cite{lamport}"), materialiseAssets: Assets));
        cited.Should().Contain("First [1], then [2].").And.Contain("Knuth").And.NotContain("[?]");
    }
}
