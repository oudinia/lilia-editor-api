using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// ACM's TAPS rules (phase 3, 25 Oct 2026; ACM's "LaTeX Best Practices", May 2024): an email per author,
/// CCS concepts and the rights block as ACM's tools give them, a description in every figure.
/// </summary>
public class AcmTapsTests
{
    private const string Ccs = """
        \begin{CCSXML}
        <ccs2012>
        <concept><concept_id>10011007.10011006.10011041</concept_id><concept_desc>Software and its engineering~Compilers</concept_desc><concept_significance>500</concept_significance></concept>
        </ccs2012>
        \end{CCSXML}
        \ccsdesc[500]{Software and its engineering~Compilers}
        """;

    private const string Rights = """
        \setcopyright{acmlicensed}
        \acmDOI{10.1145/1234567.8901234}
        \acmISBN{978-1-4503-0000-0/26/10}
        \acmConference[LILIA '26]{The Lilia Conference}{October 2026}{Paris, France}
        \acmYear{2026}
        \copyrightyear{2026}
        """;

    private static Document Acm(string stage = "final", object? ccs = null, object? rights = null,
        string author = "Ada Lovelace\\\\Analytical Engines\\\\UK\\\\ada@engines.uk")
    {
        var p = PublisherProfiles.Find($"acm-sigconf-{stage}")!;
        var doc = PublisherClassTests.Doc(p.DocumentClass, p.ClassOptions, author, new[] { "LaTeX" });
        doc.PublisherProfile = p.Id;
        var title = doc.Blocks.First(b => b.Type == "title");
        title.Content = JsonDocument.Parse(JsonSerializer.Serialize(new { title = "A Paper", author, keywords = new[] { "LaTeX" }, acmCcs = ccs, acmRights = rights }));
        return doc;
    }

    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

    private static IReadOnlyList<ComplianceFinding> Findings(Document doc) =>
        PublisherCompliance.Check(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), Export(doc)).Findings;

    private static void AddFigure(Document doc, string? alt)
    {
        var b = PublisherClassTests.B("figure", new { src = "/uploads/a/plot.png", caption = "A plot", alt });
        b.SortOrder = doc.Blocks.Count; b.Document = doc; b.DocumentId = doc.Id;
        doc.Blocks.Add(b);
    }

    [Fact]
    public void An_email_line_becomes_the_authors_email_not_part_of_the_affiliation()
    {
        var tex = Export(Acm());
        tex.Should().Contain(@"\email{ada@engines.uk}");
        tex.Should().Contain(@"\affiliation{\institution{Analytical Engines}\country{UK}}");
    }

    [Fact]
    public void CCS_concepts_and_the_rights_block_go_in_as_ACMs_tools_give_them()
    {
        var tex = Export(Acm(ccs: Ccs, rights: Rights));
        tex.Should().Contain(@"\ccsdesc[500]{Software and its engineering~Compilers}").And.Contain(@"\begin{CCSXML}");
        tex.Should().Contain(@"\setcopyright{acmlicensed}").And.Contain(@"\acmConference[LILIA '26]{The Lilia Conference}{October 2026}{Paris, France}");
        tex.IndexOf(@"\setcopyright", StringComparison.Ordinal).Should().BeLessThan(tex.IndexOf(@"\begin{document}", StringComparison.Ordinal));
        Findings(Acm(ccs: Ccs, rights: Rights)).Should().NotContain(f => f.Code.StartsWith("acm_") && f.Severity != "info");
    }

    [Fact]
    public void Anything_else_pasted_there_is_left_out_and_reported()
    {
        var sneaky = "\\setcopyright{acmlicensed}\n\\acmDOI{10.1/x}\\input{/etc/passwd}\n\\usepackage{tikz}";
        var tex = Export(Acm(ccs: Ccs, rights: sneaky));
        tex.Should().NotContain(@"\input{/etc/passwd}").And.NotContain(@"\usepackage{tikz}");
        Findings(Acm(ccs: Ccs, rights: sneaky)).Where(f => f.Code == "acm_metadata_line").Should().HaveCount(2);
    }

    [Fact]
    public void The_final_version_needs_the_rights_block_CCS_concepts_and_an_email_per_author()
    {
        var findings = Findings(Acm(author: "Ada Lovelace\\\\Analytical Engines\\\\UK"));
        findings.Should().ContainSingle(f => f.Code == "acm_rights" && f.Severity == "error");
        findings.Should().ContainSingle(f => f.Code == "acm_ccs" && f.Severity == "warning");
        findings.Should().ContainSingle(f => f.Code == "acm_email" && f.Message.Contains("Ada Lovelace"));
        Findings(Acm(stage: "review")).Select(f => f.Code).Should().NotContain(["acm_rights", "acm_ccs", "acm_email"], "the review version doesn't need them");
    }

    [Fact]
    public void Every_figure_gets_its_description_and_a_file_name_is_not_one()
    {
        var doc = Acm(ccs: Ccs, rights: Rights);
        AddFigure(doc, "A line chart: compile time falls as the cache warms.");
        AddFigure(doc, "plot.png");
        AddFigure(doc, null);
        Export(doc).Should().Contain(@"\Description{A line chart: compile time falls as the cache warms.}")
            .And.NotContain(@"\Description{plot.png}");
        Findings(doc).Where(f => f.Code == "acm_description").Should().HaveCount(2);

        // Other classes have no \Description.
        var ieee = PublisherClassTests.Doc("IEEEtran", "conference");
        AddFigure(ieee, "A chart.");
        Export(ieee).Should().NotContain(@"\Description");
    }

    [Fact]
    public void Redefined_standard_commands_and_negative_spacing_are_flagged()
    {
        var doc = Acm(ccs: Ccs, rights: Rights);
        doc.CustomPreamble = "\\renewcommand{\\section}{\\textbf}\n\\setstretch{1.1}";
        var b = PublisherClassTests.B("embed", new { engine = "latex", code = "\\vspace{-3mm}" });
        b.SortOrder = doc.Blocks.Count; b.Document = doc; b.DocumentId = doc.Id; doc.Blocks.Add(b);
        var findings = Findings(doc);
        findings.Should().Contain(f => f.Code == "acm_redefinition" && f.Message.Contains(@"\renewcommand{\section"));
        findings.Should().Contain(f => f.Code == "layout_override" && f.Message.Contains(@"\setstretch"));
        findings.Should().Contain(f => f.Code == "acm_spacing");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_complete_ACM_final_paper_compiles()
    {
        var doc = Acm(ccs: Ccs, rights: Rights);
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(Export(doc), "pdflatex", timeout: 180);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
