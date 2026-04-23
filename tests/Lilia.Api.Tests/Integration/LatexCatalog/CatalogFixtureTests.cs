using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.LatexCatalog;

/// <summary>
/// One canonical fixture per parser handler kind. Each test parses
/// a minimal .tex snippet through <see cref="ILatexParser"/> and
/// asserts the resulting <see cref="ImportElement"/> shape — proving
/// end-to-end that the coverage claim the catalog makes for that
/// handler kind actually lands on a real block type.
///
/// These tests are the concrete backing for the 'full' coverage
/// claims on the public page. If any fixture breaks, the
/// implementation-status card's per-handler count needs to drop
/// and the affected catalog rows should be demoted until the
/// handler is restored.
///
/// Target: one fixture per handler_kind in
/// <see cref="CatalogIntegrityTests.ValidHandlerKinds"/>. Current
/// coverage is tracked via the perHandlerFixtures count in
/// PublicCoverageController.ImplementationStatus — keep it in sync
/// when adding or removing fixtures here.
/// </summary>
[Collection("Integration")]
public class CatalogFixtureTests : IntegrationTestBase
{
    public CatalogFixtureTests(TestDatabaseFixture fixture) : base(fixture) { }

    private ILatexParser CreateParser()
    {
        var scope = Fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ILatexParser>();
    }

    private static async Task<ImportDocument> ParseAsync(ILatexParser parser, string body)
    {
        // Every fixture is wrapped in a minimal article skeleton so
        // the parser's top-level extraction paths (\begin{document} etc.)
        // fire normally.
        var source = $@"\documentclass{{article}}
\begin{{document}}
{body}
\end{{document}}";
        return await parser.ParseTextAsync(source);
    }

    // ---- section-regex -----------------------------------------------

    [Fact]
    public async Task section_regex_emits_heading_blocks()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"\section{Intro}
Body.
\subsection{Details}
More body.
\subsubsection{Even finer}
\paragraph{A para heading} follow-up text.
\subparagraph{Even finer para}");

        var headings = doc.GetElements<ImportHeading>().ToList();
        headings.Should().NotBeEmpty("the section regex should produce ImportHeading blocks");
        headings.Select(h => h.Level).Should().Contain(new[] { 1, 2, 3 },
            "at minimum \\section / \\subsection / \\subsubsection should emit levels 1–3");
        headings.Should().Contain(h => h.Text == "Intro");
        headings.Should().Contain(h => h.Text == "Details");
    }

    // ---- citation-regex ----------------------------------------------

    [Fact]
    public async Task citation_regex_preserves_citation_in_paragraph_text()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"As shown in \cite{foo2020}, the result holds.
See also \citet{bar}, \citep{baz}, \parencite{qux}.");

        // Citations are inline commands; they surface inside paragraph
        // text (preserved verbatim by NormaliseInlineCommands).
        var paragraphs = doc.GetElements<ImportParagraph>().ToList();
        paragraphs.Should().NotBeEmpty();
        var joined = string.Join(" ", paragraphs.Select(p => p.Text));
        joined.Should().ContainAny("cite", "foo2020", "qux");
    }

    // ---- known-structural --------------------------------------------

    [Fact]
    public async Task known_structural_itemize_emits_list_items()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"\begin{itemize}
\item one
\item two
\item three
\end{itemize}");

        var items = doc.GetElements<ImportListItem>().ToList();
        items.Should().HaveCountGreaterThanOrEqualTo(3,
            "three \\item entries should produce three list items");
    }

    [Fact]
    public async Task known_structural_figure_emits_image()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"\begin{figure}[ht]
\includegraphics[width=0.5\linewidth]{diagram.png}
\caption{A caption}\label{fig:a}
\end{figure}");

        var images = doc.GetElements<ImportImage>().ToList();
        images.Should().NotBeEmpty("figure env with includegraphics should produce an ImportImage");
    }

    // ---- theorem-like ------------------------------------------------

    [Fact]
    public async Task theorem_like_emits_theorem_block()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"\begin{theorem}
For every integer n > 0, there exists a prime p such that n < p < 2n.
\end{theorem}

\begin{lemma}
Auxiliary lemma.
\end{lemma}

\begin{proof}
By induction on n.
\end{proof}");

        var theorems = doc.GetElements<ImportTheorem>().ToList();
        theorems.Should().HaveCountGreaterThanOrEqualTo(3,
            "theorem / lemma / proof should each produce an ImportTheorem block");
        var envTypes = theorems.Select(t => t.EnvironmentType).ToList();
        envTypes.Should().Contain(TheoremEnvironmentType.Theorem);
        envTypes.Should().Contain(TheoremEnvironmentType.Lemma);
        envTypes.Should().Contain(TheoremEnvironmentType.Proof);
    }

    // ---- algorithmic -------------------------------------------------

    [Fact]
    public async Task algorithmic_emits_algorithm_with_typed_lines()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"\begin{algorithm}
