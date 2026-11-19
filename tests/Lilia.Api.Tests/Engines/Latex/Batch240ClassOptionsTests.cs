using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #240 (Overleaf round 1, 19 Nov 2026): an import rewrote \documentclass's options. amsart's [11pt,reqno]
/// came out with none (Overleaf: 32 pp against arXiv's 37, equation numbers on the left), olplainarticle's [fleqn,10pt] as
/// [10pt,a4paper] (displays centred), REVTeX's [reprint,amsmath,amssymb,aps] as [aps,reprint], and a plain [10pt]{article}
/// gained a4paper. The page setup kept the options it models (size, paper, columns) and only noted the rest as "not applied";
/// a publisher class's line takes only the stored options, so its size went too; and no paper option became the document's
/// A4 instead of LaTeX's letter. The source's options are now kept as written.
/// </summary>
public class Batch240ClassOptionsTests
{
    /// <summary>The \documentclass line an import of <paramref name="tex"/> gets: parser, page setup, venue, then the builder.</summary>
    private static async Task<string> ImportedClassLine(string tex)
    {
        var meta = (await new LatexParser().ParseTextAsync(tex)).Metadata;
        var setup = LatexPageSetupExtractor.Extract(tex, meta);
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", FontFamily = "serif",
            LatexDocumentClass = setup.DocumentClass ?? meta.DocumentClass,
            FontSize = setup.Settings.FontSize ?? 12,
            PaperSize = setup.Settings.PaperSize ?? "a4",
            LatexDocumentClassOptions = setup.ClassOptions,
        };
        // As ImportReviewService does for a publisher class: the venue's options, then the source's merged in.
        if (PublisherProfiles.FromClass(meta.DocumentClass, meta.RawClassOptions) is { } venue)
        {
            DocumentService.ApplyPublisherProfile(doc, venue);
            doc.LatexDocumentClassOptions = PublisherProfiles.WithSourceOptions(doc.LatexDocumentClass, doc.LatexDocumentClassOptions, meta.RawClassOptions);
        }
        return LaTeXPreambleBuilder.BuildClassDirective(doc).Trim();
    }

    private static string Source(string classLine) => classLine + "\n\\begin{document}\nText.\n\\end{document}\n";

    [Fact]
    public async Task Amsart_keeps_its_size_and_equation_numbers_on_the_right()
    {
        var line = await ImportedClassLine(Source(@"\documentclass[11pt,reqno]{amsart}"));
        line.Should().Be(@"\documentclass[11pt,reqno]{amsart}");
    }

    [Fact]
    public async Task Revtex_keeps_every_option_the_source_wrote()
    {
        var line = await ImportedClassLine(Source(@"\documentclass[reprint,amsmath,amssymb,aps]{revtex4-2}"));
        line.Should().Be(@"\documentclass[reprint,amsmath,amssymb,aps]{revtex4-2}");
    }

    [Fact]
    public async Task Fleqn_is_kept_and_no_A4_is_added_to_a_class_that_falls_back_to_article()
    {
        var line = await ImportedClassLine(Source(@"\documentclass[fleqn,10pt]{olplainarticle}"));
        line.Should().Contain("fleqn").And.Contain("10pt").And.NotContain("a4paper");
    }

    [Fact]
    public async Task A_source_with_no_paper_option_keeps_LaTeXs_letter_paper()
    {
        var line = await ImportedClassLine(Source(@"\documentclass[10pt]{article}"));
        line.Should().NotContain("a4paper").And.Contain("10pt");
        line.Should().Contain("letterpaper", "LaTeX's default, the size the source was laid out on");
    }

    [Fact]
    public async Task A_paper_option_the_source_wrote_is_kept()
    {
        var line = await ImportedClassLine(Source(@"\documentclass[a4paper,12pt]{article}"));
        line.Should().Contain("a4paper").And.Contain("12pt").And.NotContain("letterpaper");
    }

    [Fact]
    public async Task Elsarticle_keeps_authoryear()
    {
        var line = await ImportedClassLine(Source(@"\documentclass[preprint,authoryear,12pt]{elsarticle}"));
        line.Should().Contain("authoryear").And.Contain("preprint").And.Contain("12pt");
    }

    [Fact]
    public async Task A_KOMA_class_keeps_its_own_A4_default()
    {
        var tex = Source(@"\documentclass[11pt]{scrartcl}");
        var meta = (await new LatexParser().ParseTextAsync(tex)).Metadata;
        LatexPageSetupExtractor.Extract(tex, meta).Settings.PaperSize.Should().BeNull("scrartcl defaults to A4, not letter");
    }

    [Fact]
    public void The_kept_options_go_first_and_each_appears_once()
    {
        LatexPageSetupExtractor.MergeOptions("reqno,fleqn", "twoside,REQNO").Should().Be("reqno,fleqn,twoside");
        LatexPageSetupExtractor.MergeOptions(null, null).Should().BeNull();
    }
}
