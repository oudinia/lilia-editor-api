using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The document sweep (E-015, 16 Nov 2026): ∮ ⊗ ℝ passed the document check, which maps characters from the database
/// catalog, and stopped the PDF, which used only the shared preamble's static list. The PDFs now fill the static
/// list's gaps from the catalog; characters the static list maps keep its replacement.
/// </summary>
public class PdfUnicodeCatalogTests
{
    private sealed class Catalog(Dictionary<int, string> rows) : IUnicodeShimService
    {
        public Task PreloadAsync() => Task.CompletedTask;
        public UnicodeShimResult BuildShim(string source) => new("", []);
        public UnicodeClassification Classify(string text) => new(
            text.EnumerateRunes().Select(r => r.Value).Distinct().Where(rows.ContainsKey).ToDictionary(cp => cp, cp => rows[cp]), []);
        public string Inject(string source, string shim) => new UnicodeShimService(null!, NullLogger<UnicodeShimService>.Instance).Inject(source, shim);
        public int MappedCount => rows.Count;
    }

    private static readonly Dictionary<int, string> Rows = new()
    {
        [0x222E] = @"\ensuremath{\oint}",
        [0x2297] = @"\ensuremath{\otimes}",
        [0x211D] = @"\ensuremath{\mathbb{R}}",
        [0x03B1] = @"\ensuremath{\text{catalog alpha}}", // α is in the static list: its replacement must not change
    };

    private static LaTeXRenderService Service() => new(NullLogger<LaTeXRenderService>.Instance, new Catalog(Rows));

    private const string Doc = "\\documentclass{article}\n\\begin{document}\nSymbols: ∮ ⊗ ℝ and α.\n\\end{document}\n";

    [Fact]
    public void Characters_the_static_list_lacks_come_from_the_catalog_before_the_body()
    {
        var latex = Service().WithCatalogGaps(Doc);
        latex.Should().Contain(@"\newunicodechar{∮}{\ensuremath{\oint}}").And.Contain(@"\newunicodechar{⊗}{\ensuremath{\otimes}}");
        // ℝ is in the static list since 16 Nov (E-015): the static list's mapping is used, not the catalog's.
        latex.Should().NotContain(@"\newunicodechar{ℝ}");
        latex.IndexOf(@"\newunicodechar{∮}", StringComparison.Ordinal).Should().BeLessThan(latex.IndexOf(@"\begin{document}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_character_the_static_list_maps_keeps_its_replacement()
    {
        LaTeXPreamble.UnicodeShims.Should().Contain(@"\newunicodechar{α}");
        Service().WithCatalogGaps(Doc).Should().NotContain("catalog alpha");
    }

    [Fact]
    public void Without_the_catalog_or_without_gaps_the_source_is_unchanged()
    {
        new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).WithCatalogGaps(Doc).Should().BeSameAs(Doc);
        const string plain = "\\documentclass{article}\n\\begin{document}\nPlain α.\n\\end{document}\n";
        Service().WithCatalogGaps(plain).Should().BeSameAs(plain);
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task The_symbols_compile_with_no_problem()
    {
        var latex = "\\documentclass{article}\n" + LaTeXPreamble.Packages + "\\begin{document}\nSymbols: ∮ ⊗ ℝ and α.\n\\end{document}\n";
        var (pdf, _, problems) = await Service().RenderToPdfTolerantWithProblemsAsync(latex, timeout: 120, engine: "pdflatex");
        pdf.Should().NotBeEmpty();
        problems.Should().BeEmpty();
    }
}
