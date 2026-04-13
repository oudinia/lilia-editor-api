using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// LaTeX import coverage tests — promoted from the empirical /tmp/latex-coverage-run.sh
/// run on 2026-04-13. Each fixture mirrors a real-world LaTeX paper shape and pins
/// the parser's behavior so coverage gaps surface as failing tests.
///
/// Reference: lilia-docs/specs/launch-readiness/latex-import-coverage-empirical-2026-04-13.md
/// </summary>
public class LatexImportCoverageTests
{
    private readonly LatexParser _parser = new();

    private async Task<ImportDocument> ParseAsync(string latex) =>
        await _parser.ParseTextAsync(latex);

    // ── P1-7 — default title is empty for raw-text input ──────────────

    [Fact]
    public async Task ParseTextAsync_WithoutTitle_DefaultsToEmptyTitle()
    {
        var doc = await ParseAsync("Just a paragraph.");
        doc.Title.Should().BeEmpty();
    }

    // ── P0-8 — \title{} extracted AND stripped from body ──────────────

    [Fact]
    public async Task TitleAuthor_AreStrippedFromBody_NotJustExtracted()
    {
        var latex = """
            \title{On Hilbert Spaces}
            \author{A. Researcher}
            \section{Introduction}
            We consider the spectral theorem.
            """;
        var doc = await ParseAsync(latex);

        doc.Title.Should().Be("On Hilbert Spaces");
        doc.Metadata.Author.Should().Be("A. Researcher");

        // The body should NOT contain a paragraph that re-emits \title{} or \author{}.
        var paragraphs = doc.Elements.OfType<ImportParagraph>().ToList();
        paragraphs.Should().NotContain(p => p.Text.Contains("\\title"));
        paragraphs.Should().NotContain(p => p.Text.Contains("\\author"));
    }

    // ── P0-3 — \title with nested commands handled ────────────────────

    [Fact]
    public async Task Title_WithNestedCommand_ParsesFullText()
    {
        var doc = await ParseAsync("\\title{Foo \\textbf{Bar} Baz}\nBody");
        doc.Title.Should().Be("Foo Bar Baz");
    }

    // ── P0-1 — itemize / enumerate parsed as list items ───────────────

    [Fact]
    public async Task ItemizeEnvironment_ProducesListItems()
    {
        var latex = """
            \section{Method}
            Key features:
            \begin{itemize}
              \item Fast convergence
              \item Low memory footprint
              \item Numerically stable
            \end{itemize}
            """;
        var doc = await ParseAsync(latex);

        var items = doc.Elements.OfType<ImportListItem>().ToList();
        items.Should().HaveCount(3);
        items.Should().OnlyContain(li => !li.IsNumbered);
        items[0].Text.Should().Contain("Fast convergence");
    }

    [Fact]
    public async Task EnumerateEnvironment_ProducesNumberedListItems()
    {
        var latex = """
            \begin{enumerate}
              \item Initialize the parameters.
              \item Run the optimization loop.
              \item Report the final loss.
            \end{enumerate}
            """;
        var doc = await ParseAsync(latex);

        var items = doc.Elements.OfType<ImportListItem>().ToList();
        items.Should().HaveCount(3);
        items.Should().OnlyContain(li => li.IsNumbered);
    }

    [Fact]
    public async Task DescriptionEnvironment_ProducesItemsWithMarkers()
    {
        var latex = """
            \begin{description}
              \item[Foo] First entry.
              \item[Bar] Second entry.
            \end{description}
            """;
        var doc = await ParseAsync(latex);

        var items = doc.Elements.OfType<ImportListItem>().ToList();
        items.Should().HaveCount(2);
        items[0].ListMarker.Should().Be("Foo");
        items[1].ListMarker.Should().Be("Bar");
    }

    // ── P0-2 — tables with \hline are not corrupted ───────────────────

