using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// The accessible PDF (TaggedPdf, PROD-40): the export's LaTeX made tagged PDF/UA-2. The rewrite is
/// checked on its own, then a document with every block the tagging code refused on 1 Nov (a code
/// block with a caption, highlight and strike, starred theorems, an [H] figure, a TikZ figure) is
/// exported and compiled with LuaLaTeX: the PDF must be tagged, titled, in the document's language,
/// and the figure must carry its alt text.
/// </summary>
public class TaggedPdfTests
{
    private const string Sample = """
        \documentclass[11pt,a4paper]{article}
        \usepackage[utf8]{inputenc}
        \usepackage[T1]{fontenc}
        \usepackage{lmodern}        % Latin Modern
        \usepackage{amsmath,amssymb,amsfonts,amsthm}
        \usepackage{cancel}
        \usepackage{siunitx}
        \usepackage{float}
        \usepackage{caption}
        \usepackage{multirow}
        \usepackage{listings}
        % listings ships no JavaScript
        \lstdefinelanguage{JavaScript}{
          morekeywords={async,await},
          morestring=[b]', morestring=[b]`}
        \usepackage{soul}
        \usepackage{ulem}
        \normalem
        \usepackage{hyperref}
        \lstset{
          basicstyle=\ttfamily\small,
        }
        \newtheorem{theorem}{Theorem}
        \newtheorem*{theorem*}{Theorem}
        \begin{document}
        \begin{figure}[H]
        \includegraphics[width=0.5\textwidth]{figures/a.png}
        \end{figure}
        \begin{lstlisting}[language=Python, caption={The mean}]
        def mean(xs):
            return sum(xs) / len(xs)
        \end{lstlisting}
        Some \st{struck} and \hl{highlighted words} and \multirow{2}{*}{x}.
        \end{document}
        """;

    [Fact]
    public void The_source_is_tagged_PDF_UA_2_in_the_documents_language()
    {
        var made = TaggedPdf.Apply(Sample, "fr", "Rivières & saisons");

        made.Latex.Should().StartWith(@"\DocumentMetadata{lang=fr, pdfversion=2.0, pdfstandard=ua-2, tagging=on");
        made.Latex.Should().Contain(@"\PassOptionsToPackage{protrusion=false}{microtype}");
        made.Latex.Should().Contain(@"\hypersetup{pdftitle={Rivières \& saisons}, pdfdisplaydoctitle=true}");
        TaggedPdf.Apply(Sample, null, null).Latex.Should().StartWith(@"\DocumentMetadata{lang=en,");
    }

    [Fact]
    public void Packages_the_tagging_code_refuses_go_when_unused_and_pdflatex_fonts_go()
    {
        var made = TaggedPdf.Apply(Sample, "en", "T");

        made.Dropped.Should().Contain(["inputenc", "fontenc", "lmodern", "cancel", "float", "caption", "listings", "soul", "ulem"]);
        made.Latex.Should().NotContain(@"\usepackage{cancel}").And.NotContain(@"\lstset").And.NotContain(@"\lstdefinelanguage")
            .And.NotContain("morestring").And.NotContain(@"\normalem").And.NotContain("inputenc");
        made.Latex.Should().Contain(@"\usepackage{amsmath,amssymb,amsfonts,amsthm}").And.Contain(@"\usepackage{siunitx}");
        // Used, so kept, and named as what LaTeX does not tag yet.
        made.Untaggable.Should().Equal("multirow");
        made.Latex.Should().Contain(@"\usepackage{multirow}");
    }

    [Fact]
    public void The_body_is_rewritten_to_what_tags()
    {
        var made = TaggedPdf.Apply(Sample, "en", "T").Latex;

        made.Should().Contain("\\par\\noindent\\textit{The mean}\\par\n\\begin{verbatim}\ndef mean(xs):");
        made.Should().NotContain("lstlisting");
        made.Should().Contain(@"\begin{figure}[!htbp]");
        made.Should().Contain(@"\strikeThrough{struck}").And.Contain(@"\colorbox{yellow}{highlighted} \colorbox{yellow}{words}");
        made.Should().Contain(@"\usepackage{lua-ul}");
        made.Should().Contain(@"\ifcsname theorem*\endcsname\else\newtheorem*{theorem*}{Theorem}\fi");
        made.Should().Contain(@"\newtheorem{theorem}{Theorem}");
        TaggedPdf.Apply(Sample.Replace(@"\begin{figure}[H]", "\\begin{equation}x\\end{equation}\n\\begin{theorem*}T\\end{theorem*}\n\\begin{proof}P\\end{proof}\n\\begin{figure}[H]"), "en", "T").Latex
            .Should().Contain(@"\par\begin{theorem*}").And.Contain(@"\par\begin{proof}");
    }

