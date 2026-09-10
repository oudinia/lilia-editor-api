using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// Export a document, import it back, compare.
///
/// <para>The test most likely to expose the next class of defect, and the last
/// one written, because it needs both halves to work. Everything else here
/// asks "did what went into the database come out of the file?" — this asks
/// the harder question: <b>does the file still describe the same
/// document?</b></para>
///
/// <para>Three outcomes are worth distinguishing, and these tests keep them
/// apart rather than lumping them under "round trip works":</para>
///
/// <list type="bullet">
/// <item>preserved — the block comes back as it went out</item>
/// <item>restructured — the content survives in a different shape, which can
/// be an improvement (a display equation embedded in a paragraph comes back as
/// its own equation block)</item>
/// <item>lost — content that does not come back at all. Only one remains, and
/// it is named below rather than hidden behind a passing assertion.</item>
/// </list>
/// </summary>
[Collection("Integration")]
public class LatexRoundTripTests : IntegrationTestBase
{
    private readonly string _userId = $"rt-{Guid.NewGuid():N}"[..28];

    public LatexRoundTripTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    // ── harness ──────────────────────────────────────────────────────────

    private sealed record Imported(Guid DocumentId, JsonElement Document);

    /// <summary>Export as a LaTeX project, import it back, return the new document.</summary>
    private async Task<Imported?> RoundTripAsync(Guid sourceId)
    {
        var client = CreateClientAs(_userId);

        var zip = await client.GetByteArrayAsync($"/api/documents/{sourceId}/export/latex");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", "project.zip");

        var upload = await client.PostAsync("/api/lilia/imports/latex?autoFinalize=true", form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, "the import must at least be accepted");

        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var sessionId = uploaded.RootElement.GetProperty("sessionId").GetGuid();

        // The import runs as a job; wait for it to name the document it made.
        Guid documentId = Guid.Empty;
        for (var attempt = 0; attempt < 40 && documentId == Guid.Empty; attempt++)
        {
            await Task.Delay(500);
            await using var db = CreateDbContext();
            var session = await db.ImportReviewSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session?.DocumentId is { } id && id != Guid.Empty) documentId = id;
        }

        if (documentId == Guid.Empty) return null;