    [Fact]
    public async Task Table_WithHlineSeparators_ParsesAllRows()
    {
        var latex = """
            \begin{table}[h]
            \centering
            \begin{tabular}{lcr}
            \hline
            Method & Accuracy & Time \\
            \hline
            Baseline & 0.82 & 12.3 \\
            Ours & 0.91 & 8.7 \\
            \hline
            \end{tabular}
            \end{table}
            """;
        var doc = await ParseAsync(latex);

        var table = doc.Elements.OfType<ImportTable>().Single();
        table.Rows.Should().HaveCount(3, "header + 2 data rows");
        table.HasHeaderRow.Should().BeTrue();
        table.Rows[0][0].Text.Should().Be("Method");
        table.Rows[1][0].Text.Should().Be("Baseline");
        table.Rows[2][0].Text.Should().Be("Ours");
    }

    // ── P0-3 — figure captions with nested commands not truncated ─────

    [Fact]
    public async Task FigureCaption_WithNestedTextbf_NotTruncated()
    {
        var latex = """
            \begin{figure}[ht]
            \centering
            \includegraphics[width=0.6\textwidth]{plot1.png}
            \caption{Convergence of the loss over \textbf{1000} epochs on the validation set.}
            \end{figure}
            """;
        var doc = await ParseAsync(latex);

        var img = doc.Elements.OfType<ImportImage>().Single();
        img.AltText.Should().NotBeNullOrEmpty();
        img.AltText.Should().Contain("1000");
        img.AltText.Should().Contain("epochs on the validation set");
    }

    // ── P0-4 — figure src propagated from \includegraphics ────────────

    [Fact]
    public async Task FigureIncludegraphics_PropagatesFilename()
    {
        var latex = """
            \begin{figure}
            \includegraphics[width=0.5\linewidth]{results/plot1.png}
            \caption{A plot.}
            \end{figure}
            """;
        var doc = await ParseAsync(latex);

        var img = doc.Elements.OfType<ImportImage>().Single();
        img.Filename.Should().Be("results/plot1.png");
    }

    // ── P0-5 — lstlisting / minted languages parsed correctly ─────────

    [Fact]
    public async Task LstListing_ExtractsLanguageFromBracketArg()
    {
        var latex = """
            \begin{lstlisting}[language=Python]
            def f(x): return x
            \end{lstlisting}
            """;
        var doc = await ParseAsync(latex);

        var code = doc.Elements.OfType<ImportCodeBlock>().Single();
        code.Language.Should().Be("python");
        code.Text.Should().StartWith("def f");
        code.Text.Should().NotContain("language=");
    }

    [Fact]
    public async Task Minted_ExtractsLanguageFromBraceArg_AndStripsItFromBody()
    {
        var latex = """
            \begin{minted}{rust}
            fn main() { println!("hi"); }
            \end{minted}
            """;
        var doc = await ParseAsync(latex);

        var code = doc.Elements.OfType<ImportCodeBlock>().Single();
        code.Language.Should().Be("rust");
        code.Text.Should().StartWith("fn main");
        code.Text.Should().NotContain("{rust}");
    }

    // ── P0-6 — theorem-style environments emit ImportTheorem ──────────

    [Fact]
    public async Task TheoremEnvironment_ProducesImportTheorem_WithTitleAndLabel()
    {
        var latex = """
            \begin{theorem}[Pythagoras]
            \label{thm:pyth}
            For any right triangle, $a^2 + b^2 = c^2$.
            \end{theorem}
            """;
        var doc = await ParseAsync(latex);

        var th = doc.Elements.OfType<ImportTheorem>().Single();
        th.EnvironmentType.Should().Be(TheoremEnvironmentType.Theorem);
        th.Title.Should().Be("Pythagoras");
        th.Label.Should().Be("thm:pyth");
        th.Text.Should().Contain("right triangle");
    }

    [Fact]
    public async Task ProofDefinitionLemma_AllRecognized()
    {
        var latex = """
            \begin{proof}
            By construction.
            \end{proof}
            \begin{definition}
            A prime is a positive integer with exactly two divisors.
            \end{definition}
            \begin{lemma}
            Every integer greater than 1 has a prime factor.
            \end{lemma}
            """;
        var doc = await ParseAsync(latex);

        var theorems = doc.Elements.OfType<ImportTheorem>().ToList();
        theorems.Should().HaveCount(3);
        theorems.Select(t => t.EnvironmentType).Should().BeEquivalentTo(new[]
        {
            TheoremEnvironmentType.Proof,
            TheoremEnvironmentType.Definition,
            TheoremEnvironmentType.Lemma,
        });
    }

