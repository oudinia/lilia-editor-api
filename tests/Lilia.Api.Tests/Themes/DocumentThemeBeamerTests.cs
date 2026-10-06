using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Lilia.Engines.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// Cerulean and Index as beamer themes (Olivia, 6 Oct 2026, REPLY-2026-10-06c), without a compile:
/// a deck's Look becomes Classic · Cerulean · Index · Exposition, each loaded with one
/// <c>\usetheme</c> line (<c>[printsafe]</c> on white paper or a print-safe export), Index's pins
/// written by section number, and the .sty staged and zipped only when used. Their document
/// versions are unchanged. The compiles are in <see cref="DocumentBeamerThemeCompileTests"/>.
/// </summary>
[Collection(ThemeAvailabilityCollection.Name)]
public class DocumentThemeBeamerTests : IDisposable
{
    public DocumentThemeBeamerTests() => ThemeAvailability.OverrideForTests(_ => true);

    public void Dispose() => ThemeAvailability.OverrideForTests(null);

    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Block Heading(string text, bool numbered = true) => B("heading", new { text, level = 1, numbered });
    private static Block Slide(string title) => B("slide", new { title, content = "Body." });
    private static Block Appendix() => B("backMatter", new { subType = "appendix" });

    private static Document Doc(string? look, string? cls = "beamer") => new()
    {
        Id = Guid.NewGuid(), Title = "Neutrino oscillations", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls, Look = look,
    };

    private static string Look(string theme, string paper = "theme", IDictionary<string, int>? pins = null) =>
        JsonSerializer.Serialize(new { theme, paper, pins = pins ?? new Dictionary<string, int>() });

    private static string Export(Document doc, params Block[] blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks.ToList(), [], new LaTeXExportOptions());

