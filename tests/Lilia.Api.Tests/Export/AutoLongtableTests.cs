using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P2.3b — re-emitting a table that ran off the page as a <c>longtable</c>.
///
/// <para><b>longtable is not a variant of the float, it replaces it.</b> That is
/// the whole reason it can break across pages, and it is why these tests check
/// the absence of <c>\begin{table}</c> as carefully as the presence of
/// <c>\begin{longtable}</c> — leaving the float wrapper in place would produce a
/// document that does not compile.</para>
///
/// <para><b>Two-column is a hard error, not a warning.</b> Verified with
/// pdflatex: <c>"Package longtable Error: longtable not in 1-column mode"</c>.
/// So converting an overflowing table in a two-column document would turn a
/// table that merely looks wrong into a document that will not build — strictly
/// worse than the problem. <see cref="RenderService.SupportsLongtable"/> is the
/// guard, and it is tested here because getting it wrong is not recoverable at
/// runtime.</para>
/// </summary>
public class AutoLongtableTests
{
    private static readonly RenderService Render = BuildRenderService();

    private static RenderService BuildRenderService()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RenderService(new LiliaDbContext(opts), NullLogger<RenderService>.Instance);
    }

    private static Block TableBlock(int rows = 3, string caption = "Measurements")
    {
        var body = new StringBuilder();
        for (var i = 1; i <= rows; i++)
        {
            if (i > 1) body.Append(',');
            body.Append($"""["Row {i}","{i}"]""");
        }

        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "table",
            SortOrder = 0,
            Content = JsonDocument.Parse(
                $$"""{"caption":"{{caption}}","headers":["Sample","Reading"],"rows":[{{body}}]}"""),
        };
    }

    // ── Emission shape ────────────────────────────────────────────────

    [Fact]
    public void By_default_a_table_is_still_a_float_wrapped_tabular()
    {
        var latex = Render.RenderBlockToLatex(TableBlock());

        latex.Should().Contain(@"\begin{table}");
        latex.Should().Contain(@"\begin{tabular}");
        latex.Should().NotContain(@"\begin{longtable}");
    }

    [Fact]
    public void As_a_longtable_the_float_wrapper_is_gone_entirely()
    {
        var latex = Render.RenderBlockToLatex(TableBlock(), useLongtable: true);

        latex.Should().Contain(@"\begin{longtable}");
        latex.Should().Contain(@"\end{longtable}");
        // The load-bearing assertion: longtable inside \begin{table} does not
        // compile, and leaving the wrapper behind is the obvious way to get this
        // wrong.
        latex.Should().NotContain(@"\begin{table}");
        latex.Should().NotContain(@"\begin{tabular}");
    }

    [Fact]
    public void The_caption_survives_the_conversion()
    {
        // There is no float to attach a caption to, so it moves inside the
        // environment. Losing it would be exactly the silent content loss this
        // plan exists to remove.
        var latex = Render.RenderBlockToLatex(TableBlock(caption: "Measurements"), useLongtable: true);

        latex.Should().Contain(@"\caption{Measurements}");
    }

    [Fact]
    public void The_rows_survive_the_conversion()
    {
        var asFloat = Render.RenderBlockToLatex(TableBlock(rows: 4));
        var asLong = Render.RenderBlockToLatex(TableBlock(rows: 4), useLongtable: true);

        foreach (var marker in new[] { "Row 1", "Row 4", "Sample", "Reading" })
        {
            asFloat.Should().Contain(marker);
            asLong.Should().Contain(marker, "converting the wrapper must not drop content");
        }
    }

    [Fact]
    public void Booktabs_rules_are_kept_so_the_table_still_looks_the_same()
    {
        var latex = Render.RenderBlockToLatex(TableBlock(), useLongtable: true);

        latex.Should().Contain(@"\toprule").And.Contain(@"\bottomrule");
    }

    // ── The two-column guard ──────────────────────────────────────────

    private static Document Doc(int columns = 1, bool balanced = false) => new()
    {
        Id = Guid.NewGuid(),
        Title = "T",
        Columns = columns,
        BalancedColumns = balanced,
    };

    [Fact]
    public void A_single_column_document_can_use_longtable()
    {
        RenderService.SupportsLongtable(Doc()).Should().BeTrue();
    }

    [Fact]
    public void A_two_column_document_cannot()
    {
        // "Package longtable Error: longtable not in 1-column mode" — a hard
        // error. Converting here would break the build, not improve the layout.
        RenderService.SupportsLongtable(Doc(columns: 2)).Should().BeFalse();
    }

    [Fact]
    public void Balanced_columns_also_rule_it_out()
    {
        // Balanced columns go through multicol rather than the twocolumn class
        // option, but it is still multi-column as far as longtable is concerned.
        RenderService.SupportsLongtable(Doc(columns: 2, balanced: true)).Should().BeFalse();
        RenderService.SupportsLongtable(Doc(columns: 1, balanced: true)).Should().BeFalse();
    }
}