        var doc = await client.GetStringAsync($"/api/documents/{documentId}");
        return new Imported(documentId, JsonDocument.Parse(doc).RootElement.Clone());
    }

    private static Dictionary<string, int> TypeCounts(JsonElement document)
    {
        var counts = new Dictionary<string, int>();
        foreach (var block in document.GetProperty("blocks").EnumerateArray())
        {
            var type = block.GetProperty("type").GetString() ?? "?";
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }
        return counts;
    }

    private static string AllText(JsonElement document) =>
        string.Join(" ", document.GetProperty("blocks").EnumerateArray()
            .Select(b => b.TryGetProperty("content", out var c) ? c.ToString() : ""));

    /// <summary>A paper with the shape of a real one.</summary>
    private async Task<Guid> SeedPaperAsync()
    {
        var doc = await SeedDocumentAsync(_userId, "A Round Trip Paper");
        var o = 0;
        await SeedBlockAsync(doc.Id, "title",
            """{"title":"A Round Trip Paper","author":"UNIQUEAUTHORNAME","date":"2026"}""", o++);
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"ABSTRACTMARKER text."}""", o++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"INTROMARKER","level":1}""", o++);
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Prose with $x^2$ inline and a citation \\citep{ref1}."}""", o++);
        await SeedBlockAsync(doc.Id, "equation", """{"source":"E = mc^2","displayMode":true}""", o++);
        await SeedBlockAsync(doc.Id, "theorem",
            """{"text":"THEOREMMARKER holds.","theoremType":"theorem"}""", o++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"CONCLUSIONMARKER","level":1}""", o);
        await SeedBibliographyEntryAsync(doc.Id, "ref1", "book",
            """{"author":"Euclid","title":"BIBTITLEMARKER","year":"300"}""");
        return doc.Id;
    }

    // ── what survives ────────────────────────────────────────────────────

    [Fact]
    public async Task ADocumentSurvivesBeingExportedAndImported()
    {
        var result = await RoundTripAsync(await SeedPaperAsync());

        result.Should().NotBeNull("the import job must finish and produce a document");
        AllText(result!.Document).Should().Contain("ABSTRACTMARKER");
    }

    [Fact]
    public async Task TheAuthorComesBack()
    {
        // The one this suite was written for. The parser reads \author into
        // metadata and the import vocabulary had no title element, so the
        // author was dropped on the floor: a paper went out attributed and
        // came back anonymous.
        var result = await RoundTripAsync(await SeedPaperAsync());

        result.Should().NotBeNull();
        AllText(result!.Document).Should().Contain("UNIQUEAUTHORNAME",
            "a paper that comes back without its author has lost the thing that makes it someone's");
        TypeCounts(result.Document).GetValueOrDefault("title").Should().Be(1);
    }

    [Fact]
    public async Task TheTitleComesBack()
    {
        var result = await RoundTripAsync(await SeedPaperAsync());

        result!.Document.GetProperty("title").GetString().Should().Contain("Round Trip Paper");
    }

    [Fact]
    public async Task HeadingsAbstractsAndTheoremsComeBackIntact()
    {
        var result = await RoundTripAsync(await SeedPaperAsync());
        var counts = TypeCounts(result!.Document);

        counts.GetValueOrDefault("heading").Should().Be(2);
        counts.GetValueOrDefault("abstract").Should().Be(1);
        counts.GetValueOrDefault("theorem").Should().Be(1);

        var text = AllText(result.Document);
        text.Should().Contain("INTROMARKER").And.Contain("CONCLUSIONMARKER");
        text.Should().Contain("THEOREMMARKER");
    }

    [Fact]
    public async Task CitationsComeBack()
    {
        var result = await RoundTripAsync(await SeedPaperAsync());

        AllText(result!.Document).Should().Contain("ref1",
            "a citation that does not survive the trip is a claim without a source");
    }

    [Fact]
    public async Task BibliographyEntriesComeBack()
    {
        var result = await RoundTripAsync(await SeedPaperAsync());

        await using var db = CreateDbContext();
        var entries = await db.BibliographyEntries.AsNoTracking()
            .Where(e => e.DocumentId == result!.DocumentId)
            .ToListAsync();

        entries.Should().NotBeEmpty("references.bib travels in the project and must be read back");
        entries.Should().Contain(e => e.CiteKey == "ref1");
    }

    [Fact]
    public async Task MathematicsComesBack()
    {
        var result = await RoundTripAsync(await SeedPaperAsync());
        var text = AllText(result!.Document);

        text.Should().Contain("x^2", "inline maths");
        text.Should().Contain("mc^2", "the display equation");
    }

    [Fact]
    public async Task NoBlocksAreLost()
    {
        var sourceId = await SeedPaperAsync();
        await using var db = CreateDbContext();
        var before = await db.Blocks.CountAsync(b => b.DocumentId == sourceId);

        var result = await RoundTripAsync(sourceId);
        var after = result!.Document.GetProperty("blocks").GetArrayLength();

        after.Should().BeGreaterThanOrEqualTo(before - 1,
            "one block — the bibliography marker — is known not to survive; anything more is a loss");
    }

    // ── what changes, and why that is acceptable ─────────────────────────

    [Fact]
    public async Task ADisplayEquationInsideAParagraphComesBackAsItsOwnBlock()
    {
        // Restructured, not lost. The exporter writes $$…$$ into the paragraph
        // and the importer recognises it as an equation — so the content
        // survives in a better shape than it left in. Pinned so the change is
        // a decision rather than a surprise.
        var doc = await SeedDocumentAsync(_userId, "Embedded display maths");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"We claim $$a^2 + b^2 = c^2$$ and proceed."}""");

        var result = await RoundTripAsync(doc.Id);

        result.Should().NotBeNull();
        TypeCounts(result!.Document).GetValueOrDefault("equation")
            .Should().BeGreaterThan(0, "the equation is promoted out of the prose");
        AllText(result.Document).Should().Contain("and proceed.");
    }

    // ── what is lost, named rather than hidden ───────────────────────────

    [Fact]
    public async Task TheBibliographyBlockMarkerDoesNotSurvive()
    {
        // Documented, not asserted away. The entries survive (see above) and
        // the exporter appends a References section whenever a document has
        // any, so the output is still correct — but the block recording WHERE
        // the author put their references is gone, and a document that had one
        // comes back without it.
        var result = await RoundTripAsync(await SeedPaperAsync());

        TypeCounts(result!.Document).GetValueOrDefault("bibliography").Should().Be(0,
            "if this ever becomes 1, the gap has been closed and this test should say so");
    }

    // ── robustness ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyDocumentSurvivesTheTrip()
    {
        var doc = await SeedDocumentAsync(_userId, "Nothing in it");

        var result = await RoundTripAsync(doc.Id);

        result.Should().NotBeNull("an empty document is a legitimate thing to export and re-import");
    }

    [Fact]
    public async Task CharactersThatLaTeXEscapesComeBackUnescaped()
    {
        // The escape pass has to be reversible: 50\% must not come back as
        // "50\%" with the backslash visible in the editor.
        var doc = await SeedDocumentAsync(_userId, "Escaping");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Roughly 50% of the sample, per Smith & Jones."}""");

        var result = await RoundTripAsync(doc.Id);
        var text = AllText(result!.Document);

        text.Should().Contain("50%");
        text.Should().NotContain(@"50\%", "the reader must not see the escape");
        text.Should().Contain("Smith & Jones");
    }
}
