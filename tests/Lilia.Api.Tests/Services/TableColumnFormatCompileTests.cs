using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Themes;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// 2g's table options compiled for real, with pdfLaTeX and LuaLaTeX, through the exporter and
/// compile service the PDF export uses: an S column with mixed signs and a word in it, a unit
/// in the header, and the best value in bold. The numbers must come out of the PDF (pdftotext),
/// and the bold one must be set in a bold face (pdffonts), which only a table whose sole bold
/// text is that number can show.
/// </summary>
[Trait("Category", "TableCompile")]
public class TableColumnFormatCompileTests
{
    private static Block Table(object content) => new()
    {
        Id = Guid.NewGuid(), Type = "table", SortOrder = 1,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static string Latex(params Block[] blocks)
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "Tables", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 };
        var all = new List<Block> { new() { Id = Guid.NewGuid(), Type = "paragraph", SortOrder = 0, Content = JsonDocument.Parse("""{"text":"Quartz zebra before the table."}""") } };
        all.AddRange(blocks);
        return new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, all, [], new LaTeXExportOptions());
    }

    private static Task<byte[]> Compile(string tex, string engine) =>
        new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, engine, timeout: 180);

    [Theory]
    [InlineData("pdflatex")]
    [InlineData("lualatex")]
    public async Task An_S_column_with_a_unit_signs_a_word_and_a_bold_best_compiles(string engine)
    {
        var tex = Latex(Table(new
        {
            caption = "Results",
            headers = new[] { "Model", "Top-1", "Delta", "Params" },
            rows = new[]
            {
                new[] { "Baseline", "76.1%", "-1.25", "1,204" },
                new[] { "Ours", "82.6%", "+2.5", "87" },
                new[] { "Other", "n/a", "0.75", "12" },
            },
            // Top-1 holds a word, so it is decimal because the author said so; Delta and Params
            // are columns of numbers and become S columns by themselves.
            columnAlign = new[] { "l", "decimal" },
            columnUnit = new[] { null, "%", null, null },
            columnBest = new[] { null, "higher", "lower", null },
        }));
        tex.Should().Contain(@"\begin{tabular}{lS[table-format=2.1, mode=text, reset-text-series=false]"
            + "S[table-format=-1.2, mode=text, retain-explicit-plus, reset-text-series=false]"
            + "S[table-format=4, mode=text, group-digits=integer, group-separator={,}, group-minimum-digits=4]}");
        tex.Should().Contain(@"Other & {n/a} & 0.75 & 12 \\");
        tex.Should().Contain(@"{\liliaTableHead{\textbf{Top-1 (\%)}}}");

        var pdf = await Compile(tex, engine);
        var text = await DocumentThemeCompileTests.Tool("pdftotext", pdf, "-");
        foreach (var s in new[] { "Top-1 (%)", "76.1", "82.6", "n/a", "1.25", "+2.5", "0.75", "1,204", "87", "Baseline" })
            text.Should().Contain(s, $"{engine} must print {s}");
    }

    [Theory]
    [InlineData("pdflatex")]
    [InlineData("lualatex")]
    public async Task The_best_value_is_set_in_a_bold_face(string engine)
    {
        // No header row and no caption, so the only bold text in the document is the best value.
        object Content(string? best) => new
        {
            rows = new[] { new[] { "Alpha", "0.912" }, new[] { "Beta", "0.934" }, new[] { "Gamma", "-0.5" } },
            columnAlign = new[] { "l", "decimal" },
            columnBest = new[] { null, best },
        };

        var plain = await Compile(Latex(Table(Content(null))), engine);
        (await DocumentThemeCompileTests.Tool("pdffonts", plain)).Should().NotContain("Bold", "nothing is bold without a best value");

        var tex = Latex(Table(Content("higher")));
        tex.Should().Contain(@"\bfseries 0.934");
        var pdf = await Compile(tex, engine);
        (await DocumentThemeCompileTests.Tool("pdftotext", pdf, "-")).Should().Contain("0.934").And.Contain("0.912");
        (await DocumentThemeCompileTests.Tool("pdffonts", pdf)).Should().Contain("Bold", $"{engine}: the best value is bold in the S column");
    }
}
