using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>The publisher compliance check (publisher templates step 7, 25 Oct 2026).</summary>
public class PublisherComplianceTests
{
    private static Document Acm(string author = "Ada Lovelace\\\\Analytical Engines\\\\UK \\and Alan Turing\\\\Bletchley Park\\\\UK")
    {
        var doc = PublisherClassTests.Doc("acmart", "sigconf,review,anonymous", author, new[] { "LaTeX" });
        doc.PublisherProfile = "acm-sigconf-review";
        return doc;
    }

    private static ComplianceReport Check(Document doc)
    {
        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        return PublisherCompliance.Check(doc, blocks, tex);
    }

    private static void Add(Document doc, string type, object content)
    {
        var b = PublisherClassTests.B(type, content);
        b.SortOrder = doc.Blocks.Count; b.Document = doc; b.DocumentId = doc.Id;
        doc.Blocks.Add(b);
    }

    private static void SetContent(Document doc, string type, object content) =>
        doc.Blocks.First(b => b.Type == type).Content = JsonDocument.Parse(JsonSerializer.Serialize(content));

    [Fact]
    public void A_complete_ACM_paper_passes()
    {
        var report = Check(Acm());
        report.Findings.Should().BeEmpty();
        report.Status.Should().Be("passed");
        report.GuidelinesUrl.Should().Be(PublisherProfiles.Find("acm-sigconf-review")!.GuidelinesUrl);
    }

    [Fact]
    public void A_document_with_no_publisher_has_nothing_to_check()
    {
        var doc = PublisherClassTests.Doc("article");
        var report = PublisherCompliance.Check(doc, doc.Blocks.ToList(), "");
        report.Findings.Should().BeEmpty();
        report.Profile.Should().BeNull();
    }

    [Fact]
    public void Class_and_options_are_checked_against_the_profile()
    {
        var doc = Acm();
        doc.LatexDocumentClassOptions = "sigconf";
        Check(doc).Findings.Should().ContainSingle(f => f.Code == "class_options" && f.Severity == "warning");
        doc.LatexDocumentClassOptions = "anonymous, review ,sigconf";
        Check(doc).Findings.Should().NotContain(f => f.Code == "class_options", "the order and spaces don't matter");
    }

    [Fact]
    public void Missing_metadata_is_named_with_its_severity()
    {
        var doc = Acm(author: "Ada Lovelace \\and Alan Turing\\\\Bletchley Park");
        SetContent(doc, "abstract", new { text = "" });
        SetContent(doc, "title", new { title = "Visual Blocks", author = "Ada Lovelace \\and Alan Turing\\\\Bletchley Park" });
        var report = Check(doc);
        report.Status.Should().Be("errors");
        report.Findings.Should().Contain(f => f.Code == "abstract" && f.Severity == "error");
        report.Findings.Should().Contain(f => f.Code == "keywords" && f.Severity == "warning");
        report.Findings.Should().Contain(f => f.Code == "affiliation" && f.Message.StartsWith("Ada Lovelace"));
        report.Findings.Where(f => f.Code == "country").Should().HaveCount(2, "Ada has no affiliation, Alan one line");
    }

    [Fact]
    public void A_citation_without_an_entry_is_an_error()
    {
        var doc = Acm();
        Add(doc, "paragraph", new { text = "And @cite{nowhere99}." });
        Check(doc).Findings.Should().Contain(f => f.Code == "citation" && f.Message.Contains("nowhere99") && f.Severity == "error");
    }

    [Fact]
    public void Figures_without_an_image_or_in_a_format_pdflatex_cannot_include()
    {
        var doc = Acm();
        Add(doc, "figure", new { caption = "Empty" });
        Add(doc, "figure", new { src = "/uploads/a/diagram.svg", caption = "Vector" });
        Add(doc, "figure", new { src = "/uploads/a/plot.eps", caption = "Old" });
        Add(doc, "figure", new { src = "/uploads/a/fine.png", caption = "Fine" });
        var findings = Check(doc).Findings;
        findings.Should().ContainSingle(f => f.Code == "figure_missing" && f.Severity == "error");
        findings.Should().ContainSingle(f => f.Code == "figure_format" && f.Severity == "error" && f.Message.StartsWith("diagram.svg"));
        findings.Should().ContainSingle(f => f.Code == "figure_format" && f.Severity == "info" && f.Message.StartsWith("plot.eps"));
    }

    [Fact]
    public void The_authors_own_LaTeX_may_not_change_the_layout_or_the_references()
    {
        var doc = Acm();
        doc.CustomPreamble = "\\usepackage[margin=1in]{geometry}\n\\linespread{1.2}";
        doc.LatexPackages = """[{"name":"times"}]""";
        Add(doc, "embed", new { engine = "latex", code = "\\bibliographystyle{plain}" });
        var findings = Check(doc).Findings;
        findings.Where(f => f.Code == "layout_override").Select(f => f.Message.Split(' ')[0])
            .Should().BeEquivalentTo(["\\usepackage[margin=1in]{geometry}", "\\linespread{", "\\usepackage{times}"]);
        findings.Should().ContainSingle(f => f.Code == "bibliography_style" && f.Message.Contains("ACM-Reference-Format"));
    }

    [Fact]
    public void Template_sample_text_left_in_is_a_warning()
    {
        var doc = Acm();
        Add(doc, "paragraph", new { text = "Lorem ipsum dolor sit amet." });
        Check(doc).Findings.Should().ContainSingle(f => f.Code == "sample_text" && f.Severity == "warning");
    }
}