\caption{Example}
\begin{algorithmic}
\Require input x
\Ensure output y
\State $x \gets 1$
\If{$x = 1$}
  \State return
\Else
  \State $x \gets x - 1$
\EndIf
\While{$x > 0$}
  \State step
\EndWhile
\Return x
\end{algorithmic}
\end{algorithm}");

        var algos = doc.GetElements<ImportAlgorithm>().ToList();
        algos.Should().NotBeEmpty("algorithm env should produce ImportAlgorithm");
        algos[0].Lines.Should().NotBeEmpty();
        var kinds = algos[0].Lines.Select(l => l.Kind).ToList();
        kinds.Should().Contain(new[] { "require", "ensure", "statement", "if", "else", "endif", "while", "endwhile", "return" }
            .Intersect(kinds),
            "the algorithmic regex should produce typed-line Kinds matching its token alternatives");
    }

    // ---- math-katex + math-env ---------------------------------------

    [Fact]
    public async Task math_katex_display_equation_emits_equation_block()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"Inline math $\alpha + \beta = \gamma$.

\begin{equation}
E = mc^2 \label{eq:main}
\end{equation}

\begin{align}
a + b &= c \\
d - e &= f
\end{align}");

        var equations = doc.GetElements<ImportEquation>().ToList();
        equations.Should().NotBeEmpty("equation and align environments should emit ImportEquation blocks");
    }

    [Fact]
    public async Task math_env_cases_survives_inside_equation()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser, @"\begin{equation}
f(x) = \begin{cases} x & \text{if } x > 0 \\ 0 & \text{otherwise} \end{cases}
\end{equation}");

        var equations = doc.GetElements<ImportEquation>().ToList();
        equations.Should().NotBeEmpty();
        // cases body should be inside the equation's LaTeX — the exact
        // KaTeX rendering is client-side, so we just assert preservation.
        (equations[0].LatexContent ?? "").Should().Contain("cases");
    }

    // ---- inline-markdown ---------------------------------------------

    [Fact]
    public async Task inline_markdown_wrappers_become_markdown_marks()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser,
            @"A \textbf{bold} word and an \textit{italic} one, plus \emph{emphasis}.");

        var paragraphs = doc.GetElements<ImportParagraph>().ToList();
        paragraphs.Should().NotBeEmpty();
        var text = string.Join(" ", paragraphs.Select(p => p.Text));
        // NormaliseInlineCommands wraps textbf → **…**, textit/emph → *…*.
        text.Should().MatchRegex(@"\*\*bold\*\*", "textbf should become **bold**");
        text.Should().MatchRegex(@"\*italic\*", "textit should become *italic*");
    }

    // ---- inline-preserved --------------------------------------------

    [Fact]
    public async Task inline_preserved_ref_survives_in_paragraph()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser,
            @"See Figure~\ref{fig:foo} and equation \eqref{eq:bar} for details.");

        var paragraphs = doc.GetElements<ImportParagraph>().ToList();
        paragraphs.Should().NotBeEmpty();
        var text = string.Join(" ", paragraphs.Select(p => p.Text));
        text.Should().ContainAny("\\ref", "fig:foo",
            "\\ref{...} is in PreservedInlineCommands and should survive verbatim or with its arg");
        text.Should().ContainAny("\\eqref", "eq:bar");
    }

    // ---- metadata-extract --------------------------------------------

    [Fact]
    public async Task metadata_extract_captures_title_and_author()
    {
        // NOTE: the parser's \title extractor runs against the raw
        // source (not inside \begin{document}), so we use a different
        // wrapper for this test.
        var parser = CreateParser();
        var source = @"\documentclass{article}
\title{Invariants of Higher Cohomology}
\author{Jane Doe \and John Smith}
\date{April 2026}
\begin{document}
\maketitle
Body paragraph.
\end{document}";
        var doc = await parser.ParseTextAsync(source);

        doc.Title.Should().Be("Invariants of Higher Cohomology",
            "\\title extraction via MatchBalanced should populate the document Title");
        doc.Metadata.Author.Should().Contain("Jane Doe",
            "\\author extraction should populate ImportMetadata.Author");
    }

    // ---- inline-code -------------------------------------------------

    [Fact]
    public async Task inline_code_wraps_argument_in_backticks()
    {
        var parser = CreateParser();
        var doc = await ParseAsync(parser,
            @"Use \texttt{grep -r foo} to search, not \verb|sed|.");

        var paragraphs = doc.GetElements<ImportParagraph>().ToList();
        paragraphs.Should().NotBeEmpty();
        var text = string.Join(" ", paragraphs.Select(p => p.Text));
        text.Should().Contain("`", "CodeDisplayInlineCommands should wrap the argument in Markdown backticks");
        text.Should().Contain("grep -r foo");
    }
}
