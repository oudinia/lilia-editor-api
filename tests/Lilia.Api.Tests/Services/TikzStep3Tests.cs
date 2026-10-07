using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Themes;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Lilia.Engines.Themes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// TikZ figures, step 3 (7 Oct 2026), the API side: drafts for the split view (shared cache,
/// never the last good drawing, cancelled by a newer draft or an aborted request, the TeX process
/// killed), the precompiled format and its fallback, and the theme colour names
/// (<c>lilia-ink</c> … <c>lilia-seq8</c>) defined under every theme, Classic in ink and greys,
/// <c>lilia-chapter</c> following the figure's Index chapter, pins and appendix included.
/// </summary>
public class TikzStep3Tests
{
    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Block Figure(string source) => B("figure", new { kind = "tikz", source, caption = "", label = "" });

    private static Document Doc(string theme = "classic", string cls = "article", object? pins = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Notes", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls,
        Look = theme == "classic" ? null : JsonSerializer.Serialize(new { theme, paper = "theme", pins = pins ?? new { } }),
    };

    private static string Unique => Guid.NewGuid().ToString("N");

    private static TikzFigureService Service(string? dir = null, bool format = false, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tikz:CacheDir"] = dir ?? Path.Combine(Path.GetTempPath(), $"tikz-step3-{Unique}"),
            ["Tikz:Format"] = format ? "true" : "false",
        };
        foreach (var (k, v) in extra ?? new()) settings[k] = v;
        return new TikzFigureService(new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<TikzFigureService>.Instance);
    }

    /// <summary>Every rgb(…%) colour in an SVG (pdftocairo writes fills and strokes that way).</summary>
    private static List<(double R, double G, double B)> SvgColours(byte[] svg) =>
        Regex.Matches(Encoding.UTF8.GetString(svg), @"rgb\(\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)")
            .Select(m => (P(m.Groups[1].Value), P(m.Groups[2].Value), P(m.Groups[3].Value)))
            .ToList();

    private static double P(string s) => double.Parse(s, CultureInfo.InvariantCulture) / 100.0;

    /// <summary>Within one step of 255 per channel (xcolor mixes in TeX's fixed point).</summary>
    private static bool Near((double R, double G, double B) c, string hex)
    {
        double Ch(int i) => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
        return Math.Abs(c.R - Ch(0)) < 0.006 && Math.Abs(c.G - Ch(1)) < 0.006 && Math.Abs(c.B - Ch(2)) < 0.006;
    }

    // One square per name, each filled with it: the drawing's fills are the names' colours.
    private static string EveryName() =>
        "\\begin{tikzpicture} % " + Unique + "\n"
        + string.Join("\n", FigureColours.Names.Select((n, i) => $"  \\fill[{n}] ({i},0) rectangle ++(0.8,0.8);"))
        + "\n\\end{tikzpicture}";

    // ── The names ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\fill[lilia-accent-soft] (0,0) circle (1);", true)]
    [InlineData(@"\draw[color=lilia-seq8!50] (0,0) -- (1,1);", true)]
    [InlineData(@"\node[text=lilia-chapter] {x};", true)]
    [InlineData(@"\usepackage{lilia-theme}", false)]
    [InlineData(@"\fill[lilia-seq9] (0,0) circle (1);", false)]
    [InlineData(@"\fill[red!50] (0,0) circle (1);", false)]
    [InlineData(@"\fill[mylilia-ink] (0,0) circle (1);", false)]
    public void A_name_is_recognised_only_as_one_of_the_colour_names(string text, bool uses) =>
        FigureColours.Uses(text).Should().Be(uses);

    [Fact]
    public void Every_theme_resolves_every_name_and_Classic_is_ink_and_greys()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var map = FigureColours.Resolve(theme.Id, whitePaper: false);
            map.Keys.Should().Equal(FigureColours.Names, theme.Id);
            map[FigureColours.Paper].Should().Be(theme.Colours.Paper.ToUpperInvariant());
            map[FigureColours.Ink].Should().Be(theme.Colours.Ink.ToUpperInvariant());
            map[FigureColours.Chapter].Should().Be(map[FigureColours.Accent], "the chapter is the accent outside an Index figure");
            map[FigureColours.AccentSoft].Should().Be(FigureColours.Mix(map[FigureColours.Accent], 18, map[FigureColours.Paper]));
            if (theme.Id != ThemeCatalog.Classic)
                Enumerable.Range(1, 8).Select(i => map[$"lilia-seq{i}"]).Should().Equal(ThemeCatalog.Sequence, "the Index sequence in every theme");
        }

        var classic = FigureColours.Resolve(ThemeCatalog.Classic, false);
        classic.Values.Should().OnlyContain(hex => hex.Substring(1, 2) == hex.Substring(3, 2) && hex.Substring(3, 2) == hex.Substring(5, 2), "Classic stays monochrome");
        FigureColours.Resolve("cerulean", false)[FigureColours.Accent].Should().Be("#0A76A4");
        FigureColours.Resolve("carnet", false)[FigureColours.Accent].Should().Be("#9A3F1C");
        FigureColours.Resolve("carnet", true)[FigureColours.Paper].Should().Be("#FFFFFF", "paper=white prints Carnet on white");
        FigureColours.Resolve("index", false)[FigureColours.AccentSoft].Should().Be("#DEE2EE");
    }

    // ── The managed line ────────────────────────────────────────────────

    [Fact]
    public void Classic_writes_its_colours_only_line_only_when_a_figure_names_a_colour()
    {
        var plain = Figure(@"\begin{tikzpicture}\fill[red] (0,0) circle (1);\end{tikzpicture}");
        var named = Figure(@"\begin{tikzpicture}\fill[lilia-accent] (0,0) circle (1);\end{tikzpicture}");

        LaTeXPreambleBuilder.BuildThemeLine(Doc(), [plain]).Should().BeEmpty("Classic with today's tables loads nothing");
        LaTeXPreambleBuilder.BuildThemeLine(Doc(), [plain, named]).Should().Contain(@"\usepackage[theme=classic]{lilia-theme}");
        var embed = B("embed", new { code = @"\textcolor{lilia-ink}{x}" });
        LaTeXPreambleBuilder.BuildThemeLine(Doc(), [embed]).Should().Contain(@"\usepackage[theme=classic]{lilia-theme}", "any source counts");
        var custom = Doc();
        custom.CustomPreamble = @"\tikzset{box/.style={fill=lilia-accent-soft}}";
        LaTeXPreambleBuilder.BuildThemeLine(custom, [plain]).Should().Contain(@"\usepackage[theme=classic]{lilia-theme}");

        // A class that sets its own look still gets the names, so the figure compiles.
        LaTeXPreambleBuilder.BuildThemeLine(Doc("index", "IEEEtran"), [named]).Should().Contain(@"\usepackage[theme=classic]{lilia-theme}");
        LaTeXPreambleBuilder.BuildThemeLine(Doc("index", "IEEEtran"), [plain]).Should().BeEmpty();

        // A themed document loads the package already: one line, no second one.
        var index = LaTeXPreambleBuilder.BuildThemeLine(Doc("index"), [named]);
        Regex.Matches(index, @"\{lilia-theme\}").Should().HaveCount(1);

        // A beamer deck: its theme's .sty, then the names in colours-only mode.
        var deck = LaTeXPreambleBuilder.BuildThemeLine(Doc("index", "beamer"), [named]);
        deck.Should().Contain(@"\usetheme{LiliaIndex}").And.Contain(@"\usepackage[theme=index, coloursonly]{lilia-theme}");
        LaTeXPreambleBuilder.BuildThemeLine(Doc("classic", "beamer"), [named]).Should().Contain(@"\usepackage[theme=classic]{lilia-theme}");
    }

    [Fact]
    public void Export_keeps_the_source_byte_for_byte_and_the_zip_carries_the_sty_under_Classic()
    {
        var source = "\\begin{tikzpicture}\n  \\fill[lilia-accent-soft] (0,0) rectangle (2,1); % soft\n\\end{tikzpicture}";
        var doc = Doc();
        var blocks = new List<Block> { B("paragraph", new { text = "Before." }), Figure(source) };
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions());
        tex.Should().Contain(source);
        ThemeCatalog.FilesUsedBy(tex).Select(f => f.FileName).Should().Contain(ThemeCatalog.StyFileName);
    }

    // ── Real compiles: every name under every theme ─────────────────────

    public static TheoryData<string> Themes() => new() { "classic", "cerulean", "index", "carnet", "gazette" };

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Every_name_draws_in_the_themes_colours_in_the_figures_own_compile(string theme)
    {
        var doc = Doc(theme);
        var figure = Figure(EveryName());
        doc.Blocks = [figure];
        var r = await Service().RenderAsync(doc, figure, "tests");
        r.Error.Should().BeNull(r.Error?.Message);

        var expected = FigureColours.Resolve(FigureColours.For(doc, null));
        var colours = SvgColours(r.Svg!);
        foreach (var (name, hex) in expected)
            colours.Should().Contain(c => Near(c, hex), $"{name} is {hex} under {theme}");
        if (theme == "classic")
            colours.Should().OnlyContain(c => Math.Abs(c.R - c.G) < 0.002 && Math.Abs(c.G - c.B) < 0.002, "Classic stays monochrome");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Every_name_compiles_in_the_full_document_and_literal_colours_never_change(string theme)
    {
        var doc = Doc(theme);
        var source = EveryName().Replace(@"\end{tikzpicture}", "  \\fill[red!50!blue] (20,0) rectangle ++(0.8,0.8);\n\\end{tikzpicture}");
        var blocks = new List<Block> { B("heading", new { text = "One", level = 1 }), B("paragraph", new { text = "Text." }), Figure(source) };
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions());
        TexSourceGuard.Violation(tex).Should().BeNull();
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);

        var fills = DocumentThemeCompileTests.FillColours(pdf);
        var expected = FigureColours.Resolve(theme, false,
            theme == "index" ? ThemeCatalog.Sequence[0] : null);   // Index: the figure is in chapter 1
        foreach (var (name, hex) in expected)
            fills.Should().Contain(c => Near(c, hex), $"{name} is {hex} under {theme}");
        fills.Should().Contain(c => Near(c, "#800080"), "red!50!blue is the same purple in every theme");
    }

    // ── lilia-chapter follows the figure (Index) ────────────────────────

    private static (Document Doc, List<Block> Blocks, Block[] Figures) IndexBook()
    {
        var one = B("heading", new { text = "One", level = 1 });
        var two = B("heading", new { text = "Two", level = 1 });
        // lilia-chapter!50: a tint no heading uses, so the full PDF shows the figure's own fill.
        var source = "\\begin{tikzpicture} % chapter\n  \\fill[lilia-chapter] (0,0) rectangle (2,1);\n  \\fill[lilia-chapter!50] (2,0) rectangle (4,1);\n\\end{tikzpicture}";
        var f1 = Figure(source);
        var f2 = Figure(source);
        var f3 = Figure(source);
        var blocks = new List<Block>
        {
            B("paragraph", new { text = "Front." }),
            one, B("paragraph", new { text = "In one." }), f1,
            two, B("paragraph", new { text = "In two." }), f2,
            B("heading", new { text = "Three", level = 1 }), B("paragraph", new { text = "In three." }),
            B("backMatter", new { subType = "appendix", text = "" }),
            B("heading", new { text = "Appendix A", level = 1 }), B("paragraph", new { text = "A." }),
            B("heading", new { text = "Appendix B", level = 1 }), B("paragraph", new { text = "B." }), f3,
        };
        // Chapter two pinned to the eighth colour.
        var doc = Doc("index", "article", new Dictionary<string, int> { [two.Id.ToString()] = 7 });
        doc.Blocks = blocks;
        return (doc, blocks, [f1, f2, f3]);
    }

    [Fact]
    public async Task Two_figures_in_different_Index_chapters_draw_in_their_chapters_colours_pins_and_appendix_included()
    {
        var (doc, blocks, figures) = IndexBook();
        var themes = TikzFigureThemes.From(doc, blocks);
        var expected = new[] { "#4A5FA3", "#2E6E9E", "#996300" };   // chapter 1; chapter 2 pinned to 8; appendix B restarts: second colour
        for (var i = 0; i < 3; i++)
            themes.For(figures[i].Id).Chapter.Should().Be(expected[i]);

        // The figures' own compiles (canvas, split view): the same source, three colours.
        var svc = Service();
        for (var i = 0; i < 3; i++)
        {
            var r = await svc.RenderAsync(doc, figures[i], "tests", default, themes.For(figures[i].Id));
            r.Error.Should().BeNull(r.Error?.Message);
            r.Cached.Should().BeFalse("each chapter colour is its own drawing");
            SvgColours(r.Svg!).Should().Contain(c => Near(c, expected[i]));
            foreach (var other in expected.Where(e => e != expected[i]))
                SvgColours(r.Svg!).Should().NotContain(c => Near(c, other));
        }

        // The full document: the .sty sets lilia-chapter per chapter.
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions());
        tex.Should().Contain(@"\liliaPinColour{2}{7}");
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);
        var fills = DocumentThemeCompileTests.FillColours(pdf);
        foreach (var hex in expected) fills.Should().Contain(c => Near(c, FigureColours.Mix(hex, 50, "#FFFFFF")), $"the figure under {hex}");
        fills.Should().NotContain(c => Near(c, FigureColours.Mix("#2F2E2C", 50, "#FFFFFF")), "no figure is drawn in neutral ink");

        // The colours endpoint's map: the chapter colour, the rest the theme's.
        var map = FigureColours.Resolve(themes.For(figures[1].Id));
        map[FigureColours.Chapter].Should().Be("#2E6E9E");
        map[FigureColours.Accent].Should().Be("#4A5FA3");

        // Before the first chapter: neutral ink.
        var front = Figure("\\begin{tikzpicture}\\fill[lilia-chapter] (0,0) circle (1);\\end{tikzpicture}");
        blocks.Insert(0, front);
        TikzFigureThemes.From(doc, blocks).For(front.Id).Chapter.Should().Be("#2F2E2C");
    }

    [Fact]
    public void The_cache_key_changes_with_the_chapter_only_for_a_figure_that_names_a_theme_colour()
    {
        var doc = Doc("index");
        var red = new FigureTheme("index", false, "#4A5FA3");
        var gold = new FigureTheme("index", false, "#996300");
        string Key(string source, FigureTheme t)
        {
            var full = TikzFigureService.BuildStandalone(doc, source, true, out _, TikzFigureService.NamesThemeColours(doc, source) ? t : null);
            return TikzFigureService.Hash(source, full[..full.IndexOf(@"\begin{document}", StringComparison.Ordinal)], "pdflatex",
                TikzFigureService.NamesThemeColours(doc, source) ? t.Key : "");
        }
        var named = @"\begin{tikzpicture}\fill[lilia-chapter] (0,0) circle (1);\end{tikzpicture}";
        var literal = @"\begin{tikzpicture}\fill[red] (0,0) circle (1);\end{tikzpicture}";
        Key(named, red).Should().NotBe(Key(named, gold));
        Key(named, red).Should().NotBe(Key(named, red with { Theme = "cerulean" }));
        Key(literal, red).Should().Be(Key(literal, gold), "a figure without theme colours is drawn the same everywhere");
        red.Key.Should().Contain("index").And.Contain("#4A5FA3");
    }

    [Fact]
    public async Task Moving_a_figure_to_another_chapter_redraws_it_and_moving_it_back_is_a_cache_hit()
    {
        var svc = Service();
        var doc = Doc("index");
        var figure = Figure("\\begin{tikzpicture} % " + Unique + "\n\\fill[lilia-chapter] (0,0) circle (1);\n\\end{tikzpicture}");
        var first = new FigureTheme("index", false, "#4A5FA3");
        var second = new FigureTheme("index", false, "#B8303A");
        (await svc.RenderAsync(doc, figure, "tests", default, first)).Cached.Should().BeFalse();
        var moved = await svc.RenderAsync(doc, figure, "tests", default, second);
        moved.Cached.Should().BeFalse();
        SvgColours(moved.Svg!).Should().Contain(c => Near(c, "#B8303A"));
        (await svc.RenderAsync(doc, figure, "tests", default, first)).Cached.Should().BeTrue();
    }

    // ── Drafts ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_draft_shares_the_cache_and_never_touches_the_last_good_drawing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tikz-step3-{Unique}");
        var svc = Service(dir);
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var saved = "\\begin{tikzpicture} % " + Unique + "\n\\draw (0,0) -- (1,1);\n\\end{tikzpicture}";
        var block = Figure(saved);

        // A draft before anything was saved: drawn, but no last good drawing.
        var draft = await svc.DraftAsync(doc, block, saved.Replace("(1,1)", "(2,1)"), "tests");
        draft.Ok.Should().BeTrue();
        svc.HasLastGood(block.Id).Should().BeFalse();

        var drawn = await svc.RenderAsync(doc, block, "tests");
        drawn.Ok.Should().BeTrue();
        var lastGood = svc.LastGood(block.Id)!;

        // The saved source as a draft: a cache hit.
        var same = await svc.DraftAsync(doc, block, saved, "tests");
        same.Cached.Should().BeTrue();
        same.Svg.Should().Equal(drawn.Svg);

        // Another good draft, then a broken one: the last good drawing is still the saved one.
        (await svc.DraftAsync(doc, block, saved.Replace("(1,1)", "(3,1)"), "tests")).Ok.Should().BeTrue();
        var broken = await svc.DraftAsync(doc, block, saved.Replace("(1,1)", @"\oops (1,1)"), "tests");
        broken.Error!.Kind.Should().Be("tex");
        broken.Error.Line.Should().Be(2);
        svc.LastGood(block.Id).Should().Equal(lastGood);
        try { Directory.Delete(dir, true); } catch { }
    }

    // About seven seconds of pgfplots work: long enough to cancel in the middle.
    private static string Heavy() =>
        "\\begin{tikzpicture} % " + Unique + "\n\\begin{axis}\\addplot[samples=4000,domain=0:10]{sin(deg(x))*exp(-x/5)};\\end{axis}\n\\end{tikzpicture}";

    [Fact]
    public async Task An_aborted_draft_kills_its_compile_and_caches_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tikz-step3-{Unique}");
        var svc = Service(dir);
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var block = Figure(@"\begin{tikzpicture}\draw (0,0) -- (1,1);\end{tikzpicture}");
        var heavy = Heavy();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var act = () => svc.DraftAsync(doc, block, heavy, "tests", null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.5), "the compile stops when the request does");

        // Nothing was cached: neither a drawing nor an error.
        (Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) : [])
            .Should().NotContain(f => f.EndsWith(".svg") || f.EndsWith(".err.json"));
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public async Task A_newer_draft_of_the_same_figure_cancels_the_older_one()
    {
        var svc = Service();
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var block = Figure(@"\begin{tikzpicture}\draw (0,0) -- (1,1);\end{tikzpicture}");
        var older = svc.DraftAsync(doc, block, Heavy(), "typist");
        await Task.Delay(400);
        var newer = await svc.DraftAsync(doc, block, "\\begin{tikzpicture} % " + Unique + "\n\\draw (0,0) -- (2,2);\n\\end{tikzpicture}", "typist");
        newer.Ok.Should().BeTrue();
        var act = () => older;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Another person's draft of the same figure is theirs: not cancelled.
        var mine = svc.DraftAsync(doc, block, "\\begin{tikzpicture} % " + Unique + "\n\\draw (0,0) -- (3,3);\n\\end{tikzpicture}", "first");
        var theirs = await svc.DraftAsync(doc, block, "\\begin{tikzpicture} % " + Unique + "\n\\draw (0,0) -- (4,4);\n\\end{tikzpicture}", "second");
        theirs.Ok.Should().BeTrue();
        (await mine).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Drafts_have_their_own_budget()
    {
        var svc = Service(extra: new() { ["Tikz:DraftBurst"] = "1", ["Tikz:DraftsPerMinute"] = "1" });
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var block = Figure(@"\begin{tikzpicture}\draw (0,0) -- (1,1);\end{tikzpicture}");
        var who = "budget-" + Unique;
        (await svc.DraftAsync(doc, block, $"\\begin{{tikzpicture}} % {Unique}\n\\draw (0,0) -- (1,1);\n\\end{{tikzpicture}}", who)).Ok.Should().BeTrue();
        var refused = await svc.DraftAsync(doc, block, $"\\begin{{tikzpicture}} % {Unique}\n\\draw (0,0) -- (1,1);\n\\end{{tikzpicture}}", who);
        refused.Error!.Kind.Should().Be("budget");
        // The saved figure's budget is separate.
        (await svc.RenderAsync(doc, Figure($"\\begin{{tikzpicture}} % {Unique}\n\\draw (0,0) -- (1,1);\n\\end{{tikzpicture}}"), who)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task The_process_runner_kills_a_cancelled_process()
    {
        var dir = Directory.CreateTempSubdirectory("lilia-runner-").FullName;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var act = () => TexProcessRunner.RunAsync("sleep", "30", dir, 60, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        var timeout = () => TexProcessRunner.RunAsync("sleep", "30", dir, 1);
        await timeout.Should().ThrowAsync<TimeoutException>("a timeout is still a timeout");
        Directory.Delete(dir, true);
    }

    // ── The precompiled format ──────────────────────────────────────────

    [Fact]
    public async Task The_format_draws_the_same_picture_and_reports_the_same_error_line()
    {
        var latex = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var good = "\\begin{tikzpicture}\n\\node[draw] (a) {A};\n\\draw[->] (a) -- (2,0);\n\\end{tikzpicture}";
        var full = TikzFigureService.BuildStandalone(doc, good, true, out var start);
        var dump = TikzFigureService.DumpPart(full);
        dump.Should().Contain(@"\usepackage{tikz}").And.NotContain(@"\begin{document}");

        var fmt = Path.Combine(Path.GetTempPath(), $"tikz-fmt-{Unique}", "test");
        (await latex.BuildFormatAsync(dump, "pdflatex", fmt)).Should().BeTrue();
        File.Exists(fmt + ".fmt").Should().BeTrue();

        var with = await latex.CompileStandaloneSvgAsync(full, "pdflatex", 20, default, fmt);
        var without = await latex.CompileStandaloneSvgAsync(full, "pdflatex", 20);
        with.UsedFormat.Should().BeTrue();
        with.FormatRejected.Should().BeFalse();
        without.UsedFormat.Should().BeFalse();
        Encoding.UTF8.GetString(with.Svg!).Should().Be(Encoding.UTF8.GetString(without.Svg!), "the format changes the speed, not the drawing");

        var bad = good.Replace("(2,0)", @"\nosuch (2,0)");
        var badFull = TikzFigureService.BuildStandalone(doc, bad, true, out var badStart);
        TikzFigureService.DumpPart(badFull).Should().Be(dump, "the same packages: the same format");
        var e1 = TikzErrors.FromLog((await latex.CompileStandaloneSvgAsync(badFull, "pdflatex", 20, default, fmt)).Log, bad, badStart);
        var e2 = TikzErrors.FromLog((await latex.CompileStandaloneSvgAsync(badFull, "pdflatex", 20)).Log, bad, badStart);
        e1.Should().BeEquivalentTo(e2);
        e1.Line.Should().Be(3);
        Directory.Delete(Path.GetDirectoryName(fmt)!, true);
    }

    [Fact]
    public async Task A_bad_format_falls_back_to_the_normal_compile_and_a_failed_build_says_so()
    {
        var latex = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var full = TikzFigureService.BuildStandalone(doc, @"\begin{tikzpicture}\draw (0,0) -- (1,1);\end{tikzpicture}", true, out _);

        var dir = Directory.CreateTempSubdirectory("tikz-fmt-").FullName;
        var garbage = Path.Combine(dir, "garbage");
        await File.WriteAllTextAsync(garbage + ".fmt", "not a format");
        var r = await latex.CompileStandaloneSvgAsync(full, "pdflatex", 20, default, garbage);
        r.FormatRejected.Should().BeTrue();
        r.UsedFormat.Should().BeFalse();
        r.Svg.Should().NotBeNull("drawn the normal way");

        (await latex.BuildFormatAsync(@"\documentclass{standalone}\usepackage{no-such-package-anywhere}", "pdflatex", Path.Combine(dir, "broken")))
            .Should().BeFalse();
        File.Exists(Path.Combine(dir, "broken.fmt")).Should().BeFalse();
        (await latex.BuildFormatAsync(TikzFigureService.DumpPart(full), "lualatex", Path.Combine(dir, "lua"))).Should().BeFalse("pdflatex only");

        // Through the service: a planted bad format is rejected, removed, and the figure still draws.
        var cache = Path.Combine(dir, "cache");
        var svc = Service(cache, format: true);
        var path = svc.FormatPath(TikzFigureService.DumpPart(full), "pdflatex");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path + ".fmt", "not a format either");
        var figure = Figure("\\begin{tikzpicture} % " + Unique + "\n\\draw (0,0) -- (1,1);\n\\end{tikzpicture}");
        var drawn = await svc.RenderAsync(doc, figure, "tests");
        drawn.Ok.Should().BeTrue(drawn.Error?.Message);
        File.Exists(path + ".fmt").Should().BeFalse("a rejected format is dropped");
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task The_service_builds_the_format_once_and_draws_with_it()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"tikz-step3-{Unique}");
        var svc = Service(cache, format: true);
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var source = "\\begin{tikzpicture} % " + Unique + "\n\\begin{axis}\\addplot[samples=50]{x^2};\\end{axis}\n\\end{tikzpicture}";
        var full = TikzFigureService.BuildStandalone(doc, source, true, out _);
        TikzFigureService.DumpPart(full).Should().Contain(@"\usepackage{pgfplots}", "pgfplots goes in the format when the picture uses it");

        (await svc.EnsureFormatAsync(full, "pdflatex")).Should().BeTrue();
        svc.FormatFor(full, "pdflatex").Should().NotBeNull();
        var r = await svc.RenderAsync(doc, Figure(source), "tests");
        r.Ok.Should().BeTrue(r.Error?.Message);

        // Off by configuration: no format.
        Service(cache, format: false).FormatFor(full, "pdflatex").Should().BeNull();
        // xelatex: the normal compile.
        svc.FormatFor(full, "xelatex").Should().BeNull();

        // The prune never removes a format.
        var pruning = Service(cache, format: true, extra: new() { ["Tikz:CacheMaxMb"] = "0" });
        pruning.Prune();
        svc.FormatFor(full, "pdflatex").Should().NotBeNull();
        try { Directory.Delete(cache, true); } catch { }
    }
}