    // ── P0-7 — abstract environment ───────────────────────────────────

    [Fact]
    public async Task AbstractEnvironment_ProducesImportAbstract()
    {
        var latex = """
            \begin{abstract}
            We review recent progress in X. Our main contribution is a unified framework.
            \end{abstract}
            \section{Background}
            """;
        var doc = await ParseAsync(latex);

        var ab = doc.Elements.OfType<ImportAbstract>().Single();
        ab.Text.Should().Contain("recent progress in X");
        ab.Text.Should().Contain("unified framework");
    }

    // ── P0-9 — bibliography environment ───────────────────────────────

    [Fact]
    public async Task ThebibliographyEnvironment_ProducesBibEntries()
    {
        var latex = """
            \begin{thebibliography}{9}
            \bibitem{smith2020} J. Smith, Foo Bar, JMLR 2020.
            \bibitem{lee2021} K. Lee, Baz Qux, NeurIPS 2021.
            \end{thebibliography}
            """;
        var doc = await ParseAsync(latex);

        var entries = doc.Elements.OfType<ImportBibliographyEntry>().ToList();
        entries.Should().HaveCount(2);
        entries[0].ReferenceLabel.Should().Be("smith2020");
        entries[0].Text.Should().Contain("J. Smith");
        entries[1].ReferenceLabel.Should().Be("lee2021");
    }

    // ── P1-1 — \paragraph{}/\subparagraph{} treated as low-level headings

    [Fact]
    public async Task ParagraphCommand_BecomesLevel4Heading()
    {
        var doc = await ParseAsync("\\paragraph{Notation.}\nBody text follows.");

        var headings = doc.Elements.OfType<ImportHeading>().ToList();
        headings.Should().Contain(h => h.Level == 4 && h.Text == "Notation.");
    }

    // ── P1-3 — align environment wrapped so it renders ────────────────

    [Fact]
    public async Task AlignEnvironment_WrapperPreserved()
    {
        var latex = """
            \begin{align}
            E_n &= n\hbar\omega \\
            n &= 0,1,2,\ldots
            \end{align}
            """;
        var doc = await ParseAsync(latex);

        var eq = doc.Elements.OfType<ImportEquation>().Single();
        eq.LatexContent.Should().Contain("\\begin{align}");
        eq.LatexContent.Should().Contain("\\end{align}");
        eq.LatexContent.Should().Contain("E_n");
    }

    // ── P1-4 — \label{} stripped from equation body ───────────────────

    [Fact]
    public async Task EquationLabel_LiftedFromLatexBody()
    {
        var latex = """
            \begin{equation}
            \label{eq:main}
            \hat{H}\psi = E\psi
            \end{equation}
            """;
        var doc = await ParseAsync(latex);

        var eq = doc.Elements.OfType<ImportEquation>().Single();
        eq.LatexContent.Should().NotContain("\\label{eq:main}");
        eq.LatexContent.Should().Contain("\\hat{H}");
    }

    // ── P1-6 — unknown environments emit warnings ─────────────────────

    [Fact]
    public async Task UnknownEnvironment_EmitsWarning()
    {
        var latex = """
            \begin{tikzpicture}
            \draw (0,0) -- (1,1);
            \end{tikzpicture}
            """;
        var doc = await ParseAsync(latex);

        doc.Warnings.Should().NotBeEmpty();
        doc.Warnings.Should().Contain(w => w.Message.Contains("tikzpicture"));
    }

    [Fact]
    public async Task UnknownEnvironment_PreservedAsLatexPassthrough()
    {
        // tikzpicture is not directly handled; it should now be preserved as an
        // ImportLatexPassthrough so the raw LaTeX round-trips on export.
        var latex = """
            \begin{tikzpicture}
            \draw (0,0) -- (1,1);
            \node at (0.5, 0.5) {hello};
            \end{tikzpicture}
            """;
        var doc = await ParseAsync(latex);

        var passthrough = doc.Elements.OfType<ImportLatexPassthrough>().Single();
        passthrough.LatexCode.Should().Contain("\\begin{tikzpicture}");
        passthrough.LatexCode.Should().Contain("\\draw (0,0) -- (1,1);");
        passthrough.LatexCode.Should().Contain("\\node at (0.5, 0.5)");
        passthrough.LatexCode.Should().Contain("\\end{tikzpicture}");
    }

