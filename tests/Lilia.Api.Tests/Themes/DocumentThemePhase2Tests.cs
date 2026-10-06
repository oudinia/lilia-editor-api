using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Themes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// Document themes, phase 2, without a compile: Carnet and Gazette's colours, the table settings
/// (validation, storage, the theme line, the exporter's markers, the stripe and header colours)
/// and the section a block sits in (the "Used in" chip). The compiles are in
/// <see cref="DocumentThemeCompileTests"/>.
/// </summary>
[Collection(ThemeAvailabilityCollection.Name)]
public class DocumentThemePhase2Tests : IDisposable
{
    public DocumentThemePhase2Tests() =>
        ThemeAvailability.OverrideForTests(id => id is "classic" or "cerulean" or "index" or "carnet" or "gazette");

    public void Dispose() => ThemeAvailability.OverrideForTests(null);

    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Block H(string text, int level = 1, bool numbered = true) => B("heading", new { text, level, numbered });

    private static Block Table(int rows) => B("table", new
    {
        caption = "Results",
        headers = new[] { "Model", "Acc" },
        rows = Enumerable.Range(1, rows).Select(i => new[] { $"Row {i}", $"{i}" }).ToArray(),
    });

    private static Document Doc(string? look, string? cls = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Notes", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls, Look = look,
    };

    private static string Export(Document doc, params Block[] blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks.ToList(), [], new LaTeXExportOptions());

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // ── Carnet and Gazette ───────────────────────────────────────────────

    [Theory]
    // Measured with the WCAG 2 formula. The design's table says 13.2:1 and 11.1:1; its numbers run
    // about 3-8% above WCAG 2 for every theme (Cerulean 15.6 -> 15.2, Index 13.9 -> 13.6), so the
    // floor asserted is the measured one, and both clear the 7:1 rule.
    [InlineData("carnet", "#F4EFE4", "#2B2724", 12.9)]
    [InlineData("gazette", "#F3D54E", "#2A2926", 10.0)]
    public void Carnet_and_gazette_set_dark_ink_on_their_paper_and_print_on_white(string id, string paper, string ink, double measured)
    {
        var t = ThemeCatalog.Find(id)!;
        t.IsBuilt.Should().BeTrue();
        t.Colours.Paper.Should().Be(paper);
        t.Colours.Ink.Should().Be(ink);
        Contrast(ink, paper).Should().BeGreaterThanOrEqualTo(measured).And.BeGreaterThanOrEqualTo(7.0);
        t.PrintSafe.Paper.Should().Be("#FFFFFF", "print-safe is white paper");
        Contrast(t.PrintSafe.Ink, "#FFFFFF").Should().BeGreaterThanOrEqualTo(7.0);
    }

    [Fact]
    public void Carnets_terracotta_is_readable_on_cream_as_heading_and_label_ink() =>
        Contrast("#9A3F1C", "#F4EFE4").Should().BeGreaterThanOrEqualTo(4.5);

    [Theory]
    [InlineData("carnet", "ebgaramond")]
    [InlineData("gazette", "josefin")]
    [InlineData("gazette", "montserrat")]
    public void Their_faces_come_from_the_tex_font_packages_and_are_checked_on_this_server(string id, string package)
    {
        ThemeCatalog.Find(id)!.TexFiles.Should().Contain($"{package}.sty");
        ThemeCatalog.StySource.Should().Contain($"{{{package}}}");
    }

    [Fact]
    public void The_package_defines_carnet_and_gazette_with_the_descriptors_colours()
    {
        var sty = ThemeCatalog.StySource;
        foreach (var id in new[] { "carnet", "gazette" })
        {
            var t = ThemeCatalog.Find(id)!;
            sty.Should().Contain($@"\definecolor{{lilia@ink}}{{HTML}}{{{t.Colours.Ink.TrimStart('#')}}}", id);
            sty.Should().Contain($@"\definecolor{{lilia@paperc}}{{HTML}}{{{t.Colours.Paper.TrimStart('#')}}}", id);
        }
        sty.Should().Contain($@"\definecolor{{lilia@sec}}{{HTML}}{{{ThemeCatalog.Find("carnet")!.Colours.Heading.TrimStart('#')}}}");
    }

