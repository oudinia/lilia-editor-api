using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Engines.TexSafety;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Export.Publishers;

/// <summary>
/// The accessible PDF in every publisher format (launch-bot #257, 19 Nov 2026). It had only been tried on
/// Lilia's own article class (1 Nov). Exported in each venue with a title, two authors, an abstract, sections, an
/// equation, a theorem and its proof, a table, a figure with alt text, an algorithm and a citation, then made
/// tagged and compiled with LuaLaTeX, not one publisher class compiled:
/// <list type="bullet">
/// <item>the algorithm block stopped every class (float.sty's algorithm float is unknown to LaTeX's tagging code);</item>
/// <item>elsarticle, llncs and SIAM define a proof or \qed the tagging code already has ("already defined"), and
/// acmart's proof left the tagging code's paragraphs unbalanced;</item>
/// <item>llncs's footnote text and headings were in a shape the tagging code can't read;</item>
/// <item>elsarticle's author command stopped the tagging code on the last page, and IEEEtran's bibliography left its
/// paragraphs unbalanced once the references printed;</item>
/// <item>once they compiled, veraPDF (PDF/UA-2) failed acmart's sigconf (its author boxes are paragraphs inside a
/// paragraph) and SIAM (its ntheorem theorems left a list item open around the rest of the paper);</item>
/// <item>revtex4-2 (APS, AIP) can't set a table at all once \DocumentMetadata is there, so it is refused with a
/// clear reason, never handed back untagged.</item>
/// </list>
/// Each format now compiles to a tagged PDF/UA-2 (a structure tree, the PDF/UA mark, maths as formulas, the table,
/// the figure with its alt text), or is refused. TaggedPdf's summary has the detail.
/// </summary>
public class TaggedPdfPublisherTests
{
    private const string Alt = "A grey test picture with the letter A";

    // Every venue, and Lilia's own article class beside them (an algorithm block stopped it too).
    public static TheoryData<string> Profiles() => new(PublisherProfiles.All.Select(p => p.Id).Append("article"));

