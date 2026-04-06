using System.IO.Compression;
using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class LaTeXScenarioTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public LaTeXScenarioTests(TestDatabaseFixture fixture) : base(fixture) { }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<Lilia.Core.Entities.Document> SeedDocWithUser(string? title = null)
    {
        await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        return await SeedDocumentAsync(UserId, title ?? "Test Document");
    }

    private async Task<string> GetMainTexFromExport(HttpClient client, Guid docId)
    {
        var response = await client.GetAsync($"/api/documents/{docId}/export/latex?structure=single");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var mainEntry = archive.GetEntry("main.tex");
        mainEntry.Should().NotBeNull("ZIP should contain main.tex");
        using var reader = new StreamReader(mainEntry!.Open());
        return await reader.ReadToEndAsync();
    }

    // ── P0 Scenario Tests ───────────────────────────────────────────

    [Fact]
    public async Task BasicDocumentStructure_H1AndH2Sections()
    {
        var doc = await SeedDocWithUser("Structured Paper");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"First section content."}""", 1);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Methods","level":2}""", 2);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Methods content."}""", 3);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Results","level":2}""", 4);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Results content."}""", 5);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Discussion","level":2}""", 6);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Discussion content."}""", 7);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\section{Introduction}");
        latex.Should().Contain(@"\subsection{Methods}");
        latex.Should().Contain(@"\subsection{Results}");
        latex.Should().Contain(@"\subsection{Discussion}");
    }

    [Fact]
    public async Task ParagraphInlineFormatting_BoldItalicUnderlineCode()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"This has *bold* and _italic_ and __underline__ and `code` formatting."}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\textbf{");
        latex.Should().Contain(@"\textit{");
        latex.Should().Contain(@"\underline{");
        latex.Should().Contain(@"\texttt{");
    }

    [Fact]
    public async Task InlineMath_DollarSignsPreserved()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Einstein showed $E = mc^2$ and we know $\\alpha > 0$ holds."}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain("$E = mc^2$");
        latex.Should().Contain(@"$\alpha > 0$");
    }

    [Fact]
    public async Task DisplayEquation_Numbered_WithLabel()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"E = mc^2","mode":"display","label":"eq:test","numbered":true}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{equation}");
        latex.Should().Contain(@"\label{eq:test}");
        latex.Should().Contain("E = mc^2");
        latex.Should().Contain(@"\end{equation}");
    }

    [Fact]
    public async Task DisplayEquation_Unnumbered()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"F = ma","mode":"display","numbered":false}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{equation*}");
        latex.Should().Contain("F = ma");
        latex.Should().Contain(@"\end{equation*}");
    }

    [Fact]
    public async Task DisplayEquation_AlignMode()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"x &= a + b \\\\ y &= c + d","mode":"align"}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{align}");
        latex.Should().Contain(@"\end{align}");
    }

    [Fact]
    public async Task CrossReferences_LabeledEquationAndRef()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"E = mc^2","mode":"display","label":"eq:test","numbered":true}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"As shown in Equation \\ref{eq:test}, energy and mass are equivalent."}""", 1);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\label{eq:test}");
        latex.Should().Contain(@"\ref{eq:test}");
    }

    [Fact]
    public async Task CitationsAndBibliography()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Recent work @cite{smith2024} shows promising results."}""", 0);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 1);

        await SeedBibliographyEntryAsync(doc.Id, "smith2024", "article",
            """{"title":"Deep Learning Advances","author":"Smith, John","year":"2024","journal":"Nature AI"}""");

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\cite{smith2024}");
        // Should have either biblatex or bibtex bibliography reference
        var hasBibliography = latex.Contains(@"\bibliography{") || latex.Contains(@"\begin{thebibliography}");
        hasBibliography.Should().BeTrue("LaTeX output should contain a bibliography reference");
    }

    [Fact]
    public async Task TableWithMath_BooktabsFormatting()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "table",
            """{"headers":["Variable","Value","Unit"],"rows":[["$x$","1.5","m/s"],["$y$","2.3","kg"],["$z$","0.7","N"]],"caption":"Measurement results"}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{table}");
        latex.Should().Contain(@"\toprule");
        latex.Should().Contain(@"\midrule");
        latex.Should().Contain(@"\bottomrule");
        latex.Should().Contain(@"\end{table}");
    }

    [Fact]
    public async Task CodeBlock_PythonWithListings()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "code",
            """{"code":"def fibonacci(n):\n    if n <= 1:\n        return n\n    return fibonacci(n-1) + fibonacci(n-2)","language":"python"}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{lstlisting}");
        latex.Should().Contain("language=python", "code block should specify the language");
        latex.Should().Contain("def fibonacci(n):");
        latex.Should().Contain(@"\end{lstlisting}");
    }

    [Fact]
    public async Task UnorderedList_ItemizeEnvironment()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "list",
            """{"items":["First item","Second item","Third item"],"ordered":false}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{itemize}");
        latex.Should().Contain(@"\item");
        latex.Should().Contain("First item");
        latex.Should().Contain("Second item");
        latex.Should().Contain("Third item");
        latex.Should().Contain(@"\end{itemize}");
    }

    [Fact]
    public async Task OrderedList_EnumerateEnvironment()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "list",
            """{"items":["Alpha","Beta","Gamma"],"ordered":true}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{enumerate}");
        latex.Should().Contain(@"\item");
        latex.Should().Contain("Alpha");
        latex.Should().Contain("Beta");
        latex.Should().Contain("Gamma");
        latex.Should().Contain(@"\end{enumerate}");
    }

    [Fact]
    public async Task TheoremAndProof()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "theorem",
            """{"theoremType":"theorem","text":"If $p$ is prime and $p | ab$, then $p | a$ or $p | b$.","title":"Euclid's Lemma"}""", 0);
        await SeedBlockAsync(doc.Id, "theorem",
            """{"theoremType":"proof","text":"Suppose $p \\nmid a$. Then $\\gcd(p, a) = 1$..."}""", 1);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{theorem}");
        latex.Should().Contain(@"\end{theorem}");
        latex.Should().Contain(@"\begin{proof}");
        latex.Should().Contain(@"\end{proof}");
    }

    [Fact]
    public async Task Abstract_AbstractEnvironment()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "abstract",
            """{"text":"This paper presents a novel approach to distributed computing using quantum entanglement protocols."}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\begin{abstract}");
        latex.Should().Contain("novel approach to distributed computing");
        latex.Should().Contain(@"\end{abstract}");
    }

    [Fact]
    public async Task PageBreak_NewPage()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Content before break."}""", 0);
        await SeedBlockAsync(doc.Id, "pageBreak", """{}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Content after break."}""", 2);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain("Content before break.");
        latex.Should().Contain(@"\newpage");
        latex.Should().Contain("Content after break.");
    }

    [Fact]
    public async Task SpecialCharacterEscaping()
    {
        var doc = await SeedDocWithUser();
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Special chars: 100% profit & 50# items with under_scores."}""", 0);

        var latex = await GetMainTexFromExport(Client, doc.Id);

        latex.Should().Contain(@"\%");
        latex.Should().Contain(@"\&");
        latex.Should().Contain(@"\#");
        latex.Should().Contain(@"\_");
    }

    [Fact]
    public async Task FullAcademicPaper_AllElementsPresent()
    {
        var doc = await SeedDocWithUser("A Complete Academic Paper");
        var order = 0;

        // Title section
        await SeedBlockAsync(doc.Id, "heading",
            """{"text":"Quantum Computing in Modern Cryptography","level":1}""", order++);

        // Abstract
        await SeedBlockAsync(doc.Id, "abstract",
            """{"text":"We present a comprehensive analysis of quantum computing applications in cryptographic systems."}""", order++);

        // Introduction
        await SeedBlockAsync(doc.Id, "heading",
            """{"text":"Introduction","level":2}""", order++);
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Recent advances in quantum computing @cite{smith2024} have raised concerns about current cryptographic standards."}""", order++);

        // Core equation
        await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"H|\\psi\\rangle = E|\\psi\\rangle","mode":"display","label":"eq:schrodinger","numbered":true}""", order++);

        // Results table
        await SeedBlockAsync(doc.Id, "heading",
            """{"text":"Results","level":2}""", order++);
        await SeedBlockAsync(doc.Id, "table",
            """{"headers":["Algorithm","Classical","Quantum"],"rows":[["RSA-2048","Secure","Broken"],["AES-256","Secure","Weakened"],["Lattice","Secure","Secure"]],"caption":"Security comparison"}""", order++);

        // Bibliography
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", order++);

        await SeedBibliographyEntryAsync(doc.Id, "smith2024", "article",
            """{"title":"Post-Quantum Cryptography","author":"Smith, Alice","year":"2024","journal":"Journal of Cryptology"}""");

        var latex = await GetMainTexFromExport(Client, doc.Id);

        // Document structure
        latex.Should().Contain(@"\documentclass");
        latex.Should().Contain(@"\begin{document}");
        latex.Should().Contain(@"\end{document}");

        // Sections
        latex.Should().Contain(@"\section{");
        latex.Should().Contain(@"\subsection{");

        // Abstract
        latex.Should().Contain(@"\begin{abstract}");
        latex.Should().Contain("quantum computing applications");

        // Citation
        latex.Should().Contain(@"\cite{smith2024}");

        // Equation with label
        latex.Should().Contain(@"\begin{equation}");
        latex.Should().Contain(@"\label{eq:schrodinger}");

        // Table
        latex.Should().Contain(@"\begin{table}");
        latex.Should().Contain("RSA-2048");

        // Bibliography
        var hasBibliography = latex.Contains(@"\bibliography{") || latex.Contains(@"\begin{thebibliography}");
        hasBibliography.Should().BeTrue("full paper should include bibliography");
    }
}
