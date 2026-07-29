using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Integration.LatexValidation;

/// <summary>
/// A table taller than the page runs off the bottom, and LaTeX says so only as
/// <c>Overfull \vbox</c> — which was being filtered out alongside its cosmetic
/// namesakes.
///
/// <para>
/// The distinction that was lost: <c>Overfull \hbox</c> means a line is a few
/// points too wide (cosmetic, correctly filtered). <c>Overfull \vbox</c> means
/// content is too TALL for the page. A plain <c>tabular</c> cannot break across
/// pages, so once it exceeds the text height the rest simply falls off — and
/// the compile still exits 0.
/// </para>
///
/// <para>
/// This is the end-to-end proof of that fix, and the reproduction recipe if
/// anyone needs to see the failure by hand.
/// </para>
/// </summary>
[Collection("Integration")]
public class PageOverflowTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _out;
    private readonly string _userId = $"overflow-{Guid.NewGuid():N}"[..28];

    public PageOverflowTests(TestDatabaseFixture fixture, ITestOutputHelper output) : base(fixture)
        => _out = output;

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    /// <summary>
    /// A4 with default margins holds roughly 45 lines of body text. 90 rows is
    /// comfortably past that with room for the header, so the overflow does not
    /// depend on exact font metrics.
    /// </summary>
    private const int RowsThatOverflowAPage = 90;

    private static string TallTableJson(int rows) => JsonSerializer.Serialize(new
    {
        caption = "Measurements",
        headers = new[] { "Sample", "Reading", "Notes" },
        rows = Enumerable.Range(1, rows)
            .Select(i => new[] { $"S-{i:D3}", $"{i * 1.5:0.0}", "within tolerance" })
            .ToArray(),
    });

    [Fact]
    public async Task A_table_taller_than_the_page_is_reported_not_silently_truncated()
    {
        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Overflowing table");

        var block = await SeedBlockAsync(doc.Id, type: "table",
            contentJson: TallTableJson(RowsThatOverflowAPage), sortOrder: 0);

        var resp = await client.PostAsync($"/api/latex/block/{block.Id}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        _out.WriteLine(body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var warnings = root.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : [];

        // As with dropped glyphs: failing is acceptable, passing SILENTLY is not.
        if (root.GetProperty("valid").GetBoolean())
        {
            warnings.Should().Contain(x => x.Contains("too tall for the page", StringComparison.OrdinalIgnoreCase),
                "a 90-row tabular cannot fit on one page, and the overflow must reach the user "
              + "rather than being filtered as cosmetic noise");
        }
        else
        {
            root.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// The other half. A short table must stay clean — a check that fires on
    /// everything is as useless as one that never fires, and the per-block
    /// "compiles" indicator would never read green.
    /// </summary>
    [Fact]
    public async Task A_table_that_fits_produces_no_overflow_warning()
    {
        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Small table");

        var block = await SeedBlockAsync(doc.Id, type: "table",
            contentJson: TallTableJson(rows: 5), sortOrder: 0);

        var resp = await client.PostAsync($"/api/latex/block/{block.Id}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var warnings = json.RootElement.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : [];

        warnings.Should().NotContain(x => x.Contains("too tall for the page", StringComparison.OrdinalIgnoreCase));
    }
}
