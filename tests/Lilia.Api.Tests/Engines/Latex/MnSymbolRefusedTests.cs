using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// 21 Nov 2026: a PDF bulk-imported through Mathpix kept Mathpix's preamble packages (mhchem, stmaryrd, MnSymbol), and
/// MnSymbol redefines amssymb's symbols, which Lilia always loads: "Command \centerdot already defined", no PDF. MnSymbol
/// is refused on both paths, the preview's (RenderService) and the export's (LaTeXExportService); the other two stay.
/// </summary>
public class MnSymbolRefusedTests
{
    private static Document Doc(string packages) => new()
    {
        Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 10,
        LatexDocumentClass = "article", LatexPackages = packages, BibliographyEntries = new List<BibliographyEntry>(),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Blocks = [new Block { Id = Guid.NewGuid(), Type = "paragraph", Content = JsonDocument.Parse("""{"text":"Body."}""") }],
    };

    private static readonly string MathpixPackages = JsonSerializer.Serialize(new object[]
    {
        new { name = "mhchem", options = "version=4" }, new { name = "stmaryrd" }, new { name = "MnSymbol" },
    });

    [Fact]
    public void The_preview_leaves_MnSymbol_out_and_keeps_Mathpix_s_other_packages()
    {
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(Doc(MathpixPackages));
        tex.Should().NotContain("MnSymbol");
        tex.Should().Contain(@"\IfFileExists{stmaryrd.sty}{\usepackage{stmaryrd}}{}");
        tex.Should().Contain(@"\IfFileExists{mhchem.sty}{\usepackage[version=4]{mhchem}}{}");
    }

    [Theory]
    [InlineData("MnSymbol")]
    [InlineData("mnsymbol")]
    public void The_export_refuses_MnSymbol_whatever_its_case(string name)
        => LaTeXExportService.IsDefaultPreamblePackageForTest(name).Should().BeTrue();
}