    [Theory]
    [InlineData("carnet")]
    [InlineData("gazette")]
    public void Their_theme_line_is_written_like_the_others(string theme)
    {
        LaTeXPreambleBuilder.BuildThemeLine(Doc($$"""{"theme":"{{theme}}"}"""), [H("One")])
            .Should().Contain($@"\usepackage[theme={theme}, paper=theme]{{lilia-theme}}");
        var printSafe = DocumentLook.Parse($$"""{"theme":"{{theme}}"}""").With(null, printSafe: true);
        LaTeXPreambleBuilder.BuildThemeLine(Doc($$"""{"theme":"{{theme}}"}"""), [H("One")], printSafe)
            .Should().Contain($@"\usepackage[theme={theme}, paper=white, printsafe]{{lilia-theme}}");
    }

    // ── tables: validation and storage ───────────────────────────────────

    [Fact]
    public void A_tables_setting_is_normalised_and_stored_with_the_look()
    {
        var (look, errors) = DocumentLook.Validate(Json("""{"theme":"Cerulean","tables":{"style":"BANDED","density":"compact","caption":"below"}}"""));
        errors.Should().BeEmpty();
        look!.Tables.Should().Be(new DocumentTables("banded", "compact", "below"));
        look.ToStorage().Should().Be("""{"theme":"cerulean","paper":"theme","pins":{},"tables":{"style":"banded","density":"compact","caption":"below"}}""");
        DocumentLook.Parse(look.ToStorage()).Tables.Should().Be(look.Tables);
    }

    [Fact]
    public void Only_the_values_set_are_stored_and_the_rest_are_the_defaults()
    {
        var gazette = DocumentLook.Validate(Json("""{"theme":"gazette","tables":{"density":"compact"}}""")).Look!;
        gazette.ToStorage().Should().EndWith(""","tables":{"density":"compact"}}""");
        gazette.TableStyle.Should().Be("header", "Gazette's default style");
        gazette.CaptionsBelow.Should().BeFalse();

        var empty = DocumentLook.Validate(Json("""{"theme":"index","tables":{}}""")).Look!;
        empty.ToStorage().Should().NotContain("tables");
        empty.TableStyle.Should().Be("ruled");
        empty.LoadsPackage.Should().BeTrue();

        DocumentLook.Validate(Json("""{"theme":"index","tables":null}""")).Look!.Tables.IsEmpty.Should().BeTrue();
        DocumentLook.Validate(Json("""{"theme":"index","tables":{"style":null}}""")).Look!.Tables.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Classic_is_stored_as_nothing_unless_it_carries_a_table_setting()
    {
        DocumentLook.Validate(Json("""{"theme":"classic"}""")).Look!.ToStorage().Should().BeNull();
        var banded = DocumentLook.Validate(Json("""{"theme":"classic","tables":{"style":"banded"}}""")).Look!;
        banded.ToStorage().Should().Be("""{"theme":"classic","paper":"theme","pins":{},"tables":{"style":"banded"}}""");
        banded.LoadsPackage.Should().BeTrue();
        DocumentLook.Validate(Json("""{"theme":"classic","tables":{"style":"ruled"}}""")).Look!.LoadsPackage
            .Should().BeFalse("ruled, normal and above are today's tables");
    }

    [Theory]
    [InlineData("""{"theme":"index","tables":{"style":"zebra"}}""", "look.tables.style 'zebra' is not valid. Valid values: ruled, banded, header.")]
    [InlineData("""{"theme":"index","tables":{"density":"tight"}}""", "look.tables.density 'tight' is not valid. Valid values: normal, compact.")]
    [InlineData("""{"theme":"index","tables":{"caption":"side"}}""", "look.tables.caption 'side' is not valid. Valid values: above, below.")]
    [InlineData("""{"theme":"index","tables":{"style":3}}""", "look.tables.style '3' is not valid")]
    [InlineData("""{"theme":"index","tables":{"width":"full"}}""", "look.tables.width is not a table setting. Valid keys: style, density, caption.")]
    [InlineData("""{"theme":"index","tables":["banded"]}""", "look.tables must be an object")]
    public void A_bad_tables_setting_is_refused_with_the_valid_values(string json, string message)
    {
        var (look, errors) = DocumentLook.Validate(Json(json));
        look.Should().BeNull();
        string.Join(" ", errors).Should().Contain(message);
    }

    private static UpdateDocumentDto Put(string lookJson) => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, null, null, Look: Json(lookJson));

