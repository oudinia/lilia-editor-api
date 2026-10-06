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
/// Document themes, phase 3 (Olivia, 6 Oct 2026): Exposition as a real beamer theme, without a
/// compile. Which themes a class may use (<c>lookThemes</c>), the PUT refusals, the one
/// <c>\usetheme</c> line, the class switch, and the .sty staged and zipped only when used.
/// The compiles are in <see cref="DocumentExpositionCompileTests"/>.
/// </summary>
[Collection(ThemeAvailabilityCollection.Name)]
public class DocumentThemePhase3Tests : IDisposable
{
    public DocumentThemePhase3Tests() => ThemeAvailability.OverrideForTests(_ => true);

    public void Dispose() => ThemeAvailability.OverrideForTests(null);

    private static int _order;

    private static Block Slide(string title, string content) => new()
    {
        Id = Guid.NewGuid(),
        Type = "slide",
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { title, content })),
    };

    private static Block Table() => new()
    {
        Id = Guid.NewGuid(),
        Type = "table",
        SortOrder = _order++,
        Content = JsonDocument.Parse("""{"caption":"R","headers":["Model","Acc"],"rows":[["A","1"]]}"""),
    };

    private static Document Doc(string? look, string? cls = "beamer", string? preamble = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Neutrino oscillations", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls, Look = look, CustomPreamble = preamble,
    };

    private const string Exposition = """{"theme":"exposition","paper":"theme","pins":{}}""";

    private static string Export(Document doc, params Block[] blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks.ToList(), [], new LaTeXExportOptions());

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static UpdateDocumentDto Put(string lookJson) => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, null, null, Look: Json(lookJson));

    // ── lookThemes: which themes a class may use ─────────────────────────

    [Theory]
    [InlineData("beamer", new[] { "classic", "cerulean", "index", "exposition" })]
    [InlineData("Beamer", new[] { "classic", "cerulean", "index", "exposition" })]
    [InlineData("article", new[] { "classic", "cerulean", "index", "carnet", "gazette" })]
    [InlineData("report", new[] { "classic", "cerulean", "index", "carnet", "gazette" })]
    [InlineData(null, new[] { "classic", "cerulean", "index", "carnet", "gazette" })]
    [InlineData("IEEEtran", new[] { "classic" })]
    [InlineData("moderncv", new[] { "classic" })]
    [InlineData("beamerposter", new[] { "classic" })]
    public void Look_themes_are_the_classs_themes_in_catalog_order(string? cls, string[] themes) =>
        ThemeLock.ThemesFor(cls).Should().Equal(themes);

    [Fact]
    public void Look_themes_do_not_depend_on_what_this_server_has()
    {
        ThemeAvailability.OverrideForTests(_ => false);
        ThemeLock.ThemesFor("beamer").Should().Equal("classic", "cerulean", "index", "exposition");
    }

    [Fact]
    public void The_beamer_lock_is_lifted()
    {
        ThemeLock.Reason("beamer").Should().BeNull();
        ThemeLock.Reason("IEEEtran").Should().NotBeNull("publisher classes stay locked");
    }

    [Fact]
    public void The_document_dto_carries_look_themes_and_no_lock_for_beamer()
    {
        var dto = new DocumentDto(
            Id: Guid.NewGuid(), Title: "Deck", OwnerId: "u", TeamId: null, Language: "en", PaperSize: "a4",
            FontFamily: "serif", FontSize: 11, Columns: 1, ColumnSeparator: "none", ColumnGap: 1.0, IsPublic: false,
            ShareLink: null, ShareSlug: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow, LastOpenedAt: null,
            MarginTop: null, MarginBottom: null, MarginLeft: null, MarginRight: null, HeaderText: null, FooterText: null,
            LineSpacing: null, ParagraphIndent: null, PageNumbering: null, LatexDocumentClass: "beamer",
            LatexDocumentClassOptions: null, LatexPackages: null, BalancedColumns: false, Blocks: [], Bibliography: [], Labels: [],
            LookLocked: ThemeLock.Reason("beamer"), LookThemes: ThemeLock.ThemesFor("beamer"));
        var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement;
        json.GetProperty("lookLocked").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("lookThemes").EnumerateArray().Select(e => e.GetString()).Should().Equal("classic", "cerulean", "index", "exposition");
    }

    [Fact]
    public void The_theme_descriptor_says_which_classes_it_is_for()
    {
        var dtos = ThemeCatalog.All.Select(ThemesController.ToDto).ToList();
        var json = JsonDocument.Parse(JsonSerializer.Serialize(dtos, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement;
        foreach (var t in json.EnumerateArray())
        {
            var classes = t.GetProperty("classes");
            if (t.GetProperty("id").GetString() == "exposition")
                classes.EnumerateArray().Select(e => e.GetString()).Should().Equal("beamer");
            else
                classes.ValueKind.Should().Be(JsonValueKind.Null, "written as null, not left out");
        }
        dtos.Single(d => d.Id == "exposition").Status.Should().Be("ready");
    }

    // ── PUT: what is refused, and with what sentence ─────────────────────

    [Fact]
    public void Put_stores_exposition_and_classic_on_beamer()
    {
        DocumentService.ResolveLookUpdate(Put("""{"theme":"exposition"}"""), "beamer")
            .Should().Be((true, Exposition));
        DocumentService.ResolveLookUpdate(Put("""{"theme":"exposition","paper":"white"}"""), "beamer")
            .Stored.Should().Contain("\"paper\":\"white\"");
        DocumentService.ResolveLookUpdate(Put("""{"theme":"classic"}"""), "beamer")
            .Should().Be((true, (string?)null));
    }

    [Theory]
    [InlineData("article")]
    [InlineData("report")]
    [InlineData("book")]
    [InlineData(null)]
    public void Put_refuses_exposition_on_a_class_that_is_not_beamer(string? cls)
    {
        var act = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"exposition"}"""), cls);
        act.Should().Throw<DocumentLookException>()
            .WithMessage("Exposition is a beamer theme: switch the class to beamer to use it.");
    }

    [Theory]
    [InlineData("carnet")]
    [InlineData("gazette")]
    public void Put_refuses_a_theme_without_a_beamer_version_on_beamer(string theme)
    {
        var act = () => DocumentService.ResolveLookUpdate(Put($$"""{"theme":"{{theme}}"}"""), "beamer");
        act.Should().Throw<DocumentLookException>().WithMessage("Beamer decks take Classic, Cerulean, Index or Exposition.");
    }

    [Fact]
    public void Put_refuses_exposition_on_a_publisher_class_with_the_lock_reason()
    {
        var act = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"exposition"}"""), "IEEEtran");
        act.Should().Throw<DocumentLookException>().WithMessage("IEEEtran sets its own look, so themes are off for this document.");
    }

    [Fact]
    public void Put_refuses_exposition_when_this_server_lacks_its_fonts()
    {
        ThemeAvailability.OverrideForTests(id => id != "exposition");
        var act = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"exposition"}"""), "beamer");
        act.Should().Throw<DocumentLookException>().WithMessage("The Exposition theme's fonts are not installed*");
    }

    [Fact]
    public void Put_validates_against_the_class_the_same_put_sets()
    {
        // The service passes dto.DocumentClass ?? dto.LatexDocumentClass ?? the stored class, so a
        // PUT that switches to beamer and picks Exposition in one go is accepted.
        var dto = Put("""{"theme":"exposition"}""") with { DocumentClass = "beamer" };
        DocumentService.ResolveLookUpdate(dto, dto.DocumentClass).Apply.Should().BeTrue();
    }

    [Fact]
    public void Beamer_takes_table_settings_without_a_refusal_and_ignores_them()
    {
        // Look → Tables does not apply to beamer: stored (switching to article brings them back),
        // never written into the deck.
        var stored = DocumentService.ResolveLookUpdate(Put("""{"theme":"classic","tables":{"style":"banded"}}"""), "beamer").Stored;
        stored.Should().Contain("banded");
        Export(Doc(stored), Slide("One", "Body"), Table()).Should().NotContain("lilia-theme").And.NotContain("usetheme{Lilia");
        Export(Doc("""{"theme":"exposition","tables":{"style":"banded","density":"compact"}}"""), Slide("One", "Body"))
            .Should().NotContain("lilia-theme").And.NotContain("tables=").And.Contain(@"\usetheme{LiliaExposition}");
    }

    // ── the managed line ─────────────────────────────────────────────────

    [Fact]
    public void Beamer_with_exposition_writes_one_usetheme_line_just_before_the_custom_preamble()
    {
        var tex = Export(Doc(Exposition, preamble: @"\setbeamercolor{title}{fg=red}"), Slide("One", "Quartz zebra"));

        var line = tex.IndexOf(@"\usetheme{LiliaExposition}", StringComparison.Ordinal);
        var custom = tex.IndexOf(@"\setbeamercolor{title}{fg=red}", StringComparison.Ordinal);
        line.Should().BePositive();
        custom.Should().BeGreaterThan(line, "the author's preamble wins, so it comes after the theme");
        tex.IndexOf(@"\usetheme{LiliaExposition}", line + 1, StringComparison.Ordinal).Should().Be(-1, "one line");
        tex.Should().NotContain("{lilia-theme}");
        TexSourceGuard.Violation(tex).Should().BeNull("the guard must accept the generated deck");
    }

    [Fact]
    public void Classic_on_beamer_writes_nothing()
    {
        var tex = Export(Doc(null), Slide("One", "Body"));
        tex.Should().NotContain("LiliaExposition").And.NotContain("{lilia-theme}");
        LaTeXPreambleBuilder.BuildThemeLine(Doc(null), []).Should().BeEmpty();
        LaTeXPreambleBuilder.BuildThemeLine(Doc("""{"theme":"classic","tables":{"style":"header"}}"""), []).Should().BeEmpty();
    }

    [Fact]
    public void White_paper_and_a_print_safe_export_write_the_printsafe_option()
    {
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Exposition), [])
            .Should().Contain(@"\usetheme{LiliaExposition}").And.NotContain("printsafe");
        LaTeXPreambleBuilder.BuildThemeLine(Doc("""{"theme":"exposition","paper":"white"}"""), [])
            .Should().Contain(@"\usetheme[printsafe]{LiliaExposition}");

        var printSafe = DocumentLook.Parse(Exposition).With(null, printSafe: true);
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Exposition), [], printSafe)
            .Should().Contain(@"\usetheme[printsafe]{LiliaExposition}");
    }

    [Fact]
    public void An_export_override_picks_exposition_for_a_classic_deck()
    {
        var look = DocumentLook.Parse(null).With("exposition", printSafe: false);
        LaTeXPreambleBuilder.BuildThemeLine(Doc(null), [], look).Should().Contain(@"\usetheme{LiliaExposition}");
        // ... and Classic for an Exposition deck: nothing.
        LaTeXPreambleBuilder.BuildThemeLine(Doc(Exposition), [], DocumentLook.Parse(Exposition).With("classic", false))
            .Should().BeEmpty();
    }

    [Fact]
    public void A_compile_refuses_exposition_this_server_cannot_print_and_a_download_writes_it()
    {
        ThemeAvailability.OverrideForTests(id => id != "exposition");
        var doc = Doc(Exposition);
        var compile = () => LaTeXPreambleBuilder.BuildThemeLine(doc, []);
        compile.Should().Throw<ThemeUnavailableException>().WithMessage("*Exposition*");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [], use: LaTeXPreambleBuilder.ThemeUse.Export)
            .Should().Contain(@"\usetheme{LiliaExposition}");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [], use: LaTeXPreambleBuilder.ThemeUse.Validation).Should().BeEmpty();
    }

    [Fact]
    public void The_validation_and_preview_preambles_load_it_too()
    {
        var doc = Doc(Exposition);
        doc.Blocks = new List<Block> { Slide("One", "Body") };
        RenderService.BuildPreambleForValidation(doc, LatexEngine.Pdflatex).Should().Contain(@"\usetheme{LiliaExposition}");

        doc.BibliographyEntries = new List<BibliographyEntry>();
        var preview = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        preview.Should().Contain(@"\usetheme{LiliaExposition}").And.Contain(@"\begin{frame}{One}");
        TexSourceGuard.Violation(preview).Should().BeNull();
    }

    // ── switching the class ──────────────────────────────────────────────

    [Fact]
    public void A_class_switch_keeps_the_stored_look_and_prints_classic_until_switched_back()
    {
        // Decision: a class change is never refused for the look. The stored look stays; a theme
        // the new class cannot use prints as Classic; switching back restores it.
        var deck = Doc(Exposition);
        Export(deck, Slide("One", "Body")).Should().Contain(@"\usetheme{LiliaExposition}");

        deck.LatexDocumentClass = "article";
        var article = Export(deck, Slide("One", "Body"));
        article.Should().NotContain("LiliaExposition").And.NotContain("{lilia-theme}");
        deck.Look.Should().Be(Exposition, "the stored look is kept");
        DocumentLook.IsThemed(deck.Look, deck.LatexDocumentClass).Should().BeFalse();

        deck.LatexDocumentClass = "beamer";
        Export(deck, Slide("One", "Body")).Should().Contain(@"\usetheme{LiliaExposition}");
        DocumentLook.IsThemed(deck.Look, deck.LatexDocumentClass).Should().BeTrue();
    }

    [Fact]
    public void A_document_theme_left_on_a_deck_prints_as_classic_and_comes_back_on_article()
    {
        var doc = Doc("""{"theme":"carnet","paper":"theme","pins":{},"tables":{"style":"banded"}}""");
        Export(doc, Slide("One", "Body")).Should().NotContain("{lilia-theme}").And.NotContain("LiliaExposition");
        DocumentLook.IsThemed(doc.Look, "beamer").Should().BeFalse();

        doc.LatexDocumentClass = "article";
        Export(doc, Table()).Should().Contain(@"\usepackage[theme=carnet, paper=theme, tables=banded]{lilia-theme}");
    }

    [Fact]
    public void Exposition_left_on_an_article_keeps_its_table_settings_under_classic()
    {
        var look = DocumentLook.Parse("""{"theme":"exposition","tables":{"caption":"below"}}""").ForClass("article");
        look.IsClassic.Should().BeTrue();
        look.TableOptions().Should().Equal("captions=below");
    }

    [Fact]
    public void Routing_counts_exposition_on_beamer_as_a_theme_and_a_stale_theme_as_none()
    {
        PageSetupRouting.WhyLatex(Doc(Exposition)).Should().Contain("theme").And.Contain("document class beamer");
        PageSetupRouting.WhyLatex(Doc(null)).Should().NotContain("theme");
        PageSetupRouting.WhyLatex(Doc(Exposition, cls: null)).Should().BeNull("exposition on article prints as Classic");
    }

    // ── the file: staged into a compile and the .zip only when used ──────

    [Fact]
    public void The_exposition_sty_is_staged_only_when_the_source_loads_it()
    {
        var themed = Export(Doc(Exposition), Slide("One", "Body"));
        var plain = Export(Doc(null), Slide("One", "Body"));
        ThemeCatalog.FilesUsedBy(themed).Select(f => f.FileName).Should().Equal("beamerthemeLiliaExposition.sty");
        ThemeCatalog.FilesUsedBy(plain).Should().BeEmpty();
        ThemeCatalog.FilesUsedBy(@"\usetheme[printsafe]{LiliaExposition}").Should().ContainSingle();

        var dir = Directory.CreateTempSubdirectory("lilia-expo-stage").FullName;
        try
        {
            ThemeCatalog.StageIfUsed(plain, dir);
            Directory.GetFiles(dir).Should().BeEmpty();
            ThemeCatalog.StageIfUsed(themed, dir);
            File.ReadAllText(Path.Combine(dir, "beamerthemeLiliaExposition.sty")).Should().Be(ThemeCatalog.ExpositionStySource);
            File.Exists(Path.Combine(dir, "lilia-theme.sty")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_sty_is_olivias_and_passes_the_source_guard()
    {
        var sty = ThemeCatalog.ExpositionStySource;
        sty.Should().Contain(@"\ProvidesPackage{beamerthemeLiliaExposition}");
        sty.Should().Contain(@"\DeclareOptionBeamer{printsafe}");
        foreach (var hex in new[] { "4A1B22", "F3E9DC", "EBC155" })
            sty.Should().Contain($"{{HTML}}{{{hex}}}");
        var expo = ThemeCatalog.Find("exposition")!;
        expo.Colours.Paper.Should().Be("#4A1B22");
        expo.Colours.Ink.Should().Be("#F3E9DC");
        expo.Colours.Accent.Should().Be("#EBC155");
        TexSourceGuard.Violation(sty).Should().BeNull();
        // Fonts only through the TeX font packages: no fontspec, no font files.
        sty.Should().NotContain("{fontspec}").And.NotContain(".otf").And.NotContain(".ttf");
    }

    // ── slides and theorems: what a deck needs to compile at all ─────────

    [Fact]
    public void The_export_writes_slides_as_frames_like_the_preview()
    {
        var slide = Slide("The mixing matrix", "Quartz zebra with $\\theta_{12}$");
        var tex = Export(Doc(null), slide);
        tex.Should().Contain(@"\begin{frame}{The mixing matrix}").And.Contain(@"\end{frame}").And.Contain(@"$\theta_{12}$");
        tex.Should().NotContain("Unsupported block type for LaTeX export: slide");
        new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(slide)
            .Should().Contain(@"\begin{frame}{The mixing matrix}");
    }

    [Fact]
    public void Beamer_keeps_its_own_theorems_and_other_classes_are_unchanged()
    {
        LaTeXPreamble.TheoremEnvironmentsFor("article").Should().Be(LaTeXPreamble.TheoremEnvironments);
        LaTeXPreamble.TheoremEnvironmentsFor("report").Should().Be(LaTeXPreamble.TheoremEnvironments);
        var beamer = LaTeXPreamble.TheoremEnvironmentsFor("beamer");
        beamer.Should().Contain(@"\@ifundefined{theorem}{\newtheorem{theorem}{Theorem}}{}");
        beamer.Should().Contain(@"\@ifundefined{remark*}{\newtheorem*{remark*}{Remark}}{}");
        beamer.Should().StartWith(@"\makeatletter");
        Export(Doc(null), Slide("One", "Body")).Should().Contain(@"\@ifundefined{lemma}{\newtheorem{lemma}{Lemma}}{}");
    }
}
