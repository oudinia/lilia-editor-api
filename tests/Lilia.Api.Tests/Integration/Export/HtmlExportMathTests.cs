using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// An exported HTML file has to typeset its own mathematics.
///
/// <para>It did not. The page shipped no maths renderer of any kind — no
/// MathJax, no KaTeX, not one script tag — so every equation reached the
/// reader as literal dollar signs: <c>$a^2 + b^2 = c^2.$</c> set as prose. For
/// a LaTeX-first editor that meant the HTML export worked for prose and failed
/// at the subject matter (2026-09-09).</para>
///
/// <para>KaTeX now travels inside the file: stylesheet, script and woff2 fonts
/// as data URIs, so the page renders from disk with no network. That costs
/// about 627 KB, which is why a document with no mathematics does not carry
/// it.</para>
/// </summary>
[Collection("Integration")]
public class HtmlExportMathTests : IntegrationTestBase
{
    private readonly string _userId = $"html-math-{Guid.NewGuid():N}"[..28];

    public HtmlExportMathTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private async Task<(HttpStatusCode Status, string Html)> ExportAsync(Guid docId)
    {
        var response = await CreateClientAs(_userId).GetAsync($"/api/documents/{docId}/export/html");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ── the renderer travels with the file ───────────────────────────────

    [Fact]
    public async Task ADocumentWithMathsCarriesItsOwnRenderer()
    {
        var doc = await SeedDocumentAsync(_userId, "With maths");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"The identity $a^2 + b^2 = c^2$ is standard."}""");

        var (status, html) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("katex", "the equation cannot render without a renderer");
        html.Should().Contain("renderMathInElement", "and something has to invoke it");
        html.Should().Contain("a^2 + b^2 = c^2", "the source stays, for KaTeX to typeset");
    }

    [Fact]
    public async Task TheFontsTravelToo()
    {
        // A stylesheet pointing at fonts/KaTeX_Math-Italic.woff2 is a 404 for
        // every glyph once the file is sitting in someone's downloads folder.
        var doc = await SeedDocumentAsync(_userId, "Fonts");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Let $x$ be given."}""");

        var (_, html) = await ExportAsync(doc.Id);

        html.Should().Contain("data:font/woff2", "the fonts must be inside the file");
        html.Should().NotContain("url(fonts/", "no reference may point outside the file");
    }

    [Fact]
    public async Task NothingIsFetchedFromTheNetwork()
    {
        // The whole reason for inlining. A CDN script is one line and stops
        // working the moment the reader is offline.
        var doc = await SeedDocumentAsync(_userId, "Offline");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Given $E = mc^2$."}""");

        var (_, html) = await ExportAsync(doc.Id);

        html.Should().NotContain("cdn.jsdelivr.net");
        html.Should().NotContain("unpkg.com");
        html.Should().NotContain("cdnjs.cloudflare.com");
    }

    [Fact]
    public async Task DisplayMathIsCarriedToo()
    {
        var doc = await SeedDocumentAsync(_userId, "Display");
        await SeedBlockAsync(doc.Id, "equation", """{"source":"\\frac{a}{b}","displayMode":true}""");

        var (_, html) = await ExportAsync(doc.Id);

        html.Should().Contain("katex");
        html.Should().Contain(@"\frac{a}{b}");
    }

    // ── and only when it is needed ───────────────────────────────────────

    [Fact]
    public async Task ADocumentWithoutMathsStaysSmall()
    {
        var doc = await SeedDocumentAsync(_userId, "Prose only");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Nothing here needs typesetting beyond the words themselves."}""");

        var (status, html) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        html.Should().NotContain("katex", "627 KB is a lot to carry for no equations");
        html.Length.Should().BeLessThan(50_000);
    }

    [Fact]
    public async Task CodeBlocksAreLeftAlone()
    {
        // A paper about shell scripting must not have its prompts typeset as
        // algebra. The auto-render pass ignores pre and code; this pins that
        // the code itself still arrives intact.
        var doc = await SeedDocumentAsync(_userId, "Shell");
        await SeedBlockAsync(doc.Id, "code",
            """{"code":"echo $HOME && echo $PATH","language":"bash"}""");

        var (status, html) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("$HOME");
        html.Should().Contain("ignoredTags", "the renderer must be told to skip code");
    }

    // ── the title it exposed ─────────────────────────────────────────────

    [Fact]
    public async Task ADocumentWithATitleBlockDoesNotPrintItTwice()
    {
        // The wrapper added an <h1> unconditionally, on top of the one the
        // title block renders — so every document with a title showed it
        // twice, one above the other.
        var doc = await SeedDocumentAsync(_userId, "DUPLICATETITLE");
        await SeedBlockAsync(doc.Id, "title",
            """{"title":"DUPLICATETITLE","author":"An Author","date":"2026"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 1);

        var (_, html) = await ExportAsync(doc.Id);

        System.Text.RegularExpressions.Regex.Matches(html, "DUPLICATETITLE")
            .Count.Should().BeLessThanOrEqualTo(2,
                "once in <title>, once in the body — not three times");
        // Assert on the element, not the string — the stylesheet carries a
        // .lilia-doc-title rule whether or not anything uses it.
        html.Should().NotContain("""<h1 class="lilia-doc-title">""",
            "the wrapper heading is redundant when the document renders its own");
    }

    [Fact]
    public async Task ADocumentWithNoTitleBlockStillGetsAHeading()
    {
        var doc = await SeedDocumentAsync(_userId, "HEADINGFALLBACK");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body only."}""");

        var (_, html) = await ExportAsync(doc.Id);

        html.Should().Contain("""<h1 class="lilia-doc-title">""",
            "without a title block the wrapper supplies the heading");
        html.Should().Contain("HEADINGFALLBACK");
    }
}
