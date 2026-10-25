using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// Publisher polish (26 Oct 2026): each author's email in the class's own command; Lilia's shims only
/// where used; column layouts where REVTeX refuses multicol; figures and tables the text never cites.
/// </summary>
public class PublisherPolishTests
{
    private const string TwoAuthors = "Ada Lovelace\\\\Analytical Engines\\\\UK\\\\ada@example.org \\and Alan Turing\\\\Bletchley Park\\\\UK\\\\alan@example.org";

    private static string Front(string cls) =>
        string.Concat(PublisherClasses.FrontMatter(cls, "T", TwoAuthors, null, [], s => s, "T") is var (pre, body) ? pre + body : "");

    [Theory]
    [InlineData("acmart", @"\email{ada@example.org}")]
    [InlineData("elsarticle", @"\ead{ada@example.org}")]
    [InlineData("revtex4-2", @"\email{ada@example.org}")]
    [InlineData("amsart", @"\email{ada@example.org}")]
    [InlineData("llncs", @"\email{ada@example.org}")]
    [InlineData("iopjournal", @"\email{ada@example.org, alan@example.org}")]
    [InlineData("siamart251216", @"\thanks{Analytical Engines, UK (\email{ada@example.org}).}")]
    [InlineData("IEEEtran", @"\IEEEauthorblockA{Analytical Engines\\UK\\ada@example.org}")]
    public void Each_authors_email_goes_in_the_classs_own_command(string cls, string expected)
    {
        var tex = Front(cls);
        tex.Should().Contain(expected);
        if (cls != "IEEEtran")
            tex.Should().NotContain("UK, ada@example.org").And.NotContain(@"UK\\ada@example.org", "the email is not a line of the address");
    }

    [Fact]
    public void llncs_lists_an_institutes_emails_under_it()
    {
        var tex = Front("llncs");
        tex.Should().Contain(@"\institute{Analytical Engines, UK\\ \email{ada@example.org} \and Bletchley Park, UK\\ \email{alan@example.org}}");
    }

    [Fact]
    public void An_email_line_is_not_counted_as_the_address()
    {
        var doc = Doc("acmart", "sigconf", "Ada Lovelace\\\\ada@example.org");
        doc.PublisherProfile = "acm-sigconf-final";
        var findings = PublisherCompliance.Check(doc, doc.Blocks.ToList(), "").Findings;
        findings.Should().Contain(f => f.Code == "affiliation", "an email alone is no affiliation");
        findings.Should().NotContain(f => f.Code == "email");
    }

    [Fact]
    public void Only_the_shims_the_document_uses_are_sent()
    {
        var doc = PublisherBlocksAuditTests.EveryBlock(PublisherProfiles.Find("acm-sigconf-final"));
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        var preamble = tex[..tex.IndexOf(@"\begin{document}", StringComparison.Ordinal)];
        foreach (var unused in new[] { @"\cvitem", @"\SundayColor", @"\SetPaperName", @"\ket", @"{lemma}", @"\liliaSafeUseTheme" })
            preamble.Should().NotContain(unused);
        preamble.Should().Contain(@"\newtheorem{theorem}", "the paper has a theorem");
        preamble.Should().NotContain("MCP-", "none of Lilia's comments");
        tex.Should().NotContain(@"\lilia");
    }

    [Fact]
    public void A_shim_used_only_by_a_dropped_shim_goes_too()
    {
        const string tex = "\\documentclass{article}\n\\makeatletter\n\\@ifundefined{abstractname}{\\newcommand{\\abstractname}{Abstract}}{}\n"
            + "\\@ifundefined{abstract}{\\newenvironment{abstract}{\\section*{\\abstractname}}{}}{}\n\\makeatother\n\\begin{document}\nHi.\n\\end{document}\n";
        var result = PublisherClasses.OnlyUsedShims(tex, tex);
        result.Should().NotContain("abstractname").And.NotContain("makeatletter");
    }

    [Fact]
    public void REVTeX_prints_a_column_layouts_text_in_its_own_columns_and_says_so()
    {
        var doc = PublisherBlocksAuditTests.EveryBlock(PublisherProfiles.Find("aps-final"));
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        tex.Should().NotContain("multicol");
        tex.Should().Contain(@"\csname if@twocolumn\endcsname\newpage\fi", "the column break moves to the next column");
        PublisherCompliance.Check(doc, doc.Blocks.ToList(), tex).Findings.Should().Contain(f => f.Code == "column_layout");
    }