    [Fact]
    public async Task UnknownEnvironment_PassthroughCoexistsWithWarning()
    {
        var doc = await ParseAsync("\\begin{customenv}content\\end{customenv}");

        // We want BOTH: a passthrough (no data loss) AND a warning (user awareness).
        doc.Elements.OfType<ImportLatexPassthrough>().Should().HaveCount(1);
        doc.Warnings.Should().Contain(w => w.Message.Contains("customenv"));
    }

    // ── #58 — inline cite / ref round-trip + key extraction ───────────

    [Fact]
    public async Task InlineCitations_PreservedLiterallyInParagraphText()
    {
        // The editor stores paragraph text in markdown-flavored format and the export
        // path's ProcessLatexText protects \cite{} from escaping. So the round-trip
        // works *because* the parser leaves \cite{} as raw text. Lock that in.
        var latex = "According to \\cite{smith2020}, the rate is sublinear.\n\nAlso \\cite{a,b,c}.";
        var doc = await ParseAsync(latex);

        var paragraphs = doc.Elements.OfType<ImportParagraph>().ToList();
        paragraphs.Should().NotBeEmpty();
        string.Join(" ", paragraphs.Select(p => p.Text)).Should().Contain("\\cite{smith2020}");
        string.Join(" ", paragraphs.Select(p => p.Text)).Should().Contain("\\cite{a,b,c}");
    }

    [Fact]
    public async Task CitedKeys_HarvestedFromAllParagraphs()
    {
        var latex = """
            According to \cite{smith2020}, X holds. Other works \citep{lee2021,brown2020}
            extended this idea. Recent results \parencite{wang2024} confirm.
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.CitedKeys.Should().BeEquivalentTo(new[] { "brown2020", "lee2021", "smith2020", "wang2024" });
    }

    [Fact]
    public async Task ReferencedLabels_HarvestedFromAllParagraphs()
    {
        var latex = """
            See Figure~\ref{fig:loss} and equation~\eqref{eq:main}. Also \cref{thm:pyth}
            and \autoref{sec:method}.
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.ReferencedLabels.Should().BeEquivalentTo(new[]
        {
            "eq:main", "fig:loss", "sec:method", "thm:pyth"
        });
    }

    [Fact]
    public async Task CitedKeys_DeduplicatedAcrossParagraphs()
    {
        var doc = await ParseAsync("Para one cites \\cite{key}.\n\nPara two cites \\cite{key} again.");
        doc.Metadata.CitedKeys.Should().HaveCount(1);
        doc.Metadata.CitedKeys.Should().Contain("key");
    }

    // ── #59 — geometry / titlesec / babel preamble extraction ────────

    [Fact]
    public async Task Geometry_OptionsExtracted_FromUsepackageBracket()
    {
        var latex = """
            \documentclass{article}
            \usepackage[margin=1in,a4paper,twoside]{geometry}
            \begin{document}
            body
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.GeometryOptions.Should().Be("margin=1in,a4paper,twoside");
    }

    [Fact]
    public async Task Geometry_AlternativeFormExtracted_FromGeometryCommand()
    {
        var latex = """
            \documentclass{article}
            \usepackage{geometry}
            \geometry{margin=2cm,letterpaper}
            \begin{document}
            body
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.GeometryOptions.Should().Be("margin=2cm,letterpaper");
    }

    [Fact]
    public async Task Titlesec_FlagSetWhenLoaded()
    {
        var doc = await ParseAsync("\\documentclass{article}\n\\usepackage{titlesec}\n\\begin{document}\nbody\n\\end{document}");

        doc.Metadata.UsesTitlesec.Should().BeTrue();
        doc.Warnings.Should().Contain(w => w.Message.Contains("titlesec"));
    }

