using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Phase 1 LaTeX coverage tests:
/// 1.1 Export preamble sync with LaTeXPreamble.Packages
/// 1.3 Table column alignment
/// 1.4 Table colspan/rowspan
/// </summary>
public class LatexCoverageTests
{
    private readonly RenderService _sut;

    public LatexCoverageTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _sut = new RenderService(null!, logger.Object);
    }

    // ── 1.1 Export preamble ─────────────────────────────────────────

    [Fact]
    public void ExportPreamble_ContainsAllValidationPackages()
    {
        // Extract package names (the {name} part) from both constants
        static HashSet<string> ExtractPackageNames(string preamble)
        {
            var names = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(preamble, @"\\usepackage(\[.*?\])?\{([^}]+)\}"))
            {
                // Some lines have comma-separated packages like amsmath,amssymb,amsfonts,amsthm
                foreach (var pkg in m.Groups[2].Value.Split(','))
                    names.Add(pkg.Trim());
            }
            return names;
        }

        var exportPackages = ExtractPackageNames(LaTeXPreamble.Packages);
        var validationPackages = ExtractPackageNames(LaTeXPreamble.ValidationPackages);

        // Every package used in validation must also be present in the export preamble
        foreach (var pkg in validationPackages)
        {
            exportPackages.Should().Contain(pkg,
                $"validation package '{pkg}' should be present in LaTeXPreamble.Packages");
        }

        // The export preamble should have at least as many packages
        exportPackages.Count.Should().BeGreaterThanOrEqualTo(validationPackages.Count);
    }

    // ── 1.3 Table column alignment ──────────────────────────────────

    [Fact]
    public void Table_WithColumnAlignment_GeneratesCorrectSpec()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A","B","C"],["1","2","3"]],
                "columnAlign": ["l","c","r"]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain("{lcr}");
    }

    [Fact]
    public void Table_WithDefaultAlignment_UsesLeftForAll()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["A","B","C"],["1","2","3"]]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain("{lll}");
    }

    [Fact]
    public void Table_AlignmentInHtml_AddsTextAlign()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["Header"],["Data"]],
                "columnAlign": ["c"]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("style=\"text-align: center\"");
    }

    [Fact]
    public void Table_RightAlignmentInHtml_AddsTextAlignRight()
    {
        var block = CreateBlock("table", """
            {
                "rows": [["Header"],["Data"]],
                "columnAlign": ["r"]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("style=\"text-align: right\"");
    }

    [Fact]
    public void Table_LeftAlignmentInHtml_NoStyleAttribute()
    {
        // Left alignment is the default, so no extra style needed
        var block = CreateBlock("table", """
            {
                "rows": [["Header"],["Data"]],
                "columnAlign": ["l"]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().NotContain("text-align");
    }

    // ── 1.4 Table colspan / rowspan ─────────────────────────────────

    [Fact]
    public void Table_WithColspan_GeneratesMulticolumn()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Spanning","colspan":2}, "C"],
                    ["1","2","3"]
                ]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\multicolumn{2}{l}{Spanning}");
    }

    [Fact]
    public void Table_WithRowspan_GeneratesMultirow()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Tall","rowspan":2}, "B"],
                    ["D"]
                ]
            }
            """);

        var latex = _sut.RenderBlockToLatex(block);

        latex.Should().Contain(@"\multirow{2}{*}{Tall}");
    }

    [Fact]
    public void Table_WithColspan_HtmlHasColspanAttribute()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Wide","colspan":3}],
                    ["A","B","C"]
                ]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("colspan=\"3\"");
    }

    [Fact]
    public void Table_WithRowspan_HtmlHasRowspanAttribute()
    {
        var block = CreateBlock("table", """
            {
                "rows": [
                    [{"text":"Tall","rowspan":2}, "B"],
                    ["D"]
                ]
            }
            """);

        var html = _sut.RenderBlockToHtml(block);

        html.Should().Contain("rowspan=\"2\"");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static Block CreateBlock(string type, string contentJson)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = type,
            Content = JsonDocument.Parse(contentJson),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }
}