    [Fact]
    public void Put_stores_classic_with_a_table_setting()
    {
        DocumentService.ResolveLookUpdate(Put("""{"theme":"classic","tables":{"style":"header"}}"""), "article")
            .Should().Be((true, """{"theme":"classic","paper":"theme","pins":{},"tables":{"style":"header"}}"""));
        DocumentService.ResolveLookUpdate(Put("""{"theme":"cerulean","tables":{"style":"banded"}}"""), "report").Apply
            .Should().BeTrue();
    }

    [Fact]
    public void Put_refuses_a_table_setting_under_a_publisher_class_but_accepts_todays_tables()
    {
        var banded = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"classic","tables":{"style":"banded"}}"""), "IEEEtran");
        banded.Should().Throw<DocumentLookException>()
            .WithMessage("IEEEtran sets its own look, so themes are off for this document. Its tables stay ruled.");
        var below = () => DocumentService.ResolveLookUpdate(Put("""{"theme":"classic","tables":{"caption":"below"}}"""), "acmart");
        below.Should().Throw<DocumentLookException>();

        DocumentService.ResolveLookUpdate(Put("""{"theme":"classic","tables":{"style":"ruled","density":"normal"}}"""), "IEEEtran").Apply
            .Should().BeTrue();
        DocumentService.ResolveLookUpdate(Put("""{"theme":"classic"}"""), "IEEEtran").Should().Be((true, (string?)null));
    }

    // ── tables: the theme line ───────────────────────────────────────────

    [Theory]
    [InlineData("""{"theme":"cerulean","tables":{"style":"banded"}}""", @"\usepackage[theme=cerulean, paper=theme, tables=banded]{lilia-theme}")]
    [InlineData("""{"theme":"index","tables":{"style":"header","density":"compact","caption":"below"}}""", @"\usepackage[theme=index, paper=theme, tables=header, tabledensity=compact, captions=below]{lilia-theme}")]
    [InlineData("""{"theme":"gazette","tables":{"style":"ruled"}}""", @"\usepackage[theme=gazette, paper=theme, tables=ruled]{lilia-theme}")]
    [InlineData("""{"theme":"carnet","tables":{"caption":"below"}}""", @"\usepackage[theme=carnet, paper=theme, captions=below]{lilia-theme}")]
    public void The_theme_line_names_the_table_settings_that_differ_from_the_themes_defaults(string look, string line) =>
        LaTeXPreambleBuilder.BuildThemeLine(Doc(look), [H("One")]).Should().Contain(line);

    [Theory]
    [InlineData("""{"theme":"gazette","tables":{"style":"header"}}""")]
    [InlineData("""{"theme":"cerulean","tables":{"style":"ruled","density":"normal","caption":"above"}}""")]
    public void A_themes_own_defaults_add_nothing(string look) =>
        LaTeXPreambleBuilder.BuildThemeLine(Doc(look), [H("One")])
            .Should().NotContain("tables=").And.NotContain("tabledensity").And.NotContain("captions");

    [Fact]
    public void Classic_with_a_table_setting_loads_only_the_table_part()
    {
        var line = LaTeXPreambleBuilder.BuildThemeLine(
            Doc("""{"theme":"classic","tables":{"style":"banded","density":"compact"}}"""), [H("One")]);
        line.Should().Contain(@"\usepackage[theme=classic, tables=banded, tabledensity=compact]{lilia-theme}");
        line.Should().NotContain("paper=").And.NotContain("liliaPinColour");

        LaTeXPreambleBuilder.BuildThemeLine(Doc("""{"theme":"classic","tables":{"style":"ruled"}}"""), [H("One")]).Should().BeEmpty();
        LaTeXPreambleBuilder.BuildThemeLine(Doc(null), [H("One")]).Should().BeEmpty();

        var tex = Export(Doc("""{"theme":"classic","tables":{"style":"header"}}"""), H("One"));
        tex.Should().Contain(@"\usepackage[theme=classic, tables=header]{lilia-theme}");
        ThemeCatalog.UsesThemePackage(tex).Should().BeTrue("the .zip carries the package");
    }

    [Theory]
    [InlineData("""{"theme":"classic","tables":{"style":"banded"}}""")]
    [InlineData("""{"theme":"cerulean","tables":{"style":"header"}}""")]
    public void Under_a_publisher_class_nothing_is_written_and_tables_stay_ruled(string look)
    {
        LaTeXPreambleBuilder.BuildThemeLine(Doc(look, cls: "IEEEtran"), [H("One")]).Should().BeEmpty();
        Export(Doc(look, cls: "acmart"), H("One"), Table(5)).Should().NotContain("{lilia-theme}");
    }

    [Fact]
    public void Classic_with_a_table_setting_goes_to_latex()
    {
        PageSetupRouting.WhyLatex(Doc("""{"theme":"classic","tables":{"style":"banded"}}""")).Should().Be("theme");
        PageSetupRouting.WhyLatex(Doc("""{"theme":"classic","tables":{"style":"ruled"}}""")).Should().BeNull();
    }

    [Fact]
    public void An_export_override_keeps_the_table_settings()
    {
        var look = DocumentLook.Parse("""{"theme":"cerulean","tables":{"density":"compact"}}""").With("gazette", printSafe: true);
        look.Tables.Density.Should().Be("compact");
        LaTeXPreambleBuilder.BuildThemeLine(Doc("""{"theme":"cerulean"}"""), [H("One")], look)
            .Should().Contain(@"\usepackage[theme=gazette, paper=white, printsafe, tabledensity=compact]{lilia-theme}");
    }

    // ── tables: what the exporters write ─────────────────────────────────

    private static string TableOf(string tex) =>
        tex[tex.IndexOf(@"\begin{table}", StringComparison.Ordinal)..(tex.IndexOf(@"\end{table}", StringComparison.Ordinal) + 11)];

    [Fact]
    public void Header_cells_are_wrapped_their_bold_kept_and_the_wrapper_falls_back_to_the_cell()
    {
        var tex = Export(Doc(null), Table(5));
        tex.Should().Contain(@"\liliaHeadRow \liliaTableHead{\textbf{Model}} & \liliaTableHead{\textbf{Acc}} \\");
        tex.Should().Contain(@"\providecommand{\liliaTableHead}[1]{#1}");
        tex.Should().NotContain(@"\liliaFewRows", "five body rows can be banded");

        var preview = new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(Table(5));
        preview.Should().Contain(@"\liliaHeadRow \liliaTableHead{\textbf{Model}} & \liliaTableHead{\textbf{Acc}} \\");
        preview.Should().Contain(@"\providecommand{\liliaTableHead}[1]{#1}").And.NotContain(@"\liliaFewRows");
    }

    [Fact]
    public void A_header_span_keeps_multicolumn_first_in_its_cell()
    {
        var block = B("table", new
        {
            headers = new object[] { new { text = "Scores", colspan = 2 } },
            rows = new[] { new[] { "a", "1" } },
        });
        var preview = new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(block);
        preview.Should().Contain(@"\liliaHeadRow \multicolumn{2}{l}{\liliaTableHead{\textbf{Scores}}}");
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void A_table_with_fewer_than_four_body_rows_is_marked_so_it_is_never_banded(int rows, bool marked)
    {
        var line = @"\providecommand{\liliaFewRows}{}\liliaFewRows";
        Export(Doc(null), Table(rows)).Contains(line).Should().Be(marked);
        new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(Table(rows)).Contains(line).Should().Be(marked);
    }

    [Fact]
    public void The_tables_latex_is_identical_under_every_paper_and_table_setting()
    {
        var table = Table(5);
        var plain = TableOf(Export(Doc(null), table));
        foreach (var look in new[]
                 {
                     """{"theme":"cerulean","tables":{"style":"banded"}}""",
                     """{"theme":"gazette"}""",
                     """{"theme":"carnet","paper":"white","tables":{"density":"compact","caption":"below"}}""",
                     """{"theme":"classic","tables":{"style":"header"}}""",
                     """{"theme":"index","tables":{"style":"header"}}""",
                 })
            TableOf(Export(Doc(look), table)).Should().Be(plain, look);
    }

    // ── tables: the stripe and header colours ────────────────────────────

    public static TheoryData<string> ThemesWithTables()
    {
        var data = new TheoryData<string>();
        foreach (var t in ThemeCatalog.All.Where(t => t.Table is not null)) data.Add(t.Id);
        return data;
    }

    [Fact]
    public void Every_built_theme_has_table_colours()
    {
        ThemeCatalog.All.Where(t => t.IsBuilt).Should().OnlyContain(t => t.Table != null);
    }

    [Theory]
    [MemberData(nameof(ThemesWithTables))]
    public void Body_text_keeps_12_to_1_on_every_stripe_on_either_paper(string id)
    {
        var t = ThemeCatalog.Find(id)!;
        foreach (var (spec, paper, ink) in new[] { (t.Table!.OnPaper, t.Colours.Paper, t.Colours.Ink), (t.Table!.OnWhite, "#FFFFFF", t.PrintSafe.Ink) })
        {
            foreach (var hue in Hues(spec.Hue, t))
            {
                var stripe = Mix(hue, spec.Band / 100.0, paper);
                Contrast(ink, stripe).Should().BeGreaterThanOrEqualTo(12.0, $"{id}: {spec.Band}% of {hue} over {paper} is {stripe}");
            }
            spec.Band.Should().BeGreaterThan(0);
        }
    }

    [Theory]
    [MemberData(nameof(ThemesWithTables))]
    public void Header_band_ink_is_white_only_where_the_fill_takes_it_at_4_5_to_1(string id)
    {
        var t = ThemeCatalog.Find(id)!;
        foreach (var spec in new[] { t.Table!.OnPaper, t.Table!.OnWhite })
        {
            foreach (var fill in Hues(spec.Head, t))
            {
                var white = Contrast("#FFFFFF", fill) >= 4.5;
                spec.HeadInk.Should().Be(white ? "#FFFFFF" : t.Colours.Ink, $"{id}: the header on {fill}");
                Contrast(spec.HeadInk, fill).Should().BeGreaterThanOrEqualTo(4.5);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ThemesWithTables))]
    public void The_package_uses_the_descriptors_table_colours(string id)
    {
        var t = ThemeCatalog.Find(id)!;
        foreach (var spec in new[] { t.Table!.OnPaper, t.Table!.OnWhite })
            ThemeCatalog.StySource.Should().Contain(
                $@"\lilia@tablecolours{{{Hex(spec.Hue)}}}{{{spec.Band}}}{{{Hex(spec.Head)}}}{{{Hex(spec.HeadInk)}}}", id);
    }

    private static string Hex(string colour) => colour == "chapter" ? "chapter" : colour.TrimStart('#');

    /// <summary>A table hue or fill: one colour, or for "chapter" every Index colour and the neutral ink.</summary>
    private static IEnumerable<string> Hues(string colour, ThemeDescriptor t) =>
        colour == "chapter" ? ThemeCatalog.Sequence.Append(t.Colours.Ink) : [colour];

    // ── the section a block sits in ──────────────────────────────────────

    private static IReadOnlyDictionary<Guid, SectionPlace> Places(string? cls, string? look, params Block[] blocks) =>
        ThemeSections.Places(blocks, cls, look);

    [Fact]
    public void In_an_article_a_block_takes_its_sections_number()
    {
        var before = Table(5);
        var t1 = Table(5);
        var sub = H("Sub", level: 2);
        var t2 = Table(5);
        var unnumbered = H("Further reading", numbered: false);
        var t3 = Table(5);
        var places = Places("article", null, before, H("One"), t1, H("Two"), sub, t2, unnumbered, t3);

        places[before.Id].Should().Be(SectionPlace.None, "before the first numbered heading");
        places[t1.Id].Number.Should().Be("1");
        places[t2.Id].Number.Should().Be("2", "a subsection does not change the top heading");
        places[t3.Id].Should().Be(SectionPlace.None, "under an unnumbered heading");
        places.Values.Should().OnlyContain(p => p.Colour == null, "Classic has no section colours");
    }

    [Theory]
    [InlineData("report")]
    [InlineData("book")]
    public void In_a_report_or_book_a_block_takes_its_chapters_number(string cls)
    {
        var t = Table(5);
        var places = Places(cls, """{"theme":"cerulean"}""", H("One"), H("Two"), H("Two point one", level: 2), t);
        places[t.Id].Should().Be(new SectionPlace("2", null), "only Index has section colours");
    }

    [Fact]
    public void A_chapter_class_without_level_1_headings_numbers_its_sections_as_it_prints_them()
    {
        var t = Table(5);
        Places("report", null, H("A section", level: 2), H("Another", level: 2), t)[t.Id].Number.Should().Be("0.2");
    }

    [Fact]
    public void Index_colours_follow_the_sequence_and_pins_and_appendices_restart_in_letters()
    {
        var one = H("One");
        var two = H("Two");
        var three = H("Three");
        var t1 = Table(5);
        var t2 = Table(5);
        var t3 = Table(5);
        var tA = Table(5);
        var tB = Table(5);
        var appendix = B("embed", new { code = @"\appendix" });
        // Three is pinned to the eighth colour.
        var look = $$$"""{"theme":"index","pins":{"{{{three.Id}}}":7}}""";
        var places = Places("article", look, one, t1, two, t2, three, t3, appendix, H("Proofs"), tA, H("Tables"), tB);

        places[t1.Id].Should().Be(new SectionPlace("1", "#4A5FA3"));
        places[t2.Id].Should().Be(new SectionPlace("2", "#996300"));
        places[t3.Id].Should().Be(new SectionPlace("3", "#2E6E9E"), "pinned to the eighth colour");
        places[tA.Id].Should().Be(new SectionPlace("A", "#4A5FA3"), "appendices restart the plain sequence");
        places[tB.Id].Should().Be(new SectionPlace("B", "#996300"));
    }

    [Fact]
    public void Unpinned_chapters_skip_the_colours_pins_took()
    {
        var one = H("One");
        var t1 = Table(5);
        var t2 = Table(5);
        var t3 = Table(5);
        // One is pinned to the second colour: Two takes the first, Three the third.
        var places = Places("report", $$$"""{"theme":"index","pins":{"{{{one.Id}}}":1}}""", one, t1, H("Two"), t2, H("Three"), t3);
        places[t1.Id].Should().Be(new SectionPlace("1", "#996300"));
        places[t2.Id].Should().Be(new SectionPlace("2", "#4A5FA3"));
        places[t3.Id].Should().Be(new SectionPlace("3", "#B8303A"));
    }

    [Fact]
    public void Raw_latex_chapters_count_and_starred_ones_are_unnumbered()
    {
        var t1 = Table(5);
        var t2 = Table(5);
        var t3 = Table(5);
        var places = Places("book", """{"theme":"index"}""",
            B("embed", new { code = @"\chapter{Raw}" }), t1,
            B("embed", new { code = "% \\chapter{commented}\n\\chapter*{Preface}" }), t2,
            H("One"), t3);
        places[t1.Id].Should().Be(new SectionPlace("1", "#4A5FA3"));
        places[t2.Id].Should().Be(SectionPlace.None);
        places[t3.Id].Should().Be(new SectionPlace("2", "#996300"));
    }

    [Fact]
    public void A_locked_class_has_numbers_but_no_colours()
    {
        var t = Table(5);
        Places("IEEEtran", """{"theme":"index"}""", H("One"), t)[t.Id].Should().Be(new SectionPlace("1", null));
    }

    [Fact]
    public void The_body_leaves_out_a_leading_heading_that_repeats_the_title()
    {
        var t = Table(5);
        var body = LaTeXExportService.BodyBlocks("Notes", [B("title", new { text = "Notes" }), H("Notes"), H("One"), t]);
        ThemeSections.PlaceOf(t.Id, body, "article", """{"theme":"index"}""").Should().Be(new SectionPlace("1", "#4A5FA3"));
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string Mix(string hex, double amount, string paper)
    {
        var h = hex.TrimStart('#');
        var p = paper.TrimStart('#');
        int C(int i) => (int)Math.Round(Convert.ToInt32(h.Substring(i, 2), 16) * amount + Convert.ToInt32(p.Substring(i, 2), 16) * (1 - amount));
        return $"#{C(0):X2}{C(2):X2}{C(4):X2}";
    }

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