    [Theory]
    [InlineData(@"\includegraphics[width=2cm]{figures/a.png}", @"\includegraphics[alt={A curve, rising},width=2cm]{figures/a.png}")]
    [InlineData(@"\includegraphics{figures/a.png}", @"\includegraphics[alt={A curve, rising}]{figures/a.png}")]
    [InlineData("\\begin{tikzpicture}\n\\draw (0,0);", "\\begin{tikzpicture}[alt={A curve, rising}]\n\\draw (0,0);")]
    [InlineData(@"\begin{tikzpicture}[scale=2]", @"\begin{tikzpicture}[alt={A curve, rising},scale=2]")]
    [InlineData(@"\begin{tikzcd} A \arrow[r] & B", @"\begin{tikzcd}[alt={A curve, rising}] A \arrow[r] & B")]
    public void A_figure_carries_its_alt_text(string latex, string expected) =>
        TaggedPdf.WithAlt(latex, "A curve, rising").Should().Be(expected);

    [Fact]
    public void The_alt_text_is_the_description_else_the_caption_never_a_file_name()
    {
        static JsonElement C(object o) => JsonSerializer.SerializeToElement(o);
        TaggedPdf.AltOf(C(new { alt = "Temperatures rise in spring", caption = "Temperatures" })).Should().Be("Temperatures rise in spring");
        TaggedPdf.AltOf(C(new { alt = "IMG_2041.png", caption = "Temperatures" })).Should().Be("Temperatures");
        TaggedPdf.AltOf(C(new { alt = "", caption = "" })).Should().BeNull();
        TaggedPdf.WithAlt(@"\includegraphics{figures/a.png}", null).Should().Be(@"\includegraphics{figures/a.png}");
        TaggedPdf.WithAlt(@"\includegraphics{x}", "50% of {all}").Should().Be(@"\includegraphics[alt={50\% of \{all\}}]{x}");
    }

    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    [Fact]
    [Trait("Category", "TaggedCompile")]
    public async Task A_document_with_every_refused_block_compiles_tagged()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "La température des rivières", Language = "fr", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        };
        var blocks = new List<Block>
        {
            B("heading", new { text = "Méthode", level = 1 }),
            B("paragraph", new { text = "Du texte ~~barré~~, ==surligné sur plusieurs mots== et $E = mc^2$." }),
            B("equation", new { latex = @"\bar{T} = \frac{1}{52}\sum_{i=1}^{52} T_i" }),
            // With microtype's protrusion, this one stopped LuaTeX under the MathML tagging (1 Nov).
            B("equation", new { latex = @"\min_{\theta}\; \frac{1}{N}\sum_{i=1}^{N} \ell\bigl(f_{\theta}(x_i),\, y_i\bigr)" }),
            B("theorem", new { text = "La moyenne existe.", theoremType = "theorem" }),
            B("theorem", new { text = "Elle est bornée.", theoremType = "proof" }),
            B("theorem", new { text = "Une rivière est profonde au-delà de 3 m.", theoremType = "definition", numbered = false }),
            B("code", new { code = "def mean(xs):\n    return sum(xs) / len(xs)\n", language = "python", caption = "La moyenne" }),
            B("figure", new
            {
                kind = "tikz", alt = "Une courbe qui monte au printemps", caption = "La température au fil de l'année", placement = "here",
                source = "\\begin{tikzpicture}\n\\draw[thick,->] (0,0) -- (4,0);\n\\draw[blue] (0,0) .. controls (1.5,2.5) .. (4,2);\n\\end{tikzpicture}",
            }),
            B("table", new { headers = new[] { "Saison", "°C" }, rows = new[] { new[] { "Hiver", "4" }, new[] { "Été", "19" } }, caption = "Moyennes" }),
            B("list", new { items = new[] { "Un", "Deux" }, ordered = true }),
        };
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions { Tagged = true });
        var made = TaggedPdf.Apply(tex, doc.Language, doc.Title);
        TexSourceGuard.Violation(made.Latex).Should().BeNull();
        made.Latex.Should().Contain("alt={Une courbe qui monte au printemps}");

        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(made.Latex, TaggedPdf.Engine, timeout: 240);

        var raw = System.Text.Encoding.Latin1.GetString(Inflated(pdf));
        raw.Should().Contain("/StructTreeRoot").And.Contain("/Lang (fr)").And.Contain("pdfuaid")
            .And.Contain("/DisplayDocTitle true").And.Contain("/Formula").And.Contain("/Figure").And.Contain("/Table");
        // The figure's alt text, which the tagging code writes as UTF-16 (<FEFF…>).
        var alt = "FEFF" + Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes("Une courbe qui monte au printemps"));
        raw.Should().Contain("/Alt <" + alt + ">");
    }

    // The PDF with its compressed streams inflated beside it, to read its tags.
    private static byte[] Inflated(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
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
