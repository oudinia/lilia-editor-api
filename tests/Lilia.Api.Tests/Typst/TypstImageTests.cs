using FluentAssertions;
using Lilia.Api.Services;
using Xunit;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// Figures in Typst output.
///
/// <para>Typst reads images from disk; a block stores a URL. Nothing bridged
/// that, so every figure fell to the "unresolvable" branch and rendered as a
/// grey placeholder rectangle — in the preview, and in the PDF whenever Typst
/// served it, which is most of the time.</para>
///
/// <para>The bridge is deliberately not a download. An image is resolved
/// through the document's own asset rows and read from storage by its
/// StorageKey — the same rule the Word and PDF exporters follow, after the
/// figure exporter was found issuing an HTTP GET to whatever a document
/// named. The caller stages the files and tells the generator where they
/// landed, because the caller is the only layer allowed to do the
/// resolution.</para>
/// </summary>
public class TypstImageTests
{
    private static string Build(
        IReadOnlyDictionary<string, string>? localPaths,
        params Lilia.Core.Entities.Block[] blocks) =>
        new TypstExportService().BuildTypstDocument(
            Doc(), [.. blocks], null,
            new TypstExportOptions { LocalImagePaths = localPaths });

    private static Lilia.Core.Entities.Block Figure(string src, string caption = "A figure.") =>
        Block("figure", new { src, caption, alt = "" });

    [Fact]
    public void AStagedImageIsPointedAtDirectly()
    {
        var staged = new Dictionary<string, string>
        {
            ["http://localhost:5001/uploads/u/d/i/abc.jpg"] = "figures/abc.jpg",
        };

        var typst = Build(staged, Figure("http://localhost:5001/uploads/u/d/i/abc.jpg"));

        typst.Should().Contain("""image("figures/abc.jpg")""");
        typst.Should().NotContain("Image placeholder", "a real image was available");
    }

    [Fact]
    public void TheCaptionSurvivesAlongsideTheImage()
    {
        var staged = new Dictionary<string, string> { ["u"] = "figures/a.png" };

        var typst = Build(staged, Figure("u", "Convergence of the method."));

        typst.Should().Contain("Convergence of the method.");
    }

    [Fact]
    public void AnUnstagedUrlStillGetsAPlaceholderRatherThanABrokenPath()
    {
        // Typst cannot fetch a URL, and pointing image() at one fails the
        // whole compile. A placeholder keeps the document rendering.
        var typst = Build(null, Figure("https://example.com/remote.png"));

        typst.Should().Contain("Image placeholder");
        typst.Should().NotContain("""image("https://""");
    }

    [Fact]
    public void AStagedImageCompiles()
    {
        // The end of the chain: a real file beside main.typ, and typst opens it.
        var dir = Path.Combine(Path.GetTempPath(), $"typst-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "figures"));
        try
        {
            // A 1×1 PNG — enough for typst to decode.
            File.WriteAllBytes(Path.Combine(dir, "figures", "a.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

            var typst = Build(
                new Dictionary<string, string> { ["stored-url"] = "figures/a.png" },
                Figure("stored-url"));
            File.WriteAllText(Path.Combine(dir, "main.typ"), typst);

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

            File.Exists(Path.Combine(dir, "out.pdf")).Should().BeTrue($"typst said: {err}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ADocumentWithNoFiguresIsUnaffected()
    {
        var typst = Build(null, Para("Just prose."));

        typst.Should().Contain("Just prose.");
        Compile(typst).Ok.Should().BeTrue();
    }
}