    [Fact]
    public void A_column_break_inside_a_layout_is_multicols_own()
    {
        PublisherClasses.PlainColumnBreaks("\\begin{multicols}{2}\nA\n\\liliaColumnBreak\nB\n\\end{multicols}\n\\liliaColumnBreak\n")
            .Should().Be("\\begin{multicols}{2}\nA\n\\columnbreak\nB\n\\end{multicols}\n\\csname if@twocolumn\\endcsname\\newpage\\fi\n");
    }

    [Fact]
    public void A_figure_or_table_the_text_never_cites_is_a_warning()
    {
        var doc = Doc("elsarticle", "preprint");
        doc.PublisherProfile = "elsevier-preprint";
        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        blocks.Insert(3, B("figure", new { src = "/uploads/a/plot.png", caption = "Plot", label = "fig:plot" }));
        blocks.Insert(3, B("figure", new { src = "/uploads/a/raw.png" }));   // no caption: not numbered, nothing to cite
        for (var i = 0; i < blocks.Count; i++) blocks[i].SortOrder = i;
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

        var findings = PublisherCompliance.Check(doc, blocks, tex).Findings;
        findings.Should().ContainSingle(f => f.Code == "figure_unreferenced").Which.Message.Should().StartWith("Figure 1 is never referred to");
        findings.Should().NotContain(f => f.Code == "table_unreferenced", "the text cites Table~(tab:y)");

        blocks.Add(B("paragraph", new { text = "As Figure~(fig:plot) shows." }));
        tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        PublisherCompliance.Check(doc, blocks, tex).Findings.Should().NotContain(f => f.Code == "figure_unreferenced");
    }

    [Fact]
    public void Submission_names_follow_the_figure_numbers()
    {
        var blocks = new List<Block>
        {
            B("figure", new { src = "/uploads/u/9f2c-plot.png", caption = "A" }),
            B("figure", new { caption = "No image yet" }),
            B("figure", new { caption = "C", subfigures = new[] { new { src = "/uploads/u/left.pdf" }, new { src = "/uploads/u/right.jpg" } } }),
            B("figure", new { src = "/uploads/u/9f2c-plot.png", caption = "The same image again" }),
        };
        var names = FigureFile.SubmissionNames(blocks);
        names.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["9f2c-plot.png"] = "fig1.png", ["left.pdf"] = "fig3a.pdf", ["right.jpg"] = "fig3b.jpg",
        });
        FigureFile.Rename(@"\includegraphics{figures/9f2c-plot.png} \IfFileExists{figures/fig1.png}{x}{y}", new Dictionary<string, string> { ["9f2c-plot.png"] = "fig1.png", ["fig1.png"] = "fig2.png" })
            .Should().Be(@"\includegraphics{figures/fig1.png} \IfFileExists{figures/fig2.png}{x}{y}", "one pass: a renamed file is not renamed again");
    }
}

/// <summary>The submission ZIP's figures, under their numbers, and the LaTeX that includes them.</summary>
[Collection("Integration")]
public class SubmissionFigureNamesTests : IntegrationTestBase
{
    public SubmissionFigureNamesTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_submission_names_its_figures_by_number_and_a_draft_keeps_their_names()
    {
        var owner = $"fig-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(owner);
        var doc = await SeedDocumentAsync(owner, "Figures");
        await SeedBlockAsync(doc.Id, "title", """{"title":"Figures","author":"Ada Lovelace\\\\Analytical Engines\\\\UK"}""", 0);
        await SeedBlockAsync(doc.Id, "figure", """{"src":"/uploads/u/3f9e1c-system.png","caption":"System","label":"fig:sys"}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"See Figure~(fig:sys)."}""", 2);
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(doc.Id);
            d!.LatexDocumentClass = "elsarticle";
            d.PublisherProfile = "elsevier-preprint";
            await db.SaveChangesAsync();
        }
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DownloadAsync(It.IsAny<string>())).ReturnsAsync(() => new MemoryStream(Encoding.ASCII.GetBytes("PNG")));

        async Task<Dictionary<string, string>> Zip(bool submission)
        {
            await using var db = CreateDbContext();
            var service = new LaTeXExportService(db, storage.Object, NullLogger<LaTeXExportService>.Instance);
            using var zip = new ZipArchive(await service.ExportToZipAsync(doc.Id, new LaTeXExportOptions { Submission = submission }));
            return zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
        }

        var sent = await Zip(submission: true);
        sent.Keys.Should().Contain("figures/fig1.png").And.NotContain("figures/3f9e1c-system.png");
        sent["main.tex"].Should().Contain("figures/fig1.png");
        sent["main.tex"].Split('\n').Where(l => l.Contains("3f9e1c")).Should().BeEmpty();

        var draft = await Zip(submission: false);
        draft.Keys.Should().Contain("figures/3f9e1c-system.png", "a draft export keeps the names");
    }
}
