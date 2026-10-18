using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Workflow;

/// <summary>
/// Working with a document, through the API only, as the editor does (the user, 18 Oct: "a few e2e
/// and api tests for working with a document and preview"): write blocks, read the LaTeX preview,
/// compile the PDF, edit, cite, change settings, check. Every assertion reads what the author would
/// see, the preview's LaTeX or the PDF's text, never a status code alone.
///
/// <para>They also guard the compile caches of 18 Oct (API #330–#333): an unchanged document is not
/// compiled again, and an edit is never answered with the PDF from before it.</para>
/// </summary>
[Collection("Integration")]
public class DocumentPreviewWorkflowTests : IntegrationTestBase
{
    private readonly string _userId = $"workflow-{Guid.NewGuid():N}"[..28];
    private HttpClient _client = null!;

    public DocumentPreviewWorkflowTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        _client = CreateClientAs(_userId);
    }

    /// <summary>Answers from a cache: by document version (since 18 Oct) or by compile input.</summary>
    private static long CachedAnswers() => CompileCache.Versions.Hits + CompileCache.Shared.Hits;

    // ── the author's actions ─────────────────────────────────────────────

    private async Task<Guid> NewDocumentAsync(string title)
    {
        var res = await _client.PostAsJsonAsync("/api/documents", new { title });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddBlockAsync(Guid doc, string type, object content, int sortOrder)
    {
        var res = await _client.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type, content, sortOrder });
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task EditBlockAsync(Guid doc, Guid block, string type, object content) =>
        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/{block}", new { type, content })).EnsureSuccessStatusCode();

    private async Task<string> PreviewLatexAsync(Guid doc) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/documents/{doc}/preview/latex")).GetProperty("content").GetString()!;

    /// <summary>The PDF through pdflatex (the path the caches serve).</summary>
    private async Task<byte[]> PdfAsync(Guid doc, string engine = "pdflatex")
    {
        var res = await _client.GetAsync($"/api/documents/{doc}/export/pdf?engine={engine}");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes.Take(400).ToArray()));
        Encoding.Latin1.GetString(bytes, 0, 5).Should().StartWith("%PDF");
        return bytes;
    }

    private async Task<JsonElement> ValidateAsync(Guid doc)
    {
        var res = await _client.PostAsync($"/api/latex/{doc}/validate", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Tool(string tool, string args, byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-workflow-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var p = Process.Start(new ProcessStartInfo(tool, $"{args} \"{path}\"" + (tool == "pdftotext" ? " -" : ""))
            { RedirectStandardOutput = true, UseShellExecute = false })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
        finally { File.Delete(path); }
    }

    /// <summary>The PDF's text, whitespace collapsed (a line break is not a difference).</summary>
    private static string Text(byte[] pdf) => System.Text.RegularExpressions.Regex.Replace(Tool("pdftotext", "", pdf), @"\s+", " ");

    // ── writing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task What_the_author_writes_is_in_the_latex_preview_block_by_block_and_in_the_pdf()
    {
        var doc = await NewDocumentAsync("Workflow paper");
        var heading = await AddBlockAsync(doc, "heading", new { text = "Hierarchical model", level = 1 }, 0);
        var para = await AddBlockAsync(doc, "paragraph", new { text = "Each cohort is fitted separately with \\textbf{partial} pooling." }, 1);
        var eq = await AddBlockAsync(doc, "equation", new { latex = @"y_{ij} \sim \mathcal{N}(\alpha_j, \sigma^2)", display = true }, 2);
        var list = await AddBlockAsync(doc, "list", new { items = new[] { "Pool the estimates", "Check convergence" }, ordered = false }, 3);

        var tex = await PreviewLatexAsync(doc);
        tex.Should().Contain(@"\section{Hierarchical model}");
        tex.Should().Contain(@"\textbf{partial}");
        tex.Should().Contain(@"\begin{equation}");
        tex.Should().Contain(@"\item Pool the estimates");
        // Each block is marked, in the document's order: the LaTeX view's Blocks tab reads these.
        var marks = new[] { heading, para, eq, list }.Select(id => tex.IndexOf($"% block:{id}", StringComparison.Ordinal)).ToArray();
        marks.Should().OnlyContain(i => i > 0).And.BeInAscendingOrder();

        var text = Text(await PdfAsync(doc));
        text.Should().Contain("Workflow paper").And.Contain("Hierarchical model").And.Contain("partial pooling")
            .And.Contain("Pool the estimates").And.Contain("Check convergence");
    }

    [Fact]
    public async Task The_default_engine_also_gives_the_pdf()
    {
        var doc = await NewDocumentAsync("Auto engine");
        await AddBlockAsync(doc, "paragraph", new { text = "Whatever engine typesets this sentence." }, 0);
        Text(await PdfAsync(doc, "auto")).Should().Contain("Whatever engine typesets this sentence.");
    }

    // ── editing, and the caches ─────────────────────────────────────────

    [Fact]
    public async Task An_unchanged_document_is_not_compiled_again_and_an_edit_is_never_served_stale()
    {
        var doc = await NewDocumentAsync("Cache workflow");
        var para = await AddBlockAsync(doc, "paragraph", new { text = "The first version of this sentence." }, 0);

        var first = await PdfAsync(doc);
        var hits = CachedAnswers();
        var again = await PdfAsync(doc);
        again.Should().Equal(first, "the same document is the same PDF");
        CachedAnswers().Should().BeGreaterThan(hits, "the second request is served from the compile cache");

        await EditBlockAsync(doc, para, "paragraph", new { text = "The second version of this sentence." });
        var edited = Text(await PdfAsync(doc));
        edited.Should().Contain("The second version").And.NotContain("The first version");
        (await PreviewLatexAsync(doc)).Should().Contain("The second version").And.NotContain("The first version");
    }

    [Fact]
    public async Task Two_requests_for_the_same_pdf_at_once_both_get_it()
    {
        // The preview asks twice at once; the second save of the label numbers used to fail (#332).
        var doc = await NewDocumentAsync("Twice at once");
        await AddBlockAsync(doc, "heading", new { text = "Results", level = 1 }, 0);
        await AddBlockAsync(doc, "paragraph", new { text = "Asked for twice, compiled once." }, 1);

        var both = await Task.WhenAll(PdfAsync(doc), PdfAsync(doc));
        both[1].Should().Equal(both[0]);
        Text(both[0]).Should().Contain("Asked for twice, compiled once.");
    }

    [Fact]
    public async Task Reordering_and_deleting_blocks_follow_into_the_pdf()
    {
        var doc = await NewDocumentAsync("Order");
        var a = await AddBlockAsync(doc, "paragraph", new { text = "Alpha paragraph." }, 0);
        var b = await AddBlockAsync(doc, "paragraph", new { text = "Bravo paragraph." }, 1);
        var c = await AddBlockAsync(doc, "paragraph", new { text = "Charlie paragraph." }, 2);
        var text = Text(await PdfAsync(doc));
        text.IndexOf("Alpha").Should().BeLessThan(text.IndexOf("Bravo"));

        (await _client.PutAsJsonAsync($"/api/documents/{doc}/blocks/reorder", new { blockIds = new[] { c, a, b } })).EnsureSuccessStatusCode();
        text = Text(await PdfAsync(doc));
        text.IndexOf("Charlie").Should().BeLessThan(text.IndexOf("Alpha"));

        (await _client.DeleteAsync($"/api/documents/{doc}/blocks/{b}")).EnsureSuccessStatusCode();
        Text(await PdfAsync(doc)).Should().Contain("Charlie").And.Contain("Alpha").And.NotContain("Bravo");
    }

    // ── references ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_citation_prints_its_entry_an_unchanged_document_with_references_is_cached_and_a_new_citation_appears()
    {
        var doc = await NewDocumentAsync("Cited");
        foreach (var (key, title, author, year) in new[]
                 {
                     ("knuth84", "The TeXbook", "Knuth, Donald", "1984"),
                     ("lamport94", "A Document Preparation System", "Lamport, Leslie", "1994"),
                 })
        {
            var res = await _client.PostAsJsonAsync($"/api/documents/{doc}/bibliography", new
            {
                citeKey = key, entryType = "book",
                data = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { title, author, year, publisher = "Addison-Wesley" })),
            });
            res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        }
        var para = await AddBlockAsync(doc, "paragraph", new { text = @"As \cite{knuth84} shows, macros compose." }, 0);
        await AddBlockAsync(doc, "bibliography", new { entries = Array.Empty<object>(), title = "References" }, 1);

        var first = await PdfAsync(doc);
        var text = Text(first);
        text.Should().Contain("The TeXbook").And.NotContain("A Document Preparation System", "only what is cited is printed");
        text.Should().NotContain("[?]", "the citation resolved");

        // The bibliography file once carried the time it was written, so this never hit the cache (#332).
        var hits = CachedAnswers();
        (await PdfAsync(doc)).Should().Equal(first);
        CachedAnswers().Should().BeGreaterThan(hits);

        await EditBlockAsync(doc, para, "paragraph", new { text = @"As \cite{knuth84} and \cite{lamport94} show, macros compose." });
        Text(await PdfAsync(doc)).Should().Contain("The TeXbook").And.Contain("A Document Preparation System");
    }

    // ── settings ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Paper_size_and_the_author_s_own_preamble_reach_the_preview_and_the_pdf()
    {
        var doc = await NewDocumentAsync("Settings");
        await AddBlockAsync(doc, "paragraph", new { text = @"The reals: $\R$ and a custom word: \liliaword." }, 0);
        var res = await _client.PutAsJsonAsync($"/api/documents/{doc}", new
        {
            paperSize = "letter",
            customPreamble = "\\newcommand{\\R}{\\mathbb{R}}\n\\newcommand{\\liliaword}{Zanzibar}",
        });
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());

        var tex = await PreviewLatexAsync(doc);
        // The author's lines, marked: the LaTeX view's Preamble tab lists them last, tagged "You".
        var start = tex.IndexOf(Lilia.Engines.RenderService.YourPreambleStart, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        tex.IndexOf(@"\newcommand{\liliaword}{Zanzibar}", StringComparison.Ordinal).Should().BeGreaterThan(start);

        var pdf = await PdfAsync(doc);
        Text(pdf).Should().Contain("Zanzibar");
        Tool("pdfinfo", "", pdf).Should().Contain("612 x 792", "US Letter");

        // Changing only the author's lines: the next PDF has them (they are never in the precompiled preamble).
        (await _client.PutAsJsonAsync($"/api/documents/{doc}", new
        {
            customPreamble = "\\newcommand{\\R}{\\mathbb{R}}\n\\newcommand{\\liliaword}{Timbuktu}",
        })).EnsureSuccessStatusCode();
        Text(await PdfAsync(doc)).Should().Contain("Timbuktu").And.NotContain("Zanzibar");
    }

    // ── checking ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_check_names_the_broken_block_and_passes_once_it_is_fixed()
    {
        var doc = await NewDocumentAsync("Checked");
        await AddBlockAsync(doc, "paragraph", new { text = "A sound first paragraph." }, 0);
        var broken = await AddBlockAsync(doc, "paragraph", new { text = @"This one calls \liliadefinitelyundefined{x}." }, 1);

        var check = await ValidateAsync(doc);
        check.GetProperty("valid").GetBoolean().Should().BeFalse();
        check.GetProperty("errorBlockId").GetString().Should().Be(broken.ToString());
        check.GetProperty("error").GetString().Should().Contain("Undefined control sequence");

        await EditBlockAsync(doc, broken, "paragraph", new { text = "This one is fine now." });
        var fixedCheck = await ValidateAsync(doc);
        fixedCheck.GetProperty("valid").GetBoolean().Should().BeTrue(fixedCheck.ToString());

        // Asked again unchanged, the same answer.
        (await ValidateAsync(doc)).GetProperty("valid").GetBoolean().Should().BeTrue();
    }
}