    /// <summary>A full paper in the venue's class, exported as the accessible PDF's export writes it.</summary>
    private static string Export(PublisherProfile? p)
    {
        var doc = PublisherClassTests.Doc(p?.DocumentClass ?? "article", p?.ClassOptions, "Ada Lovelace\\\\Analytical Engines\\\\UK \\and Alan Turing", new[] { "LaTeX", "publishing" });
        doc.PublisherProfile = p?.Id;
        // A figure with its description, after the proof (an uploaded image has its extension; SIAM's class looks
        // for .eps first when there is none, which LuaLaTeX can't convert without shell escape).
        var figure = PublisherClassTests.B("figure", new { src = "example-image-a.png", alt = Alt, caption = "An example" });
        foreach (var b in doc.Blocks.Where(b => b.SortOrder >= 7)) b.SortOrder++;
        figure.SortOrder = 7; figure.Document = doc; figure.DocumentId = doc.Id;
        doc.Blocks.Add(figure);
        return new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions { Tagged = true });
    }

    [Trait("Category", "RequiresTeX")]
    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task Every_publisher_format_compiles_to_a_tagged_PDF_or_is_refused_with_a_reason(string id)
    {
        var p = PublisherProfiles.Find(id);
        var tex = Export(p);

        if (p?.DocumentClass == "revtex4-2")
        {
            TaggedPdf.WhyUnavailable(tex).Should().Contain("REVTeX");
            var refused = () => TaggedPdf.Apply(tex, "en", "Visual Blocks for Real LaTeX");
            refused.Should().Throw<TaggedPdfUnavailableException>().WithMessage("*isn't available yet*APS and AIP*");
            return;
        }

        TaggedPdf.WhyUnavailable(tex).Should().BeNull(id);
        var made = TaggedPdf.Apply(tex, "en", "Visual Blocks for Real LaTeX");
        TexSourceGuard.Violation(made.Latex).Should().BeNull();
        made.Latex.Should().Contain("alt={" + Alt + "}");

        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(made.Latex, TaggedPdf.Engine, timeout: 240);

        var raw = Encoding.Latin1.GetString(Inflated(pdf));
        raw.Should().Contain("/StructTreeRoot", id).And.Contain("pdfuaid").And.Contain("/Lang (en)")
            .And.Contain("/Formula").And.Contain("/Figure").And.Contain("/Table");
        raw.Should().Contain("/Alt <FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(Alt)) + ">", "the figure keeps its alt text");
    }

    [Theory]
    [InlineData("acmart", true)]
    [InlineData("elsarticle", true)]
    [InlineData("llncs", true)]
    [InlineData("siamart251216", true)]
    [InlineData("article", false)]
    public void Under_a_publisher_class_the_tagged_proof_outlives_the_classs_own(string cls, bool kept)
    {
        var tex = $"\\documentclass{{{cls}}}\n\\usepackage{{amsmath}}\n\\begin{{document}}\n\\begin{{proof}}P\\end{{proof}}\n\\end{{document}}\n";
        var made = TaggedPdf.Apply(tex, "en", "T").Latex;

        made.Contains(@"\NewEnvironmentCopy{LiliaTaggedProof}{proof}").Should().Be(kept);
        if (kept)
            made.IndexOf("LiliaTaggedProof", StringComparison.Ordinal).Should().BeLessThan(made.IndexOf(@"\documentclass", StringComparison.Ordinal),
                "it is set aside before the class defines its own");
    }

    [Fact]
    public void An_algorithm_is_a_kernel_float_the_tagging_code_knows()
    {
        const string tex = "\\documentclass{amsart}\n\\usepackage{algorithm}\n\\usepackage{algorithmic}\n\\begin{document}\n"
            + "\\begin{algorithm}[H]\n\\caption{Sum}\n\\begin{algorithmic}\n\\STATE $s$\n\\end{algorithmic}\n\\end{algorithm}\n\\end{document}\n";
        var made = TaggedPdf.Apply(tex, "en", "T").Latex;

        made.Should().Contain(@"\tagpdfsetup{float/new=algorithm}\renewenvironment{algorithm}{\@float{algorithm}}{\end@float}");
        made.Should().Contain(@"\begin{algorithm}[!htbp]").And.NotContain(@"\begin{algorithm}[H]");
        TaggedPdf.Apply(tex.Replace("\\begin{algorithm}[H]\n\\caption{Sum}\n\\begin{algorithmic}\n\\STATE $s$\n\\end{algorithmic}\n\\end{algorithm}\n", ""), "en", "T")
            .Latex.Should().NotContain("float/new=algorithm", "only when the document has one");
    }

    [Fact]
    public void Llncs_footnotes_and_headings_are_written_so_the_tagging_code_can_read_them()
    {
        var made = TaggedPdf.Apply("\\documentclass[runningheads]{llncs}\n\\begin{document}\nText.\n\\end{document}\n", "en", "T").Latex;

        made.Should().Contain(@"\long\def\@makefntext#1{\parindent\fnindent\leftskip\fnindent\noindent\hb@xt@\z@{\hss\hb@xt@1em");
        made.Should().Contain(@"\rightskip\z@ \@plus 8em\pretolerance10000 }}").And.NotContain(@"\rightskip=");
        TaggedPdf.Apply("\\documentclass{amsart}\n\\begin{document}\nText.\n\\end{document}\n", "en", "T").Latex.Should().NotContain("makefntext");
    }

    [Fact]
    public void SIAMs_theorems_are_LaTeXs_own_declared_right_after_the_class()
    {
        var made = TaggedPdf.Apply("\\documentclass[review]{siamart251216}\n\\begin{document}\nText.\n\\end{document}\n", "en", "T").Latex;

        var keep = made.IndexOf(@"\NewCommandCopy\LiliaTaggedNewtheorem\newtheorem", StringComparison.Ordinal);
        var cls = made.IndexOf(@"\documentclass[review]{siamart251216}", StringComparison.Ordinal);
        var again = made.IndexOf(@"\let\newtheorem\LiliaTaggedNewtheorem", StringComparison.Ordinal);
        keep.Should().BePositive().And.BeLessThan(cls, "LaTeX's \\newtheorem is kept before the class loads ntheorem");
        again.Should().BeGreaterThan(cls);
        made.Should().Contain(@"\newtheorem{lemma}[theorem]{Lemma}");
        TaggedPdf.Apply("\\documentclass{amsart}\n\\begin{document}\nText.\n\\end{document}\n", "en", "T").Latex.Should().NotContain("LiliaTaggedNewtheorem");
    }

    [Fact]
    public void Acmart_authors_lines_are_part_of_their_lines_paragraph()
    {
        var made = TaggedPdf.Apply("\\documentclass[sigconf]{acmart}\n\\begin{document}\nText.\n\\end{document}\n", "en", "T").Latex;

        made.Should().Contain(@"\global\setbox\author@bx=\vtop{\tagpdfsetup{para/tagging=false}");
    }

    [Theory]
    [InlineData(@"\documentclass[aps,reprint]{revtex4-2}")]
    [InlineData(@"\documentclass[aip,preprint]{revtex4-1}")]
    public void REVTeX_is_refused_with_a_reason_never_handed_back_untagged(string documentClass)
    {
        var tex = documentClass + "\n\\begin{document}\nText.\n\\end{document}\n";

        TaggedPdf.WhyUnavailable(tex).Should().StartWith("An accessible PDF isn't available yet for the APS and AIP format (REVTeX)");
        FluentActions.Invoking(() => TaggedPdf.Apply(tex, "en", "T")).Should().Throw<TaggedPdfUnavailableException>();
        TaggedPdf.WhyUnavailable("% \\documentclass{revtex4-2}\n\\documentclass{article}\n\\begin{document}\n\\end{document}\n")
            .Should().BeNull("a class in a comment is not the document's");
    }

    // The PDF with its compressed streams inflated beside it, to read its tags.
    private static byte[] Inflated(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var output = new MemoryStream();
        output.Write(pdf);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "stream\r?\n"))
        {
            try
            {
                using var z = new System.IO.Compression.ZLibStream(new MemoryStream(pdf, m.Index + m.Length, pdf.Length - m.Index - m.Length), System.IO.Compression.CompressionMode.Decompress);
                var buffer = new byte[1 << 16];
                int n;
                while ((n = z.Read(buffer)) > 0) output.Write(buffer, 0, n);
            }
            catch (InvalidDataException) { }
        }
        return output.ToArray();
    }
}
