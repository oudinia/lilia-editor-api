using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Tables;

/// <summary>
/// A copy remembers where it came from, and the original counts its copies
/// (Olivia, tables-modern 1e: "1 copy was made from this table" in the tool,
/// "remembers where it came from" at insert). <c>copied_from</c> was recorded
/// already; now it is read back.
/// </summary>
[Collection("Integration")]
public class TableProvenanceTests : IntegrationTestBase
{
    private const string Author = "test_user_tables_provenance";

    public TableProvenanceTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static object Body(string caption, Guid? copiedFrom = null) => new
    {
        caption,
        label = "",
        content = new { headers = new[] { "Model", "Top-1" }, rows = new[] { new[] { "ResNet", "76.1" } } },
        copiedFrom,
    };

    private static async Task<JsonElement> JsonOf(HttpResponseMessage res) =>
        await res.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task A_copy_says_what_it_was_copied_from_in_the_list_and_on_its_own()
    {
        await SeedUserAsync(Author);
        using var client = CreateClientAs(Author);
        var original = (await JsonOf(await client.PostAsJsonAsync("/api/tables", Body("Top-1")))).GetProperty("id").GetGuid();

        var made = await client.PostAsJsonAsync("/api/tables", Body("Top-1", original));
        made.StatusCode.Should().Be(HttpStatusCode.Created);
        var copy = await JsonOf(made);
        copy.GetProperty("copiedFrom").GetGuid().Should().Be(original);
        var copyId = copy.GetProperty("id").GetGuid();

        var one = await JsonOf(await client.GetAsync($"/api/tables/{copyId}"));
        one.GetProperty("copiedFrom").GetGuid().Should().Be(original);

        var list = await JsonOf(await client.GetAsync("/api/tables"));
        var rows = list.EnumerateArray().ToList();
        rows.Single(r => r.GetProperty("id").GetGuid() == copyId).GetProperty("copiedFrom").GetGuid().Should().Be(original);
        var first = rows.Single(r => r.GetProperty("id").GetGuid() == original);
        (first.TryGetProperty("copiedFrom", out var from) && from.ValueKind != JsonValueKind.Null)
            .Should().BeFalse("an original is not a copy");
    }

    [Fact]
    public async Task Where_a_table_is_used_counts_its_copies_and_forgets_deleted_ones()
    {
        await SeedUserAsync(Author);
        using var client = CreateClientAs(Author);
        var original = (await JsonOf(await client.PostAsJsonAsync("/api/tables", Body("Top-1")))).GetProperty("id").GetGuid();

        var usage = await JsonOf(await client.GetAsync($"/api/tables/{original}/documents"));
        usage.GetProperty("copies").GetInt32().Should().Be(0);

        await client.PostAsJsonAsync("/api/tables", Body("Top-1", original));
        var second = (await JsonOf(await client.PostAsJsonAsync("/api/tables", Body("Top-1", original)))).GetProperty("id").GetGuid();
        usage = await JsonOf(await client.GetAsync($"/api/tables/{original}/documents"));
        usage.GetProperty("copies").GetInt32().Should().Be(2);
        usage.GetProperty("total").GetInt32().Should().Be(0, "a copy is a separate table, not a paper using this one");

        (await client.DeleteAsync($"/api/tables/{second}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        usage = await JsonOf(await client.GetAsync($"/api/tables/{original}/documents"));
        usage.GetProperty("copies").GetInt32().Should().Be(1);
    }
}
