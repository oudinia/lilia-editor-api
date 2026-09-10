using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Xunit;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// The document-level things that stopped real papers compiling, once their
/// mathematics no longer did.
///
/// <para>Each was found by compiling an actual document rather than by
/// reading: fix one, run it, read the next error. All three failed the same
/// way — the Typst path gave up and pdflatex quietly served the PDF, so the
/// only visible symptom was that previews were slow.</para>
/// </summary>
public class TypstDocumentLevelTests
{
    private static BibliographyEntry Entry(string key, string year, string author = "Euclid") => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        CiteKey = key,
        EntryType = "book",
        Data = JsonDocument.Parse(JsonSerializer.Serialize(
            new { author, title = "Elements", year })),
    };

    private static string BibFor(params BibliographyEntry[] entries) =>
        BibTeXSerializer.SerializeForTypst(entries);

    /// <summary>Compile a document that cites the entries, with the .bib beside it.</summary>
    private static TypstHarness.CompileResult CompileWithBib(string bib, string citeKey) =>
        CompileWithFile(
            $"#set page(width: 12cm, height: auto)\n@{citeKey}\n#bibliography(\"refs.bib\")\n",
            "refs.bib", bib);

    private static TypstHarness.CompileResult CompileWithFile(string source, string name, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"typst-doc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, name), content);
            File.WriteAllText(Path.Combine(dir, "main.typ"), source);

            var psi = new System.Diagnostics.ProcessStartInfo("typst")
            {
                WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("compile");
            psi.ArgumentList.Add("main.typ");
            psi.ArgumentList.Add("out.pdf");

            using var p = System.Diagnostics.Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            var pdf = Path.Combine(dir, "out.pdf");
            return new TypstHarness.CompileResult(
                p.ExitCode == 0 && File.Exists(pdf) && new FileInfo(pdf).Length > 0, err);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── the year ─────────────────────────────────────────────────────────

    [Fact]
    public void AClassicalDateDoesNotBreakTheWholeBibliography()
    {
        // "c. 300 BCE" is an ordinary thing to write, BibTeX accepts it, and
        // Typst rejects the entire file for it: "failed to parse BibLaTeX
        // (wrong number of digits)". One citation of Euclid therefore cost a
        // paper its whole reference list.
        var result = CompileWithBib(BibFor(Entry("euclid", "c. 300 BCE")), "euclid");

        result.Ok.Should().BeTrue($"typst said: {result.FirstProblem}");
    }

    [Fact]
    public void TheDateTheAuthorWroteIsNotLost()
    {
        // Coercing to "300" would compile and quietly change what the paper
        // says. The digits go in year; the original stays in note.
        var bib = BibFor(Entry("euclid", "c. 300 BCE"));

        bib.Should().Contain("300");
        bib.Should().Contain("c. 300 BCE", "the reader must still see what was written");
    }

    [Fact]
    public void AnOrdinaryYearIsUntouched()
    {
        var bib = BibFor(Entry("neugebauer", "1957"));

        bib.Should().Contain("1957");
        bib.Should().NotContain("note", "nothing needed rescuing here");
    }

    [Theory]
    [InlineData("c. 300 BCE")]
    [InlineData("300 BC")]
    [InlineData("1957a")]
    [InlineData("in press")]
    [InlineData("forthcoming")]
    [InlineData("n.d.")]
    public void EveryAwkwardDateStillCompiles(string year)
    {
        var result = CompileWithBib(BibFor(Entry("k", year)), "k");

        result.Ok.Should().BeTrue($"year '{year}' — typst said: {result.FirstProblem}");
    }

    // ── the bibliography directive ───────────────────────────────────────

    [Fact]
    public void ADocumentWithNoReferencesDoesNotAskForABibFile()
    {
        // The directive was emitted unconditionally, so a bibliography block
        // with nothing in it — a section the author added and has not filled
        // — compiled to "file not found (searched at …/references.bib)".
        var doc = Doc();
        var typst = new TypstExportService().BuildTypstDocument(
            doc, [Para("Body."), Block("bibliography", new { })]);

        typst.Should().NotContain("#bibliography(",
            "there is no references.bib to point at");
        Compile(typst).Ok.Should().BeTrue();
    }

    [Fact]
    public void ADocumentWithReferencesStillAsksForTheFile()
    {
        var doc = Doc();
        doc.BibliographyEntries = [Entry("euclid", "300")];

        var typst = new TypstExportService().BuildTypstDocument(
            doc, [Para("Body."), Block("bibliography", new { })]);

        typst.Should().Contain("#bibliography(\"references.bib\")");
    }

    // ── multi-page output ────────────────────────────────────────────────

    [Fact]
    public void ALongDocumentRendersToSvg()
    {
        // typst refuses to write several pages into one .svg: "cannot export
        // multiple images without a page number template". Any document over
        // a page failed to preview — which is most of them.
        var blocks = Enumerable.Range(0, 60)
            .Select(i => Para($"Paragraph {i}, long enough to push the document past one page.", i))
            .ToArray();

        var typst = new TypstExportService().BuildTypstDocument(Doc(), [.. blocks]);
        var result = Compile(typst);

        result.Ok.Should().BeTrue($"typst said: {result.FirstProblem}");
    }
}
