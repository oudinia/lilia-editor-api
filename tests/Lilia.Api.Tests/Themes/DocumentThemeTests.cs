using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Lilia.Engines.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// Document themes (Document settings → Look), without a compile: the descriptors, the stored
/// look and its validation, the class lock, and the one preamble line the look becomes.
/// The compiles are in <see cref="DocumentThemeCompileTests"/>.
/// </summary>
[Collection(ThemeAvailabilityCollection.Name)]
public class DocumentThemeTests : IDisposable
{
    public DocumentThemeTests() => ThemeAvailability.OverrideForTests(id => id is "classic" or "cerulean" or "index");

    public void Dispose() => ThemeAvailability.OverrideForTests(null);

    private static int _order;

    private static Block B(string type, string json) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(json),
    };

    private static Block Heading(string text, int level = 1, bool numbered = true) =>
        B("heading", JsonSerializer.Serialize(new { text, level, numbered }));

    private static Document Doc(string? look, string? cls = null, string? preamble = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Notes", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls, Look = look, CustomPreamble = preamble,
    };

    private static string Export(Document doc, params Block[] blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks.ToList(), [], new LaTeXExportOptions());

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // ── themes.json: the contract the web builds against ─────────────────

    [Fact]
    public void Six_themes_in_the_contracts_order_with_only_exposition_planned()
    {
        ThemeCatalog.Ids.Should().Equal("classic", "cerulean", "index", "carnet", "gazette", "exposition");
        // Exposition is for beamer, which locks the look: a separate decision.
        ThemeCatalog.All.Where(t => t.Status == "planned").Select(t => t.Id).Should().BeEquivalentTo("exposition");
        ThemeCatalog.All.Where(t => t.IsBuilt).Select(t => t.Id)
            .Should().BeEquivalentTo("classic", "cerulean", "index", "carnet", "gazette");
    }

    [Fact]
    public void The_index_sequence_is_the_designed_one_and_only_index_carries_it()
    {
        var designed = new[] { "#4A5FA3", "#996300", "#B8303A", "#3A3836", "#B84A10", "#66701A", "#6E2430", "#2E6E9E" };
        ThemeCatalog.Sequence.Should().Equal(designed);
        ThemeCatalog.Find("index")!.Sequence.Should().Equal(designed);
        ThemeCatalog.All.Where(t => t.Id != "index").Should().OnlyContain(t => t.Sequence == null);
    }

    [Fact]
    public void Gazette_and_exposition_default_to_a_header_band_and_the_rest_to_ruled()
    {
        ThemeCatalog.All.ToDictionary(t => t.Id, t => t.TablesDefault).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["classic"] = "ruled", ["cerulean"] = "ruled", ["index"] = "ruled",
            ["carnet"] = "ruled", ["gazette"] = "header", ["exposition"] = "header",
        });
    }

    [Fact]
    public void Body_ink_is_at_least_7_to_1_on_the_paper_and_in_print()
    {
        foreach (var t in ThemeCatalog.All)
        {
            Contrast(t.Colours.Ink, t.Colours.Paper).Should().BeGreaterThanOrEqualTo(7.0, $"{t.Id} body text");
            Contrast(t.PrintSafe.Ink, t.PrintSafe.Paper).Should().BeGreaterThanOrEqualTo(7.0, $"{t.Id} print-safe body text");
        }
    }

    [Fact]
    public void Every_index_colour_is_at_least_4_5_to_1_on_white_and_on_its_own_8_percent_tint()
    {
        // Olivia's rule (5 Oct): every sequence colour also sits on its own 8% tint (Banded table
        // stripes, tinted boxes), so it must pass there too. The gold and the orange were
        // corrected to #996300 and #B84A10 for it.
        foreach (var hex in ThemeCatalog.Sequence)
        {
            Contrast(hex, "#FFFFFF").Should().BeGreaterThanOrEqualTo(4.5, $"{hex} on white");
            Contrast(hex, Tint(hex, 0.08)).Should().BeGreaterThanOrEqualTo(4.5, $"{hex} on its tint");
        }
    }

    private static string Tint(string hex, double amount)
    {
        var h = hex.TrimStart('#');
        int Mix(int i) => (int)Math.Round(Convert.ToInt32(h.Substring(i, 2), 16) * amount + 255 * (1 - amount));
        return $"#{Mix(0):X2}{Mix(2):X2}{Mix(4):X2}";
    }

    [Fact]
    public void The_package_uses_the_descriptors_colours()
    {
        var sty = ThemeCatalog.StySource;
        for (var i = 0; i < ThemeCatalog.Sequence.Count; i++)
            sty.Should().Contain($@"\definecolor{{lilia@seq{i}}}{{HTML}}{{{ThemeCatalog.Sequence[i].TrimStart('#')}}}");
        foreach (var id in new[] { "cerulean", "index" })
        {
            var t = ThemeCatalog.Find(id)!;
            sty.Should().Contain($@"\definecolor{{lilia@ink}}{{HTML}}{{{t.Colours.Ink.TrimStart('#')}}}", id);
        }
        sty.Should().Contain($@"\definecolor{{lilia@sec}}{{HTML}}{{{ThemeCatalog.Find("cerulean")!.Colours.Heading.TrimStart('#')}}}");
        sty.Should().Contain($@"\definecolor{{lilia@kicker}}{{HTML}}{{{ThemeCatalog.Find("index")!.Colours.Kicker.TrimStart('#')}}}");
    }

    [Fact]
    public void The_package_passes_the_source_guard_and_loads_fonts_only_through_font_packages()
    {
        TexSourceGuard.Violation(ThemeCatalog.StySource).Should().BeNull();
        ThemeCatalog.StySource.Should().NotContain("fontspec").And.NotContain(".otf");
    }

    // ── the stored look and its validation ──────────────────────────────

    [Fact]
    public void A_valid_look_is_normalised()
    {
        var (look, errors) = DocumentLook.Validate(Json("""{"theme":"Index","paper":"WHITE","pins":{"b1":3}}"""));
        errors.Should().BeEmpty();
        look!.Theme.Should().Be("index");
        look.Paper.Should().Be("white");
        look.Pins.Should().ContainKey("b1").WhoseValue.Should().Be(3);
        look.ToStorage().Should().Be("""{"theme":"index","paper":"white","pins":{"b1":3}}""");
    }

    [Fact]
    public void Paper_defaults_to_the_themes_and_classic_is_stored_as_nothing()
    {
        DocumentLook.Validate(Json("""{"theme":"cerulean"}""")).Look!.Paper.Should().Be("theme");
        DocumentLook.Validate(Json("""{"theme":"classic","pins":{"b":1}}""")).Look!.ToStorage().Should().BeNull();
    }

    [Theory]
    [InlineData("""{"theme":"neon"}""", "classic, cerulean, index, carnet, gazette, exposition")]
    [InlineData("""{"paper":"theme"}""", "look.theme is required")]
    [InlineData("""{"theme":"index","paper":"cream"}""", "Valid values: theme, white")]
    [InlineData("""{"theme":"index","pins":{"b1":8}}""", "from 0 to 7")]
    [InlineData("""{"theme":"index","pins":{"b1":-1}}""", "from 0 to 7")]
    [InlineData("""{"theme":"index","pins":{"b1":"red"}}""", "from 0 to 7")]
    [InlineData("""{"theme":"index","pins":[1,2]}""", "must be an object")]
    [InlineData("""{"theme":"index","width":"full"}""", "Valid keys: theme, paper, pins, tables")]
    [InlineData("""{"theme":"index","tables":"banded"}""", "look.tables must be an object")]
    [InlineData("""["index"]""", "look must be an object")]
    public void An_invalid_look_is_refused_with_the_valid_values(string json, string named)
    {
        var (look, errors) = DocumentLook.Validate(Json(json));
        look.Should().BeNull();
        string.Join(" ", errors).Should().Contain(named);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"theme":"neon"}""")]
    public void An_unreadable_stored_look_is_classic(string? stored) =>
        DocumentLook.Parse(stored).IsClassic.Should().BeTrue();

    [Fact]
    public void An_export_override_replaces_the_theme_and_print_safe_forces_white_paper()
    {
        var stored = DocumentLook.Parse("""{"theme":"index","paper":"theme","pins":{"b":2}}""");
        var exported = stored.With("cerulean", printSafe: true);
        exported.Theme.Should().Be("cerulean");
        exported.Paper.Should().Be("white");
        exported.PrintSafe.Should().BeTrue();
        exported.Pins.Should().ContainKey("b");
        stored.With(null, printSafe: false).Should().BeSameAs(stored);
    }

    // ── PUT /api/documents/{id}: what is stored ─────────────────────────

    private static UpdateDocumentDto Put(string lookJson) => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, null, null, Look: Json(lookJson));

    [Fact]
    public void Put_stores_a_theme_and_classic_clears_it()
    {
        DocumentService.ResolveLookUpdate(Put("""{"theme":"index"}"""), "article")
            .Should().Be((true, """{"theme":"index","paper":"theme","pins":{}}"""));
        DocumentService.ResolveLookUpdate(Put("""{"theme":"classic"}"""), "article")
            .Should().Be((true, (string?)null));
    }

    [Fact]
    public void Put_without_a_look_leaves_it_alone() =>
        DocumentService.ResolveLookUpdate(new UpdateDocumentDto(
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null), "article").Apply.Should().BeFalse();

    [Fact]
    public void Put_refuses_a_theme_on_a_publisher_class_but_accepts_classic()
    {
        var act = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"cerulean"}"""), "IEEEtran");
        act.Should().Throw<DocumentLookException>().WithMessage("IEEEtran sets its own look, so themes are off for this document.");
        DocumentService.ResolveLookUpdate(Put("""{"theme":"classic"}"""), "IEEEtran").Apply.Should().BeTrue();
    }

    [Fact]
    public void Put_refuses_a_planned_theme_and_one_whose_fonts_are_missing()
    {
        var planned = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"exposition"}"""), "article");
        planned.Should().Throw<DocumentLookException>().WithMessage("*planned*Available themes: classic, cerulean, index.");

        ThemeAvailability.OverrideForTests(id => id != "index");
        var missing = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"index"}"""), "article");
        missing.Should().Throw<DocumentLookException>().WithMessage("*fonts are not installed*");
    }

    [Theory]
    [InlineData("IEEEtran")]
    [InlineData("acmart")]
    [InlineData("llncs")]
    [InlineData("elsarticle")]
    [InlineData("revtex4-1")]
    [InlineData("revtex4-2")]
    [InlineData("beamer")]
    [InlineData("moderncv")]
    public void Classes_that_set_their_own_look_lock_it(string cls) =>
        ThemeLock.Reason(cls).Should().Be($"{cls} sets its own look, so themes are off for this document.");

    [Theory]
    [InlineData(null)]
    [InlineData("article")]
    [InlineData("report")]
    [InlineData("book")]
    [InlineData("amsart")]
    [InlineData("memoir")]
    public void Ordinary_classes_do_not(string? cls) => ThemeLock.Reason(cls).Should().BeNull();

    // ── the preamble line ───────────────────────────────────────────────

    [Fact]
    public void Classic_writes_no_line_and_no_package()
    {
        var tex = Export(Doc(null), Heading("One"));
        tex.Should().NotContain("lilia-theme");
        Export(Doc("""{"theme":"classic"}"""), Heading("One")).Should().NotContain("lilia-theme");
    }

    [Fact]
    public void A_theme_on_a_publisher_class_writes_no_line()
    {
        Export(Doc("""{"theme":"index"}""", cls: "IEEEtran"), Heading("One")).Should().NotContain("lilia-theme");
    }

    [Fact]
    public void The_theme_line_goes_just_before_the_custom_preamble()
    {
        var tex = Export(Doc("""{"theme":"cerulean","paper":"white"}""", preamble: @"\newcommand{\R}{\mathbb{R}}"), Heading("One"));

        var line = tex.IndexOf(@"\usepackage[theme=cerulean, paper=white]{lilia-theme}", StringComparison.Ordinal);
        var custom = tex.IndexOf(@"\newcommand{\R}{\mathbb{R}}", StringComparison.Ordinal);
        line.Should().BePositive();
        custom.Should().BeGreaterThan(line, "the author's preamble wins, so it comes after the theme");
        // ... and after every other package Lilia writes.
        tex.LastIndexOf(@"\usepackage", custom, StringComparison.Ordinal).Should().Be(line);
        TexSourceGuard.Violation(tex).Should().BeNull();
    }

    [Fact]
    public void The_preview_preamble_orders_it_the_same_way()
    {
        var doc = Doc("""{"theme":"index"}""", preamble: @"\newcommand{\R}{\mathbb{R}}");
        var preamble = RenderService.BuildPreambleForValidation(doc, LatexEngine.Pdflatex);
        var line = preamble.IndexOf(@"\usepackage[theme=index, paper=theme]{lilia-theme}", StringComparison.Ordinal);
        line.Should().BePositive();
        preamble.IndexOf(@"\newcommand{\R}", StringComparison.Ordinal).Should().BeGreaterThan(line);
        RenderService.BuildPreambleForValidation(Doc(null), LatexEngine.Pdflatex).Should().NotContain("lilia-theme");
        RenderService.BuildPreambleForValidation(Doc("""{"theme":"index"}""", cls: "acmart"), LatexEngine.Pdflatex)
            .Should().NotContain("lilia-theme");
    }

    [Fact]
    public void A_pin_is_written_as_its_headings_current_number()
    {
        var one = Heading("One");
        var unnumbered = Heading("Preface", numbered: false);
        var sub = Heading("One point one", level: 2);
        var two = Heading("Two");
        var three = Heading("Three");
        var doc = Doc($$$"""{"theme":"index","pins":{"{{{two.Id}}}":5,"{{{three.Id}}}":0,"gone":4}}""");

        var line = LaTeXPreambleBuilder.BuildThemeLine(doc, [one, unnumbered, sub, two, three]);

        line.Should().Contain(@"\liliaPinColour{2}{5}").And.Contain(@"\liliaPinColour{3}{0}");
        Regex.Matches(line, @"\\liliaPinColour").Count.Should().Be(2, "a pin whose heading is gone is dropped");

        // Moved: the pin follows the heading, not the position.
        LaTeXPreambleBuilder.BuildThemeLine(doc, [two, one, three]).Should().Contain(@"\liliaPinColour{1}{5}");
    }

    [Theory]
    [InlineData("report")]
    [InlineData("book")]
    public void A_chapter_class_colours_its_chapters_the_level_1_headings(string cls)
    {
        // In a class with \chapter a level-1 heading prints as \chapter (HeadingCommands): that is
        // the top numbered heading, so the pin maps to its chapter number.
        var two = Heading("Two");
        var doc = Doc($$$"""{"theme":"index","pins":{"{{{two.Id}}}":5}}""", cls: cls);
        var line = LaTeXPreambleBuilder.BuildThemeLine(doc, [Heading("One"), two]);
        line.Should().Contain(@"\usepackage[theme=index, paper=theme, top=chapter]{lilia-theme}");
        line.Should().Contain(@"\liliaPinColour{2}{5}");
    }

    [Fact]
    public void A_chapter_class_without_level_1_headings_colours_its_sections()
    {
        var doc = Doc("""{"theme":"index"}""", cls: "report");
        var sectionOnly = B("heading", """{"text":"Only a section","level":2}""");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [sectionOnly]).Should().Contain("top=section");
    }

    [Fact]
    public void An_embed_that_prints_its_own_chapter_keeps_pins_out_of_step_so_none_is_written()
    {
        var two = Heading("Two");
        var doc = Doc($$$"""{"theme":"index","pins":{"{{{two.Id}}}":5}}""", cls: "book");
        var chapter = B("embed", """{"code":"\\chapter{Raw chapter}"}""");
        var commented = B("embed", """{"code":"% \\chapter{not printed}\n\\section*{x}"}""");

        LaTeXPreambleBuilder.BuildThemeLine(doc, [commented, Heading("One"), two]).Should().Contain(@"\liliaPinColour{2}{5}");
        var line = LaTeXPreambleBuilder.BuildThemeLine(doc, [chapter, Heading("One"), two]);
        line.Should().Contain("top=chapter");
        line.Should().NotContain(@"\liliaPinColour", "the raw chapter puts the count out of step");
        // article has no \chapter: nothing to say.
        LaTeXPreambleBuilder.BuildThemeLine(Doc("""{"theme":"index"}"""), [chapter]).Should().NotContain("top=");
    }

    [Fact]
    public void An_author_header_footer_or_no_page_numbers_keeps_the_theme_off_the_foot()
    {
        var doc = Doc("""{"theme":"index"}""");
        LaTeXPreambleBuilder.BuildThemeLine(doc, []).Should().NotContain("foottab");
        doc.FooterRight = "Draft";
        LaTeXPreambleBuilder.BuildThemeLine(doc, []).Should().Contain("foottab=false");
        doc.FooterRight = null;
        doc.PageNumbering = "none";
        LaTeXPreambleBuilder.BuildThemeLine(doc, []).Should().Contain("foottab=false");
    }

    [Fact]
    public void A_compile_refuses_a_theme_this_server_cannot_print_and_a_download_writes_it()
    {
        ThemeAvailability.OverrideForTests(_ => false);
        var doc = Doc("""{"theme":"index"}""");

        var compile = () => LaTeXPreambleBuilder.BuildThemeLine(doc, []);
        compile.Should().Throw<ThemeUnavailableException>().WithMessage("*Index*fonts are not installed*");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [], use: LaTeXPreambleBuilder.ThemeUse.Export)
            .Should().Contain("{lilia-theme}");
        LaTeXPreambleBuilder.BuildThemeLine(doc, [], use: LaTeXPreambleBuilder.ThemeUse.Validation)
            .Should().BeEmpty();
    }

    [Fact]
    public void Print_safe_is_passed_to_the_package()
    {
        var look = DocumentLook.Parse("""{"theme":"cerulean"}""").With(null, printSafe: true);
        LaTeXPreambleBuilder.BuildThemeLine(Doc("""{"theme":"cerulean"}"""), [], look)
            .Should().Contain(@"\usepackage[theme=cerulean, paper=white, printsafe]{lilia-theme}");
    }

    [Fact]
    public void A_themed_document_goes_to_latex_and_a_locked_one_does_not_for_its_theme()
    {
        PageSetupRouting.WhyLatex(Doc("""{"theme":"index"}""")).Should().Be("theme");
        PageSetupRouting.WhyLatex(Doc(null)).Should().BeNull();
        PageSetupRouting.WhyLatex(Doc("""{"theme":"classic"}""")).Should().BeNull();
        PageSetupRouting.WhyLatex(Doc("""{"theme":"index"}""", cls: "IEEEtran")).Should().NotContain("theme");
    }

    // ── tables: one \liliaHeadRow, otherwise the same LaTeX ──────────────

    [Fact]
    public void A_header_row_starts_with_liliaHeadRow_and_keeps_its_bold()
    {
        var table = B("table", """{"caption":"R","headers":["Model","Acc"],"rows":[["A","1"]]}""");
        var tex = Export(Doc(null), table);

        tex.Should().Contain(@"\liliaHeadRow \liliaTableHead{\textbf{Model}} & \liliaTableHead{\textbf{Acc}} \\");
        tex.Should().Contain(string.Join(Environment.NewLine,
            @"\providecommand{\liliaHeadRow}{}", @"\providecommand{\liliaTableHead}[1]{#1}",
            @"\providecommand{\liliaFewRows}{}\liliaFewRows", @"\begin{tabular}"));
    }

    [Fact]
    public void The_tables_latex_is_identical_under_every_theme()
    {
        var table = B("table", """{"caption":"R","headers":["Model","Acc"],"rows":[["A","1"]]}""");
        string TableOf(string tex) => tex[tex.IndexOf(@"\begin{table}", StringComparison.Ordinal)..(tex.IndexOf(@"\end{table}", StringComparison.Ordinal) + 11)];

        var plain = TableOf(Export(Doc(null), table));
        TableOf(Export(Doc("""{"theme":"cerulean"}"""), table)).Should().Be(plain);
        TableOf(Export(Doc("""{"theme":"index","paper":"white"}"""), table)).Should().Be(plain);
    }

    [Fact]
    public void A_themed_zip_carries_the_package()
    {
        var tex = Export(Doc("""{"theme":"index"}"""), Heading("One"));
        ThemeCatalog.UsesThemePackage(tex).Should().BeTrue();
        ThemeCatalog.UsesThemePackage(Export(Doc(null), Heading("One"))).Should().BeFalse();

        var dir = Directory.CreateTempSubdirectory("lilia-theme-stage").FullName;
        try
        {
            ThemeCatalog.StageIfUsed(tex, dir);
            File.ReadAllText(Path.Combine(dir, "lilia-theme.sty")).Should().Be(ThemeCatalog.StySource);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static double Contrast(string a, string b)
    {
        static double Lum(string hex)
        {
            hex = hex.TrimStart('#');
            double C(int i)
            {
                var c = int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * C(0) + 0.7152 * C(2) + 0.0722 * C(4);
        }
        var (l1, l2) = (Lum(a), Lum(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}

/// <summary>Tests that set <see cref="ThemeAvailability.OverrideForTests"/> run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThemeAvailabilityCollection
{
    public const string Name = "Theme availability";
}
