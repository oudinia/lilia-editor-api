using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// TikZ figures, step 1 (7 Oct 2026): a tikzpicture is a figure whose drawing is source.
/// Import → <c>{ kind: "tikz", source, caption, label, … }</c>; both LaTeX emitters write the
/// source back byte for byte; the importer, the exporter and the preamble agree on the
/// libraries; the source compiles on its own into an SVG; Typst places that SVG.
/// </summary>
public class TikzFigureTests
{
    // ── helpers ─────────────────────────────────────────────────────────

    internal static async Task<Document> ImportAsync(string tex)
    {
        var parsed = await new LatexParser().ParseTextAsync(tex);
        var mapped = LatexImportJobExecutor.MapElements(parsed.Elements);
        var pre = LatexPreambleExtractor.Extract(tex);
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = "tikz-tests",
            Title = string.IsNullOrEmpty(parsed.Title) ? "TikZ" : parsed.Title,
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 12,
            Columns = 1,
            LatexPackages = pre.PackagesJson,
            CustomPreamble = pre.CustomPreamble,
        };
        doc.Blocks = mapped.Select((m, i) => new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = m.type,
            Content = JsonDocument.Parse(JsonSerializer.Serialize(m.content)),
            SortOrder = i,
        }).ToList();
        return doc;
    }

    private static Block Figures(Document doc) => doc.Blocks.Single(b => b.Type == "figure");

    private static Block Block(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static string Export(Document doc) =>
        new LaTeXExportService(context: null!, storageService: null!, logger: NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());

    private static string Preview(Document doc) =>
        new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    internal static string CorpusDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "tikz-corpus");

    public static IEnumerable<object[]> CorpusFiles() =>
        Directory.Exists(CorpusDir)
            ? Directory.EnumerateFiles(CorpusDir, "*.tex").OrderBy(p => p).Select(p => new object[] { Path.GetFileName(p) })
            : [];

    private const string IntroFigure = """
        \documentclass{article}
        \usepackage{tikz}
        \begin{document}
        Text before.

        \begin{figure}
            \centering
            \begin{tikzpicture}

                % (x, y): x horizontal; every instruction ends with ;
                \draw (0, 0) -- (2, 0) -- (2, 2) -- (0, 2) -- (0, 0);
                \draw (0, 2) -- (1, 3) -- (2, 2); % the roof

            \end{tikzpicture}
            \caption{Our first TikZ figure}
            \label{fig:first_tikz}
        \end{figure}

        Text after.
        \end{document}
        """;

    // The picture as stored: verbatim, comments included, at the left margin.
    private const string IntroSource = """
        \begin{tikzpicture}

            % (x, y): x horizontal; every instruction ends with ;
            \draw (0, 0) -- (2, 0) -- (2, 2) -- (0, 2) -- (0, 0);
            \draw (0, 2) -- (1, 3) -- (2, 2); % the roof

        \end{tikzpicture}
        """;

    // ── import ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_picture_in_a_figure_imports_as_one_tikz_figure_with_its_caption_and_label()
    {
        var doc = await ImportAsync(IntroFigure);

        doc.Blocks.Should().NotContain(b => b.Type == "code", "a picture is a figure, not a code block");
        var c = Figures(doc).Content.RootElement;
        c.GetProperty("kind").GetString().Should().Be("tikz");
        c.GetProperty("source").GetString().Should().Be(IntroSource);
        c.GetProperty("caption").GetString().Should().Be("Our first TikZ figure");
        c.GetProperty("label").GetString().Should().Be("fig:first_tikz");
        c.TryGetProperty("position", out _).Should().BeFalse("\\centering is the default");
        c.GetProperty("placement").GetString().Should().Be("auto", "no specifier: LaTeX placed it, so [htbp] rather than the [H] an absent placement prints");
        c.TryGetProperty("float", out _).Should().BeFalse();
        doc.Blocks.Where(b => b.Type == "paragraph").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("[H]", "here")]
    [InlineData("[t]", "top")]
    [InlineData("[!b]", "bottom")]
    [InlineData("[p]", "page")]
    public async Task The_float_specifier_becomes_the_placement(string spec, string placement)
    {
        var doc = await ImportAsync($"\\begin{{figure}}{spec}\\centering\\begin{{tikzpicture}}\\draw (0,0)--(1,1);\\end{{tikzpicture}}\\caption{{C}}\\end{{figure}}");
        Figures(doc).Content.RootElement.GetProperty("placement").GetString().Should().Be(placement);
    }

    [Fact]
    public async Task Alignment_starred_figures_and_a_resizebox_are_kept()
    {
        var doc = await ImportAsync("""
            \begin{figure*}[h]
              \raggedleft
              \resizebox{0.5\linewidth}{!}{\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}}
              \caption{Wide}
            \end{figure*}
            """);
        var c = Figures(doc).Content.RootElement;
        c.GetProperty("position").GetString().Should().Be("right");
        c.GetProperty("span").GetString().Should().Be("page");
        c.GetProperty("placement").GetString().Should().Be("auto", "[h] is not a single clear choice");
        c.GetProperty("source").GetString().Should().Be(@"\resizebox{0.5\linewidth}{!}{\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}}");
    }

    [Fact]
    public async Task A_figure_without_an_alignment_command_is_left_aligned_as_in_the_original()
    {
        var doc = await ImportAsync(@"\begin{figure}\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}\caption{C}\end{figure}");
        Figures(doc).Content.RootElement.GetProperty("position").GetString().Should().Be("left");
    }

    [Fact]
    public async Task Subfigure_captions_stay_in_the_source_and_the_figure_caption_is_the_outer_one()
    {
        var doc = await ImportAsync("""
            \begin{figure}
            \centering
            \begin{subfigure}{0.45\textwidth}\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}\caption{Left}\label{fig:l}\end{subfigure}
            \hfill
            \begin{subfigure}{0.45\textwidth}\begin{tikzpicture}\draw (0,0) circle (1);\end{tikzpicture}\caption{Right}\end{subfigure}
            \caption{Both}
            \label{fig:both}
            \end{figure}
            """);
        var c = Figures(doc).Content.RootElement;
        c.GetProperty("caption").GetString().Should().Be("Both");
        c.GetProperty("label").GetString().Should().Be("fig:both");
        var source = c.GetProperty("source").GetString()!;
        source.Should().Contain(@"\caption{Left}\label{fig:l}").And.Contain(@"\caption{Right}").And.Contain(@"\hfill");
        source.Should().NotContain("Both");
    }

    [Fact]
    public async Task A_bare_picture_is_a_figure_without_a_float_caption_or_number()
    {
        var doc = await ImportAsync("""
            Before.

            \begin{tikzpicture}
              \draw (0,0) -- (1,1); % diagonal
            \end{tikzpicture}

            After.
            """);
        var c = Figures(doc).Content.RootElement;
        c.GetProperty("kind").GetString().Should().Be("tikz");
        c.GetProperty("float").GetBoolean().Should().BeFalse();
        c.GetProperty("caption").GetString().Should().BeEmpty();
        c.GetProperty("source").GetString().Should().Be("\\begin{tikzpicture}\n  \\draw (0,0) -- (1,1); % diagonal\n\\end{tikzpicture}");
        doc.Blocks.Select(b => b.Type).Should().Equal("paragraph", "figure", "paragraph");
    }

    [Fact]
    public async Task A_commutative_diagram_in_display_math_is_a_bare_figure_with_its_math()
    {
        var doc = await ImportAsync(@"\[ \begin{tikzcd} A \arrow[r] & B \end{tikzcd} \]");
        var c = Figures(doc).Content.RootElement;
        c.GetProperty("source").GetString().Should().Be(@"\[ \begin{tikzcd} A \arrow[r] & B \end{tikzcd} \]");
        c.GetProperty("float").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_picture_inside_an_equation_or_a_listing_stays_where_it_was_verbatim()
    {
        var parsed = await new LatexParser().ParseTextAsync("""
            \begin{equation}
            \begin{tikzcd} A \arrow[r] & B % arrow
            \end{tikzcd}
            \end{equation}

            \begin{lstlisting}
            \begin{tikzpicture} \draw (0,0)--(1,1); \end{tikzpicture}
            \end{lstlisting}
            """);
        parsed.Elements.OfType<ImportEquation>().Single().LatexContent.Should().Contain(@"\begin{tikzcd} A \arrow[r] & B % arrow");
        parsed.Elements.OfType<ImportCodeBlock>().Single().Text.Should().Contain(@"\begin{tikzpicture} \draw (0,0)--(1,1); \end{tikzpicture}");
        parsed.Elements.OfType<ImportTikzFigure>().Should().BeEmpty();
        parsed.Elements.SelectMany(e => new[] { (e as ImportEquation)?.LatexContent, (e as ImportCodeBlock)?.Text })
            .Where(t => t is not null).Should().NotContain(t => t!.Contains('\uE0F0'), "no placeholder may leak");
    }

    [Fact]
    public async Task A_commented_out_picture_is_not_a_figure()
    {
        var doc = await ImportAsync("Text.\n% \\begin{tikzpicture}\\draw (0,0)--(1,1);\\end{tikzpicture}\nMore.");
        doc.Blocks.Should().NotContain(b => b.Type == "figure");
    }

    [Fact]
    public async Task Tikz_setup_lines_are_kept_in_the_custom_preamble_and_leave_the_body()
    {
        const string tex = """
            \documentclass{article}
            \usepackage{tikz}
            \usetikzlibrary{arrows.meta, positioning}
            % \usetikzlibrary{shadows}
            \tikzset{box/.style={draw, fill={blue!10}}}
            \tikzstyle{dot}=[circle, fill=black, inner sep={1pt}]
            \newcommand{\R}{\mathbb{R}}
            \begin{document}
            \pgfplotsset{width=6cm}
            \begin{figure}\centering
            \begin{tikzpicture}\def\r{2}\tikzset{local/.style=red}\node[box] {$\R$};\end{tikzpicture}
            \caption{C}\end{figure}
            \end{document}
            """;
        var doc = await ImportAsync(tex);

        doc.CustomPreamble.Should().Contain(@"\usetikzlibrary{arrows.meta, positioning}")
            .And.Contain(@"\tikzset{box/.style={draw, fill={blue!10}}}")
            .And.Contain(@"\tikzstyle{dot}=[circle, fill=black, inner sep={1pt}]")
            .And.Contain(@"\pgfplotsset{width=6cm}")
            .And.Contain(@"\newcommand{\R}{\mathbb{R}}");
        doc.CustomPreamble.Should().NotContain("shadows", "a commented-out library stays out");
        doc.CustomPreamble.Should().NotContain(@"\def\r", "a definition inside a picture is local to it");
        doc.CustomPreamble.Should().NotContain("local/.style");
        doc.Blocks.Should().NotContain(b => b.Type == "paragraph" && b.Content.RootElement.GetRawText().Contains("pgfplotsset"));
        Figures(doc).Content.RootElement.GetProperty("source").GetString().Should().Contain(@"\def\r{2}\tikzset{local/.style=red}");
    }

    [Fact]
    public async Task Tikz_and_pgfplots_no_longer_warn_limited_support()
    {
        var parsed = await new LatexParser().ParseTextAsync("\\documentclass{article}\\usepackage{tikz}\\usepackage{pgfplots}\\begin{document}x\\end{document}");
        parsed.Warnings.Should().NotContain(w => w.Message.Contains("limited editor support"));
    }

    // ── export ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Both_emitters_write_the_source_byte_for_byte_inside_the_figure()
    {
        var doc = await ImportAsync(IntroFigure);

        var expectedPreview = "\\begin{figure}[htbp]\n\\centering\n" + IntroSource + "\n\\caption{Our first TikZ figure}\n\\label{fig:first_tikz}\n\\end{figure}";
        Preview(doc).Should().Contain(expectedPreview);

        var export = Export(doc);
        export.Should().Contain("\\begin{figure}[htbp]\n\\centering\n" + IntroSource + "\n\\caption{Our first TikZ figure}\n\\label{");
        export.Should().Contain(IntroSource);
    }

    [Fact]
    public void Without_a_placement_a_tikz_figure_prints_H_like_image_figures_and_without_a_caption_it_is_bare()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T",
            Blocks =
            [
                Block("figure", new { kind = "tikz", source = Src, caption = "Drawn.", label = "" }),
                Block("figure", new { kind = "tikz", source = @"\begin{tikzpicture}\fill (0,0) circle (1pt);\end{tikzpicture}", caption = "", label = "" }),
            ],
        };
        foreach (var latex in new[] { Preview(doc), Export(doc) })
        {
            latex.Should().Contain("\\begin{figure}[H]\n\\centering\n" + Src + "\n\\caption{Drawn.}");
            latex.Should().Contain(@"\begin{tikzpicture}\fill (0,0) circle (1pt);\end{tikzpicture}");
            System.Text.RegularExpressions.Regex.Matches(latex, @"\\begin\{figure\}").Count.Should().Be(1, "the captionless one is bare");
        }
    }

    [Fact]
    public async Task A_bare_picture_is_written_without_a_float()
    {
        var doc = await ImportAsync("Before.\n\n\\begin{tikzpicture}\\draw (0,0)--(1,1);\\end{tikzpicture}\n\nAfter.");
        foreach (var latex in new[] { Preview(doc), Export(doc) })
        {
            latex.Should().Contain(@"\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}");
            latex.Should().NotContain(@"\begin{figure}");
        }
    }

    [Fact]
    public async Task Round_trip_gives_back_the_same_source_caption_and_label()
    {
        var first = await ImportAsync(IntroFigure);
        var again = await ImportAsync(Export(first));
        var a = Figures(first).Content.RootElement;
        var b = Figures(again).Content.RootElement;
        b.GetProperty("source").GetString().Should().Be(a.GetProperty("source").GetString());
        b.GetProperty("caption").GetString().Should().Be(a.GetProperty("caption").GetString());

        var viaPreview = await ImportAsync(Preview(first));
        Figures(viaPreview).Content.RootElement.GetProperty("source").GetString().Should().Be(IntroSource);
        Figures(viaPreview).Content.RootElement.GetProperty("label").GetString().Should().Be("fig:first_tikz");
    }

    [Fact]
    public void Tikz_is_loaded_only_by_a_document_that_has_tikz_figures()
    {
        var plain = new Document { Id = Guid.NewGuid(), Title = "Plain", Blocks = [Block("paragraph", new { text = "x" })] };
        Preview(plain).Should().NotContain(@"\usepackage{tikz}");
        Export(plain).Should().NotContain(@"\usepackage{tikz}");

        var withTikz = new Document
        {
            Id = Guid.NewGuid(), Title = "Drawn",
            Blocks =
            [
                Block("figure", new { kind = "tikz", source = @"\begin{tikzpicture}\begin{axis}\addplot coordinates {(0,0) (1,1)};\end{axis}\end{tikzpicture}", caption = "Plot", label = "" }),
                Block("figure", new { kind = "tikz", source = @"\begin{tikzcd} A \arrow[r] & B \end{tikzcd}", caption = "", label = "", @float = false }),
            ],
        };
        foreach (var latex in new[] { Preview(withTikz), Export(withTikz) })
        {
            latex.Should().Contain(@"\usepackage{tikz}").And.Contain(@"\usepackage{pgfplots}").And.Contain(@"\usepackage{tikz-cd}");
            latex.Should().Contain(@"\pgfplotsset{compat=1.18}");
            latex.IndexOf(@"\usepackage{tikz}", StringComparison.Ordinal)
                .Should().BeGreaterThan(latex.IndexOf("{xcolor}", StringComparison.Ordinal), "after xcolor and its options");
        }

        // An imported document that loads tikz itself does not get it twice.
        withTikz.LatexPackages = """[{"name":"tikz"},{"name":"pgfplots"},{"name":"tikz-cd"}]""";
        Preview(withTikz).Should().NotContain("% TikZ figures");
    }

    // ── preamble routing, LML, normaliser ───────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("\\usetikzlibrary{arrows}\n% a comment\n\\tikzset{x/.style={red}}\n\\pgfplotsset{compat=1.18}", true)]
    [InlineData("\\usetikzlibrary{arrows}\n\\newcommand{\\R}{\\mathbb{R}}", false)]
    public void Tikz_setup_alone_does_not_send_a_document_to_LaTeX(string? preamble, bool typstOk)
    {
        var d = new Document { Id = Guid.NewGuid(), Title = "T", CustomPreamble = preamble };
        (PageSetupRouting.WhyLatex(d) is null).Should().Be(typstOk);
    }

    [Fact]
    public void LML_carries_a_tikz_figure_both_ways_and_reads_the_figure_skill_shape()
    {
        var block = Block("figure", new { kind = "tikz", source = "\\begin{tikzpicture}\n  \\draw (0,0) -- (1,1);\n\\end{tikzpicture}", caption = "A line.", label = "fig:line" });
        var lml = new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLml(block);
        var parsed = new LmlTextParser().Parse(lml).Blocks.Single();
        var content = JsonSerializer.SerializeToElement(parsed.Content);
        content.GetProperty("kind").GetString().Should().Be("tikz");
        content.GetProperty("source").GetString().Should().Be("\\begin{tikzpicture}\n  \\draw (0,0) -- (1,1);\n\\end{tikzpicture}");
        content.GetProperty("caption").GetString().Should().Be("A line.");

        // What the lilia-figure skill writes: the picture as the body of @figure.
        var skill = new LmlTextParser().Parse("@figure[label=\"fig:arch\", caption=\"A three-layer MLP.\"]\n  \\begin{tikzpicture}\n    \\node {x};\n  \\end{tikzpicture}").Blocks.Single();
        var sc = JsonSerializer.SerializeToElement(skill.Content);
        sc.GetProperty("kind").GetString().Should().Be("tikz");
        sc.GetProperty("caption").GetString().Should().Be("A three-layer MLP.");
        sc.GetProperty("source").GetString().Should().StartWith("\\begin{tikzpicture}");
    }

    [Fact]
    public void A_figure_given_tikz_source_without_a_kind_is_stored_as_a_tikz_figure()
    {
        var content = JsonDocument.Parse("""{"source":"\\begin{tikzpicture}\\draw (0,0)--(1,1);\\end{tikzpicture}","caption":"C"}""").RootElement;
        var normalised = BlockContentNormaliser.Normalise("figure", content).RootElement;
        normalised.GetProperty("kind").GetString().Should().Be("tikz");
        BlockContentNormaliser.Normalise("figure", JsonDocument.Parse("""{"src":"a.png"}""").RootElement)
            .RootElement.TryGetProperty("kind", out _).Should().BeFalse();
    }

    // ── Typst ───────────────────────────────────────────────────────────

    private static string Typst(Dictionary<string, TikzStaged>? staged, params Block[] blocks) =>
        new TypstExportService().BuildTypstDocument(
            new Document { Id = Guid.NewGuid(), Title = "T", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11, Columns = 1 },
            [.. blocks], null, new TypstExportOptions { TikzFigures = staged });

    private const string Src = @"\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}";

    [Fact]
    public async Task Typst_places_the_drawn_svg_with_its_caption()
    {
        var svg = await DrawAsync(Src);
        var staged = new Dictionary<string, TikzStaged> { [Src] = new("figures/tikz-a.svg", null) };
        var typst = Typst(staged, Block("figure", new { kind = "tikz", source = Src, caption = "A diagonal.", label = "" }));
        typst.Should().Contain("#figure(image(\"figures/tikz-a.svg\"), caption: [A diagonal.])");

        var result = await new TypstCompileService().CompileAsync(typst, TypstOutputFormat.Pdf, null,
            new Dictionary<string, byte[]> { ["figures/tikz-a.svg"] = svg });
        result.Success.Should().BeTrue(result.Error ?? "");

        var bare = Typst(staged, Block("figure", new { kind = "tikz", source = Src, caption = "", label = "", @float = false }));
        bare.Should().Contain("#image(\"figures/tikz-a.svg\")").And.NotContain("#figure(image(\"figures/tikz-a");
    }

    [Fact]
    public void A_figure_that_does_not_draw_is_a_visible_placeholder_and_the_preview_still_compiles()
    {
        var staged = new Dictionary<string, TikzStaged> { [Src] = new(null, "Undefined control sequence \\foo. \"quoted\" [#]") };
        var typst = Typst(staged,
            Block("paragraph", new { text = "Before." }),
            Block("figure", new { kind = "tikz", source = Src, caption = "Broken.", label = "" }));
        typst.Should().Contain("This TikZ figure doesn't compile: Undefined control sequence");
        var compiled = Lilia.Api.Tests.Typst.TypstHarness.CompileToText(typst);
        compiled.Text.Should().NotBeNull(compiled.Error);
        compiled.Text.Should().Contain("This TikZ figure doesn").And.Contain("Broken.").And.Contain("Before.");
    }

    [Fact]
    public void A_missing_bracket_noticed_at_a_blank_line_points_at_the_line_with_the_mistake()
    {
        // TeX names the blank line after the unclosed plot (it noticed only when the paragraph
        // ended); the author's mistake is the line before it.
        var source = "\\begin{tikzpicture}\n  \\draw plot (\\x,{\\x^0.5};\n\n\\end{tikzpicture}";
        var log = "! Paragraph ended before \\tikz@plot@expression was complete.\n<to be read again>\n                   \\par\nl.3\n";
        var e = TikzErrors.FromLog(log, source, sourceStartLine: 1);
        e.Line.Should().Be(2);
        e.Excerpt.Should().Contain("plot (");
        e.Column.Should().BeNull();
        e.Message.Should().Contain("isn't closed");
    }

    // ── drawing (real compiles) ─────────────────────────────────────────

    internal static TikzFigureService Service(string? cacheDir = null) =>
        new(new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tikz:CacheDir"] = cacheDir ?? Path.Combine(Path.GetTempPath(), $"tikz-tests-{Guid.NewGuid():N}"),
            }).Build(),
            NullLogger<TikzFigureService>.Instance);

    private static async Task<byte[]> DrawAsync(string source)
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var block = Block("figure", new { kind = "tikz", source, caption = "", label = "" });
        var r = await Service().RenderAsync(doc, block, "tests");
        r.Error.Should().BeNull(r.Error?.Message);
        return r.Svg!;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public async Task Each_corpus_file_imports_exports_compiles_and_draws(string file)
    {
        var tex = await File.ReadAllTextAsync(Path.Combine(CorpusDir, file));
        var doc = await ImportAsync(tex);
        var figure = Figures(doc);
        TikzFigure.IsTikz(figure.Content.RootElement).Should().BeTrue();

        // The source is the original picture, comments and all.
        var source = TikzFigure.Source(figure.Content.RootElement);
        var original = tex[tex.IndexOf(@"\begin{tikzpicture}", StringComparison.Ordinal)..(tex.IndexOf(@"\end{tikzpicture}", StringComparison.Ordinal) + 17)];
        source.Should().Be(TikzFigure.Dedent(original, "    "), "verbatim, apart from the figure's own indentation");

        var latex = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        foreach (var full in new[] { Export(doc), Preview(doc) })
        {
            full.Should().Contain(source);
            var pdf = await latex.RenderToPdfAsync(full);
            Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        }

        var drawn = await Service().RenderAsync(doc, figure, "tests");
        drawn.Error.Should().BeNull(drawn.Error?.Message);
        var svg = Encoding.UTF8.GetString(drawn.Svg!);
        svg.Should().Contain("<svg").And.Contain("</svg>");
    }

    [Fact]
    public async Task A_compile_error_names_the_line_in_the_figures_own_source_and_the_bad_part()
    {
        var source = "\\begin{tikzpicture}\n  \\draw (0,0) -- (1,1);\n  \\draw (0,0) -- \\nosuchmacro (2,2);\n\\end{tikzpicture}";
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var block = Block("figure", new { kind = "tikz", source, caption = "", label = "" });
        var r = await Service().RenderAsync(doc, block, "tests");

        r.Svg.Should().BeNull();
        r.Error!.Kind.Should().Be("tex");
        r.Error.Line.Should().Be(3);
        r.Error.Message.Should().Contain(@"\nosuchmacro");
        r.Error.Excerpt.Should().Be("  \\draw (0,0) -- \\nosuchmacro (2,2);");
        r.Error.Column.Should().NotBeNull();
        r.Error.Excerpt!.Substring(r.Error.Column!.Start, r.Error.Column.End - r.Error.Column.Start).Should().Be(@"\nosuchmacro");
    }

    [Fact]
    public async Task The_second_drawing_comes_from_the_cache_and_the_last_good_one_is_kept()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tikz-tests-{Guid.NewGuid():N}");
        var svc = Service(dir);
        var doc = new Document { Id = Guid.NewGuid(), Title = "T" };
        var block = Block("figure", new { kind = "tikz", source = Src, caption = "", label = "" });

        (await svc.RenderAsync(doc, block, "tests")).Cached.Should().BeFalse();
        var second = await svc.RenderAsync(doc, block, "tests");
        second.Cached.Should().BeTrue();
        second.Ok.Should().BeTrue();
        svc.LastGood(block.Id).Should().Equal(second.Svg);

        // Broken now: an error, and the last drawing still there.
        block.Content = JsonDocument.Parse(JsonSerializer.Serialize(new { kind = "tikz", source = @"\begin{tikzpicture}\draw (0,0) -- \oops;\end{tikzpicture}" }));
        (await svc.RenderAsync(doc, block, "tests")).Error!.Kind.Should().Be("tex");
        svc.HasLastGood(block.Id).Should().BeTrue();
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public async Task The_guard_refuses_an_unsafe_picture_before_any_compile()
    {
        var source = "\\begin{tikzpicture}\n\\node {\\input{/etc/hostname}};\n\\end{tikzpicture}";
        var r = await Service().RenderAsync(new Document { Id = Guid.NewGuid(), Title = "T" },
            Block("figure", new { kind = "tikz", source }), "tests");
        r.Svg.Should().BeNull();
        r.Error!.Kind.Should().Be("tex");
        r.Error.Line.Should().Be(2);
        r.Error.Message.Should().Contain("server");
    }

    [Fact]
    public void The_scanner_ignores_comments_and_verbatim_and_keeps_nested_pictures_whole()
    {
        var text = "% \\begin{tikzpicture}x\\end{tikzpicture}\n\\begin{verbatim}\\begin{tikzpicture}\\end{tikzpicture}\\end{verbatim}\n"
                 + "\\begin{tikzpicture}\\node{\\begin{tikzpicture}\\end{tikzpicture}};% \\end{tikzpicture}\n\\end{tikzpicture}";
        var spans = TikzFigure.FindEnvironments(text);
        spans.Should().HaveCount(1);
        text.Substring(spans[0].Start, spans[0].Length).Should().EndWith("% \\end{tikzpicture}\n\\end{tikzpicture}");
    }
}
