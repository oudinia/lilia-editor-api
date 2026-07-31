using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Integration.LatexValidation;

/// <summary>
/// End-to-end cover for the failure this whole change exists to catch: LaTeX
/// reporting success while silently deleting text.
///
/// These run against the full API and a real LaTeX binary, so they assert the
/// behaviour a user actually gets — not the behaviour of a parser in isolation.
/// A unit test over a captured log proves the regex; only this proves the
/// warning survives the compile, the classifier, the cache and the JSON.
/// </summary>
[Collection("Integration")]
public class SilentTextLossTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _out;
    private readonly string _userId = $"silent-{Guid.NewGuid():N}"[..28];

    public SilentTextLossTests(TestDatabaseFixture fixture, ITestOutputHelper output) : base(fixture)
        => _out = output;

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    /// <summary>
    /// The case the fix exists for, and it is narrower than it first appears.
    ///
    /// Under **pdfLaTeX**, an unmapped Unicode character is a HARD ERROR —
    /// inputenc aborts with "Unicode character 结 (U+7ED3)" and no PDF. Loud,
    /// and already handled.
    ///
    /// Under **LuaLaTeX / XeLaTeX** it is only a warning: TeX drops the
    /// character, writes "Missing character:" to the log, and exits 0 with a
    /// valid PDF. That is the silent path, and it is reached by any document
    /// the font picker has touched — because <c>\setmainfont</c> forces the
    /// engine up. Pick a serif face, type an accented name or a Greek letter it
    /// lacks, and the text vanishes with a green tick beside it.
    /// </summary>
    [Fact]
    public async Task Dropped_glyphs_are_reported_even_though_the_compile_succeeds()
    {
        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Silent loss");
        await SetDocumentEngineAsync(doc.Id, "lualatex");

        var block = await SeedBlockAsync(doc.Id, type: "paragraph",
            contentJson: JsonSerializer.Serialize(new { text = "The result is 42. 结果是四十二。" }),
            sortOrder: 0);

        var resp = await client.PostAsync($"/api/latex/block/{block.Id}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        _out.WriteLine(body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var warnings = root.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : [];

        // Whether it compiles depends on the local TeX installation, but it must
        // never come back clean — silence is the bug, not failure.
        if (root.GetProperty("valid").GetBoolean())
        {
            warnings.Should().Contain(x => x.Contains("dropped", StringComparison.OrdinalIgnoreCase),
                "a compile that deleted every CJK character must say so, in words an author can act on");
        }
        else
        {
            root.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace(
                "if it failed instead, the reason must still reach the user");
        }
    }

    private async Task SetDocumentEngineAsync(Guid documentId, string engine)
    {
        await using var db = CreateDbContext();
        var doc = await db.Documents.FindAsync(documentId);
        doc!.LatexEngine = engine;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The counterpart, and the one that keeps the fix honest: ordinary Latin
    /// prose must stay clean. A glyph check that fires on everything is as
    /// useless as one that never fires — the per-block "compiles" indicator
    /// would never read green and authors would learn to ignore it.
    /// </summary>
    [Fact]
    public async Task Ordinary_prose_produces_no_dropped_glyph_warning()
    {
        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Clean prose");

        var block = await SeedBlockAsync(doc.Id, type: "paragraph",
            contentJson: JsonSerializer.Serialize(new { text = "The result is 42, give or take." }),
            sortOrder: 0);

        var resp = await client.PostAsync($"/api/latex/block/{block.Id}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var warnings = json.RootElement.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : [];

        warnings.Should().NotContain(x => x.Contains("dropped", StringComparison.OrdinalIgnoreCase));
    }
}
