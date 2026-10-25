using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// ACM's accepted packages (TAPS list, April 2025; phase 2, 25 Oct 2026): Lilia loads only what ACM
/// accepts unless the document uses a feature that needs more, and the check says which.
/// </summary>
public class PublisherPackagesTests
{
    private static Document Acm()
    {
        var doc = PublisherClassTests.Doc("acmart", "sigconf", "Ada Lovelace\\\\Analytical Engines\\\\UK", new[] { "LaTeX" });
        doc.PublisherProfile = "acm-sigconf-final";
        return doc;
    }

    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

    private static void Add(Document doc, string type, object content)
    {
        var b = PublisherClassTests.B(type, content);
        b.SortOrder = doc.Blocks.Count; b.Document = doc; b.DocumentId = doc.Id;
        doc.Blocks.Add(b);
    }

    private static ComplianceReport Check(Document doc) =>
        PublisherCompliance.Check(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), Export(doc));

    [Fact]
    public void An_ACM_paper_loads_only_packages_ACM_accepts()
    {
        var tex = Export(Acm());
        var accepted = PublisherPackages.Accepted("acmart")!;
        PublisherPackages.Loaded(tex).Where(p => !accepted.Contains(p)).Should().BeEmpty("the paper uses none of the features that need more");
        tex.Should().NotContain(@"\newunicodechar").And.NotContain(@"\normalem");
        Check(Acm()).Findings.Should().NotContain(f => f.Code == "package_not_accepted");
    }

    [Fact]
    public void A_feature_that_needs_a_package_keeps_it_and_the_check_names_the_feature()
    {
        var doc = Acm();
        Add(doc, "callout", new { variant = "note", title = "Note", text = "Remember this." });
        var tex = Export(doc);
        PublisherPackages.Loaded(tex).Should().Contain("tcolorbox");
        Check(doc).Findings.Should().ContainSingle(f => f.Code == "package_not_accepted"
            && f.Severity == "warning" && f.Message.StartsWith("tcolorbox") && f.Message.Contains("callout blocks"));
    }

    [Fact]
    public void A_Greek_letter_typed_as_a_character_keeps_its_mapping()
    {
        var doc = Acm();
        Add(doc, "paragraph", new { text = "The angle α is small." });
        Export(doc).Should().Contain(@"\usepackage{newunicodechar}").And.Contain(@"\newunicodechar{α}");
    }

    [Fact]
    public void The_authors_own_unaccepted_package_is_an_error_and_one_acmart_loads_is_a_note()
    {
        var doc = Acm();
        doc.CustomPreamble = "\\usepackage{tikz}\n\\usepackage{booktabs}";
        var findings = Check(doc).Findings;
        findings.Should().ContainSingle(f => f.Code == "package_not_accepted" && f.Severity == "error" && f.Message.StartsWith("tikz"));
        findings.Should().ContainSingle(f => f.Code == "package_in_class" && f.Severity == "info" && f.Message.Contains("booktabs"));
    }

    [Fact]
    public void Other_publishers_publish_no_list_and_keep_Lilias_packages()
    {
        PublisherPackages.Accepted("IEEEtran").Should().BeNull();
        var ieee = PublisherClassTests.Doc("IEEEtran", "conference");
        PublisherPackages.Loaded(Export(ieee)).Should().Contain("tcolorbox");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task ACM_papers_compile_with_the_trimmed_preamble_and_with_a_kept_package()
    {
        foreach (var doc in new[] { Acm(), WithCallout() })
        {
            var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(Export(doc), "pdflatex", timeout: 180);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        }

        static Document WithCallout()
        {
            var doc = Acm();
            Add(doc, "callout", new { variant = "note", title = "Note", text = "Remember α." });
            return doc;
        }
    }
}
