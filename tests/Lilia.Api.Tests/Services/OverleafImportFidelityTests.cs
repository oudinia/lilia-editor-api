using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Six Overleaf projects written in each publisher's own template (ACM, IEEE, Elsevier, Springer LNCS,
/// APS, and a multi-chapter report; lilia-docs/reports/overleaf-test-projects, 26 Oct 2026), imported
/// the way the upload does it: the project's files inlined, parsed, mapped to blocks. Each test is a
/// bug those projects found.
/// </summary>
public class OverleafImportFidelityTests
{
    internal static async Task<(ImportDocument Doc, List<(string Type, JsonElement Content)> Blocks)> Import(string project)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "overleaf-projects", project + ".zip"));
        var tex = new LatexProjectExtractor().Extract(bytes).InlinedTex;
        var doc = await new LatexParser().ParseTextAsync(tex, new LatexImportOptions
        {
            ExtractDocumentTitle = true,
            ConvertEquationEnvironments = true,
            ConvertDisplayMath = true,
        });
        var blocks = LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, JsonSerializer.SerializeToElement(b.content)))
            .ToList();
        return (doc, blocks);
    }

    private static string Str(JsonElement e, string field) =>
        e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static IEnumerable<JsonElement> OfType(List<(string Type, JsonElement Content)> blocks, string type) =>
        blocks.Where(b => b.Type == type).Select(b => b.Content);

    private static string AllText(List<(string Type, JsonElement Content)> blocks) =>
        string.Join("\n", blocks.Select(b => b.Content.GetRawText()));

    [Theory]
    [InlineData("acm-sigconf", "fig:latency", "fig:heatmap")]
    [InlineData("ieee-conference", "fig:f1")]
    [InlineData("springer-lncs", "fig:scaling")]
    [InlineData("aps-revtex", "fig:spectrum")]
    [InlineData("report-multichapter", "fig:turbidity", "fig:sensors")]
    public async Task A_figure_keeps_its_label(string project, params string[] labels)
    {
        var (_, blocks) = await Import(project);
        OfType(blocks, "figure").Select(f => Str(f, "label")).Should().Contain(labels);
    }

    [Fact]
    public async Task A_label_inside_the_caption_is_the_figures_label_not_caption_text()
    {
        var (_, blocks) = await Import("aps-revtex");
        var figure = OfType(blocks, "figure").Single();
        Str(figure, "label").Should().Be("fig:spectrum");
        Str(figure, "caption").Should().StartWith("Measured response").And.NotContain(@"\label");
    }

    [Fact]
    public async Task A_caption_keeps_its_references()
    {
        var (_, blocks) = await Import("elsevier-multifile");
        var figure = OfType(blocks, "figure").Single();
        figure.GetProperty("subfigures")[0].GetProperty("caption").GetString().Should().Contain(@"\eqref{eq:spectrum}");
    }

    [Fact]
    public async Task Subfigures_stay_subfigures_with_their_images_captions_and_labels()
    {
        var (_, blocks) = await Import("elsevier-multifile");
        var figure = OfType(blocks, "figure").Single();
        Str(figure, "label").Should().Be("fig:spectra");
        var subs = figure.GetProperty("subfigures").EnumerateArray().ToList();
        subs.Select(s => Str(s, "src")).Should().Equal("figures/spectrum.pdf", "figures/heatmap.png");
        subs.Select(s => Str(s, "label")).Should().Equal("fig:spectra-model", "fig:spectra-measured");
        Str(subs[1], "caption").Should().Be("Measured intensity map.");
    }

    [Fact]
    public async Task ACMs_Description_is_the_figures_description_and_the_caption_stays_the_caption()
    {
        var (_, blocks) = await Import("acm-sigconf");
        var figure = OfType(blocks, "figure").First();
        Str(figure, "caption").Should().Be("Compile time against document size, full rebuild and incremental.");
        Str(figure, "alt").Should().StartWith("A line chart. Compile time grows");
    }

    [Fact]
    public async Task A_figures_width_is_kept()
    {
        var (_, blocks) = await Import("springer-lncs");
        OfType(blocks, "figure").Single().GetProperty("width").GetDouble().Should().Be(0.8);
    }

    [Fact]
    public async Task An_algorithmic_inside_a_figure_is_an_algorithm()
    {
        var (_, blocks) = await Import("ieee-conference");
        blocks.Should().Contain(b => b.Type == "algorithm" && Str(b.Content, "label") == "alg:decode" && Str(b.Content, "caption") == "Row-by-row decoding of a table region.");
        OfType(blocks, "figure").Should().OnlyContain(f => Str(f, "src").Length > 0, "no empty figure is left in its place");
    }

    [Fact]
    public async Task A_listing_keeps_its_caption_and_label()
    {
        var (_, blocks) = await Import("acm-sigconf");
        var code = OfType(blocks, "code").Single(c => Str(c, "code").Contains("def invalidated"));
        Str(code, "label").Should().Be("lst:split");
        Str(code, "caption").Should().Be("Finding the blocks to recompile.");
    }

    [Fact]
    public async Task Each_line_of_an_align_keeps_its_label()
    {
        var (_, blocks) = await Import("aps-revtex");
        var align = OfType(blocks, "equation").First(e => Str(e, "latex").Contains(@"\ddot{x}_1"));
        var latex = Str(align, "latex");
        latex.Should().Contain(@"\label{eq:x1}").And.Contain(@"\label{eq:x2}");
    }

    [Theory]
    [InlineData("elsevier-multifile", @"\SI{4}{\percent}")]
    [InlineData("report-multichapter", @"\SI{25}{\milli\gram\per\liter}")]
    public async Task siunitx_stays_as_written(string project, string expected)
    {
        var (_, blocks) = await Import(project);
        var abstractText = Str(OfType(blocks, "abstract").Single(), "text");
        abstractText.Should().Contain(expected);
    }

    [Fact]
    public async Task The_papers_own_macros_stay_as_written()
    {
        var (_, acm) = await Import("acm-sigconf");
        Str(OfType(acm, "abstract").Single(), "text").Should().Contain(@"\system{}");
        var (_, report) = await Import("report-multichapter");
        // \conc adds units and \todo adds "[TODO: …]": as written. \station only formats its argument: the argument.
        AllText(report).Should().Contain(@"\\conc{25}").And.Contain(@"\\todo{add the December data").And.Contain("Nine stations, R1 to R9");
    }

    [Fact]
    public async Task A_nested_list_is_a_list_inside_an_item()
    {
        var (_, blocks) = await Import("report-multichapter");
        var list = OfType(blocks, "list").Single();
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(3);
        var nested = items[1];
        nested.GetProperty("text").GetString().Should().StartWith("the measurements, by season and by station");
        nested.GetProperty("children").EnumerateArray().Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetProperty("text").GetString())
            .Should().Equal("nitrate and phosphate;", "turbidity and temperature;");
        AllText(blocks).Should().NotContain("itemize");
    }

    [Fact]
    public async Task A_biblatex_textcite_stays_a_citation()
    {
        var (_, blocks) = await Import("report-multichapter");
        AllText(blocks).Should().Contain(@"\\citet{hydro2019}", "the export keeps natbib forms and writes \\textcite back for biblatex");
    }

    [Theory]
    [InlineData("ieee-conference", "Layout-Aware Extraction of Tables and Equations from Scanned Engineering Reports")]
    [InlineData("springer-lncs", "Shortest Paths over Versioned Graphs")]
    [InlineData("report-multichapter", "Field Report: Water Quality Monitoring in the Upper Rhône Basin, 2026")]
    public async Task The_documents_name_is_plain_text(string project, string expected)
    {
        var (doc, _) = await Import(project);
        LatexImportJobExecutor.DocumentName(doc.Title).Should().Be(expected);
    }

    [Fact]
    public async Task IEEE_author_names_lose_the_templates_ordinal()
    {
        var (doc, _) = await Import("ieee-conference");
        doc.Metadata.Author.Should().StartWith(@"Sofia Marquez\\").And.NotContain("textsuperscript");
    }

    [Fact]
    public async Task Elsevier_emails_are_read()
    {
        var (doc, _) = await Import("elsevier-multifile");
        doc.Metadata.Author.Should().Contain("maya.lindqvist@kth.example").And.Contain("r.costa@ist.example").And.Contain("yusuf.demir@kth.example");
    }

    [Fact]
    public async Task Elseviers_MSC_codes_are_not_keywords()
    {
        var (doc, _) = await Import("elsevier-multifile");
        doc.Metadata.Keywords.Should().Be("microresonators, damped oscillators, resonance broadening, coupled modes");
    }

    [Fact]
    public async Task LNCS_shared_emails_go_to_their_authors()
    {
        var (doc, _) = await Import("springer-lncs");
        var authors = doc.Metadata.Author!.Split(@"\and").Select(a => a.Trim()).ToList();
        authors[0].Should().Contain("p.vanderberg@tudelft.example");
        authors[1].Should().Contain("chiara.romano@polimi.example");
        authors[2].Should().Contain("t.novak@tudelft.example");
        doc.Metadata.Author.Should().NotContain(", @");
    }

    [Theory]
    [InlineData("acm-sigconf", "acks")]
    [InlineData("aps-revtex", "acknowledgments")]
    [InlineData("elsevier-multifile", "highlights")]
    [InlineData("springer-lncs", "credits")]
    public async Task A_publisher_environment_is_kept_as_LaTeX_not_as_code(string project, string env)
    {
        var (_, blocks) = await Import(project);
        blocks.Should().Contain(b => b.Type == "embed" && Str(b.Content, "code").Contains(@"\begin{" + env + "}"));
        OfType(blocks, "code").Should().NotContain(c => Str(c, "code").Contains(@"\begin{" + env + "}"));
    }
}
