using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// The Markdown export prints a reference's number when the document's last PDF compile gave one (10 Nov 2026,
/// launch bot #49: "[tab:x](#tab:x)" where the PDF says "Table 1"). Without a compile, the link to the label stays.
/// </summary>
public class MarkdownReferenceNumbersTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string Text = "As Table~(tab:recall) and Equation~(eq:loss) show, see \\ref{tab:recall}, \\eqref{eq:loss} and \\ref{fig:none}.";

    public MarkdownReferenceNumbersTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> PaperAsync(string? numbers)
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Numbers");
        await SeedBlockAsync(doc.Id, "paragraph", JsonSerializer.Serialize(new { text = Text }), 0);
        if (numbers is not null)
        {
            await using var db = CreateDbContext();
            await db.Documents.Where(d => d.Id == doc.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.LabelNumbers, numbers));
        }
        return doc.Id;
    }

    [Fact]
    public async Task With_a_compiled_PDF_references_print_its_numbers()
    {
        var id = await PaperAsync("""{"tab:recall":{"n":"2","p":3},"eq:loss":{"n":"1","p":2}}""");

        var md = await Client.GetStringAsync($"/api/documents/{id}/export/markdown");

        md.Should().Contain("As Table 2 and Equation (1) show, see 2, (1) and [fig:none](#fig:none).");
    }

    [Fact]
    public async Task Without_one_they_stay_links_to_their_labels()
    {
        var id = await PaperAsync(null);

        var md = await Client.GetStringAsync($"/api/documents/{id}/export/markdown");

        md.Should().Contain("As Table [tab:recall](#tab:recall) and Equation [eq:loss](#eq:loss) show, see [tab:recall](#tab:recall), [eq:loss](#eq:loss)");
    }
}