    [Fact]
    public async Task Babel_LanguageExtractedFromOptionList()
    {
        var latex = """
            \documentclass{article}
            \usepackage[english,french]{babel}
            \begin{document}
            Bonjour.
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        // Convention: the LAST language in the option list is the primary one.
        doc.Metadata.Language.Should().Be("french");
    }

    [Fact]
    public async Task Polyglossia_LanguageExtracted_FromSetdefaultlanguage()
    {
        var latex = """
            \documentclass{article}
            \usepackage{polyglossia}
            \setdefaultlanguage{spanish}
            \begin{document}
            Hola.
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.Language.Should().Be("spanish");
    }

    [Fact]
    public async Task Babel_SingleLanguage_Extracted()
    {
        var doc = await ParseAsync("\\documentclass{article}\n\\usepackage[german]{babel}\n\\begin{document}\nHallo.\n\\end{document}");

        doc.Metadata.Language.Should().Be("german");
    }

    [Fact]
    public async Task KnownEnvironments_DoNotEmitUnknownWarnings()
    {
        var latex = """
            \begin{equation} x = 1 \end{equation}
            \begin{itemize} \item one \end{itemize}
            \begin{theorem} t \end{theorem}
            """;
        var doc = await ParseAsync(latex);

        doc.Warnings.Should().NotContain(w => w.Message.Contains("Skipped unsupported"));
    }

    // ── #45 — preamble metadata extraction ────────────────────────────

    [Fact]
    public async Task DocumentClass_WithOptions_Extracted()
    {
        var latex = """
            \documentclass[11pt,a4paper,twocolumn]{article}
            \begin{document}
            Hello.
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.DocumentClass.Should().Be("article");
        doc.Metadata.DocumentClassOptions.Should().Be("11pt,a4paper,twocolumn");
    }

    [Fact]
    public async Task UsePackage_CommaList_ExtractedAsMultipleEntries()
    {
        var latex = """
            \documentclass{article}
            \usepackage[utf8]{inputenc}
            \usepackage{amsmath,amssymb,amsthm}
            \usepackage{graphicx}
            \begin{document}
            Body.
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        var names = doc.Metadata.Packages.Select(p => p.Name).ToList();
        names.Should().Contain(new[] { "inputenc", "amsmath", "amssymb", "amsthm", "graphicx" });

        var inputenc = doc.Metadata.Packages.Single(p => p.Name == "inputenc");
        inputenc.Options.Should().Be("utf8");
    }

    [Fact]
    public async Task Date_Extracted_FromPreamble()
    {
        var latex = """
            \title{X}
            \date{March 2026}
            \begin{document}
            body
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.Date.Should().Be("March 2026");
    }

    [Fact]
    public async Task BibliographyStyle_Extracted()
    {
        var latex = """
            \bibliographystyle{plainnat}
            \begin{document}
            body
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Metadata.BibliographyStyle.Should().Be("plainnat");
    }

    [Fact]
    public async Task Preamble_DoesNotLeakIntoBodyParagraphs()
    {
        var latex = """
            \documentclass{article}
            \usepackage{amsmath}
            \title{X}
            \begin{document}
            The real content.
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        var paragraphs = doc.Elements.OfType<ImportParagraph>().ToList();
        paragraphs.Should().NotContain(p => p.Text.Contains("\\documentclass"));
        paragraphs.Should().NotContain(p => p.Text.Contains("\\usepackage"));
        paragraphs.Should().Contain(p => p.Text.Contains("real content"));
    }

    [Fact]
    public async Task LimitedPackage_EmitsWarning()
    {
        var latex = """
            \documentclass{article}
            \usepackage{tikz}
            \begin{document}
            body
            \end{document}
            """;
        var doc = await ParseAsync(latex);

        doc.Warnings.Should().Contain(w => w.Message.Contains("tikz"));
    }

    [Fact]
    public async Task BeamerClass_EmitsWarning()
    {
        var doc = await ParseAsync("\\documentclass{beamer}\n\\begin{document}\nbody\n\\end{document}");

        doc.Warnings.Should().Contain(w => w.Message.Contains("Beamer"));
    }
}