    private static UpdateDocumentDto Put(string lookJson) => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, null, null, Look: JsonDocument.Parse(lookJson).RootElement);

    // ── the catalog and lookThemes ───────────────────────────────────────

    [Fact]
    public void A_deck_takes_classic_cerulean_index_and_exposition_in_catalog_order()
    {
        ThemeLock.ThemesFor("beamer").Should().Equal("classic", "cerulean", "index", "exposition");
        ThemeLock.ThemesFor("article").Should().Equal("classic", "cerulean", "index", "carnet", "gazette");
        ThemeLock.ThemesFor("beamerposter").Should().Equal("classic");
    }

    [Fact]
    public void Cerulean_and_index_keep_classes_null_and_gain_a_beamer_version()
    {
        foreach (var id in new[] { "cerulean", "index" })
        {
            var t = ThemeCatalog.Find(id)!;
            t.Classes.Should().BeNull("they serve every class");
            t.IsBeamerTheme.Should().BeFalse();
            t.Beamer!.StyFileName.Should().Be(id == "cerulean" ? "beamerthemeLiliaCerulean.sty" : "beamerthemeLiliaIndex.sty");
            t.Beamer.TexFiles.Should().Equal("beamer.cls", "montserrat.sty", "sourceserifpro.sty", "tikz.sty");
            // The document version is untouched.
            t.TexFiles.Should().Contain("titlesec.sty").And.NotContain("beamer.cls");
        }
        ThemeCatalog.Find("exposition")!.Beamer!.Theme.Should().Be("LiliaExposition");
        ThemeCatalog.Find("carnet")!.Beamer.Should().BeNull();
        ThemeCatalog.Find("gazette")!.Beamer.Should().BeNull();
        ThemeCatalog.BeamerThemes.Select(t => t.Id).Should().Equal("cerulean", "index", "exposition");

        var dtos = ThemeCatalog.All.Select(ThemesController.ToDto).ToList();
        dtos.Single(d => d.Id == "index").Classes.Should().BeNull();
        dtos.Single(d => d.Id == "exposition").Classes.Should().Equal("beamer");
    }

    [Fact]
    public void The_refusal_names_the_four_and_exposition_still_needs_beamer()
    {
        ThemeLock.BeamerDeckThemes.Should().Be("Beamer decks take Classic, Cerulean, Index or Exposition.");
        ThemeLock.WhyNot("beamer", "carnet").Should().Be(ThemeLock.BeamerDeckThemes);
        ThemeLock.WhyNot("beamer", "gazette").Should().Be(ThemeLock.BeamerDeckThemes);
        ThemeLock.WhyNot("beamer", "cerulean").Should().BeNull();
        ThemeLock.WhyNot("beamer", "index").Should().BeNull();
        ThemeLock.WhyNot("article", "exposition").Should().Be("Exposition is a beamer theme: switch the class to beamer to use it.");
        ThemeLock.WhyNot("article", "index").Should().BeNull();
    }

    [Theory]
    [InlineData("cerulean")]
    [InlineData("index")]
    public void Put_stores_cerulean_and_index_on_a_deck(string theme)
    {
        DocumentService.ResolveLookUpdate(Put($$"""{"theme":"{{theme}}"}"""), "beamer")
            .Should().Be((true, "{\"theme\":\"" + theme + "\",\"paper\":\"theme\",\"pins\":{}}"));
    }

    [Fact]
    public void Put_refuses_carnet_on_a_deck_with_the_four_named()
    {
        var act = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"carnet"}"""), "beamer");
        act.Should().Throw<DocumentLookException>().WithMessage("Beamer decks take Classic, Cerulean, Index or Exposition.");
    }

    [Fact]
    public void Put_refuses_a_theme_whose_beamer_version_this_server_cannot_print()
    {
        ThemeAvailability.OverrideForTests(id => id != "index");
        var act = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"index"}"""), "beamer");
        act.Should().Throw<DocumentLookException>().WithMessage("The Index theme's fonts are not installed*");
    }

    // ── the managed line ─────────────────────────────────────────────────

    [Theory]
    [InlineData("cerulean", "LiliaCerulean")]
    [InlineData("index", "LiliaIndex")]
    [InlineData("exposition", "LiliaExposition")]
    public void A_deck_writes_one_usetheme_line_and_printsafe_on_white_paper(string theme, string beamerTheme)
    {
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Look(theme)), [])
            .Should().Contain($@"\usetheme{{{beamerTheme}}}").And.NotContain("printsafe").And.NotContain("lilia-theme");
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Look(theme, "white")), [])
            .Should().Contain($@"\usetheme[printsafe]{{{beamerTheme}}}");
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Look(theme)), [], DocumentLook.Parse(Look(theme)).With(null, printSafe: true))
            .Should().Contain($@"\usetheme[printsafe]{{{beamerTheme}}}");
    }

    [Fact]
    public void The_line_comes_just_before_the_custom_preamble_and_passes_the_guard()
    {
        var doc = Doc(Look("index"));
        doc.CustomPreamble = @"\setbeamercolor{frametitle}{fg=red}";
        var tex = Export(doc, Heading("Mixing"), Slide("One"));
        var line = tex.IndexOf(@"\usetheme{LiliaIndex}", StringComparison.Ordinal);
        line.Should().BePositive();
        tex.IndexOf(@"\setbeamercolor{frametitle}{fg=red}", StringComparison.Ordinal).Should().BeGreaterThan(line);
        tex.IndexOf(@"\usetheme{LiliaIndex}", line + 1, StringComparison.Ordinal).Should().Be(-1, "one line");
        tex.Should().NotContain("{lilia-theme}").And.Contain(@"\section{Mixing}");
        TexSourceGuard.Violation(tex).Should().BeNull();
    }

    [Fact]
    public void An_export_override_picks_a_beamer_version_and_a_document_only_theme_prints_classic()
    {
        LaTeXPreambleBuilder.BuildThemeLine(Doc(null), [], DocumentLook.Parse(null).With("cerulean", printSafe: true))
            .Should().Contain(@"\usetheme[printsafe]{LiliaCerulean}");
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Look("exposition")), [], DocumentLook.Parse(Look("exposition")).With("index", false))
            .Should().Contain(@"\usetheme{LiliaIndex}").And.NotContain("Exposition");
        // Carnet has no beamer version: on a deck it is Classic, as PUT and the export refuse it.
        LaTeXPreambleBuilder.BuildThemeLine(Doc(null), [], DocumentLook.Parse(null).With("carnet", false)).Should().BeEmpty();
    }

    [Fact]
    public void Index_pins_on_a_deck_are_written_by_section_number_and_stop_at_the_appendix()
    {
        Block one = Heading("Mixing"), aside = Heading("Aside", numbered: false), two = Heading("Data"),
              three = Heading("Results"), proofs = Heading("Proofs");
        var pins = new Dictionary<string, int>
        {
            [two.Id.ToString()] = 7, [three.Id.ToString()] = 0, [aside.Id.ToString()] = 3, [proofs.Id.ToString()] = 5,
        };
        var line = LaTeXPreambleBuilder.BuildThemeLine(Doc(Look("index", pins: pins)),
            [one, Slide("a"), aside, two, Slide("b"), three, Appendix(), proofs]);
        line.Should().Contain("\\usetheme{LiliaIndex}\n\\liliaPinColour{2}{7}\n\\liliaPinColour{3}{0}\n");
        line.Should().NotContain(@"\liliaPinColour{1}").And.NotContain("{5}", "an appendix section takes no pin")
            .And.NotContain("{3}{3}", "a starred section has no number to pin");

        // Cerulean carries no pins, even when the stored look still has them.
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Look("cerulean", pins: pins)), [one, two]).Should().NotContain("liliaPinColour");
    }

    [Fact]
    public void A_compile_refuses_index_this_server_cannot_print_and_a_download_writes_it()
    {
        ThemeAvailability.OverrideForTests(id => id != "index");
        var doc = Doc(Look("index"));
        var compile = () => LaTeXPreambleBuilder.BuildThemeLine(doc, []);
        compile.Should().Throw<ThemeUnavailableException>().WithMessage("*Index*");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [], use: LaTeXPreambleBuilder.ThemeUse.Export).Should().Contain(@"\usetheme{LiliaIndex}");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [], use: LaTeXPreambleBuilder.ThemeUse.Validation).Should().BeEmpty();
    }

    [Fact]
    public void The_preview_and_validation_preambles_load_it_too()
    {
        var doc = Doc(Look("cerulean"));
        doc.Blocks = new List<Block> { Heading("Mixing"), Slide("One") };
        doc.BibliographyEntries = new List<BibliographyEntry>();
        RenderService.BuildPreambleForValidation(doc, LatexEngine.Pdflatex).Should().Contain(@"\usetheme{LiliaCerulean}");
        var preview = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        preview.Should().Contain(@"\usetheme{LiliaCerulean}").And.Contain(@"\begin{frame}{One}");
        TexSourceGuard.Violation(preview).Should().BeNull();
    }

    [Fact]
    public void A_class_switch_keeps_index_which_prints_as_a_document_theme_on_article()
    {
        var doc = Doc(Look("index"));
        Export(doc, Heading("Mixing"), Slide("One")).Should().Contain(@"\usetheme{LiliaIndex}");
        DocumentLook.IsThemed(doc.Look, "beamer").Should().BeTrue();

        doc.LatexDocumentClass = "article";
        var article = Export(doc, Heading("Mixing"));
        article.Should().Contain(@"\usepackage[theme=index, paper=theme]{lilia-theme}").And.NotContain("LiliaIndex");
    }

    // ── where a block sits: Index's colours on a deck ────────────────────

    [Fact]
    public void Section_places_on_an_index_deck_follow_the_theme()
    {
        Block one = Heading("Mixing"), s1 = Slide("a"), aside = Heading("Aside", numbered: false), s2 = Slide("b"),
              two = Heading("Data"), s3 = Slide("c"), app = Appendix(), proofs = Heading("Proofs"), s4 = Slide("d");
        var look = Look("index", pins: new Dictionary<string, int> { [two.Id.ToString()] = 7 });
        var places = ThemeSections.Places([one, s1, aside, s2, two, s3, app, proofs, s4], "beamer", look);
        places[s1.Id].Should().Be(new SectionPlace("1", "#4A5FA3"));
        places[s2.Id].Should().Be(SectionPlace.None, "a starred section is neutral");
        places[s3.Id].Should().Be(new SectionPlace("2", "#2E6E9E"), "pinned");
        places[s4.Id].Should().Be(new SectionPlace("A", "#4A5FA3"), "the appendix restarts the sequence");
    }

    // ── the files ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cerulean", "beamerthemeLiliaCerulean.sty")]
    [InlineData("index", "beamerthemeLiliaIndex.sty")]
    public void The_sty_is_staged_and_zipped_only_when_the_source_loads_it(string theme, string file)
    {
        var themed = Export(Doc(Look(theme, "white")), Heading("Mixing"), Slide("One"));
        ThemeCatalog.FilesUsedBy(themed).Select(f => f.FileName).Should().Equal(file);
        ThemeCatalog.FilesUsedBy(Export(Doc(Look("exposition")), Slide("One"))).Select(f => f.FileName)
            .Should().Equal("beamerthemeLiliaExposition.sty");
        ThemeCatalog.FilesUsedBy(Export(Doc(null), Slide("One"))).Should().BeEmpty();

        var dir = Directory.CreateTempSubdirectory("lilia-beamer-stage").FullName;
        try
        {
            ThemeCatalog.StageIfUsed(themed, dir);
            File.ReadAllText(Path.Combine(dir, file)).Should().Be(ThemeCatalog.BeamerStySource(ThemeCatalog.Find(theme)!.Beamer!));
            Directory.GetFiles(dir).Should().ContainSingle();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_sty_files_are_olivias_and_pass_the_source_guard()
    {
        var cerulean = ThemeCatalog.BeamerStySource(ThemeCatalog.Find("cerulean")!.Beamer!);
        cerulean.Should().Contain(@"\ProvidesPackage{beamerthemeLiliaCerulean}").And.Contain(@"\DeclareOptionBeamer{printsafe}");
        foreach (var hex in new[] { "08597F", "0A76A4", "22262B", "C4314B", "107C41" })
            cerulean.Should().Contain($"{{HTML}}{{{hex}}}");

        var index = ThemeCatalog.BeamerStySource(ThemeCatalog.Find("index")!.Beamer!);
        index.Should().Contain(@"\ProvidesPackage{beamerthemeLiliaIndex}").And.Contain(@"\newcommand\liliaPinColour[2]");
        foreach (var hex in ThemeCatalog.Sequence.Select(h => h[1..]).Concat(["2F2E2C", "3A3836"]))
            index.Should().Contain($"{{HTML}}{{{hex}}}");

        foreach (var sty in new[] { cerulean, index })
        {
            TexSourceGuard.Violation(sty).Should().BeNull();
            sty.Should().Contain(@"\RequirePackage[type1]{sourceserifpro}").And.Contain("{montserrat}");
            // Fonts only through the TeX font packages; the theme never sets \tiny.
            sty.Should().NotContain("{fontspec}").And.NotContain(".otf").And.NotContain(".ttf").And.NotContain(@"\tiny");
        }
        // Exposition is unchanged.
        ThemeCatalog.ExpositionStySource.Should().Contain(@"\ProvidesPackage{beamerthemeLiliaExposition}");
    }
}
