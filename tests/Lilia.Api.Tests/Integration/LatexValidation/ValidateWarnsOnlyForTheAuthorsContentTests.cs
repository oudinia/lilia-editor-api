using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.LatexValidation;

/// <summary>
/// Validation speaks about what the author wrote.
///
/// <para>Measured 26 Sep: a document with one clean equation and no author
/// validated with "LaTeX Warning: No \author given." — the render path left
/// \author out when empty, where the export writes \author{}. Every document
/// without an author came back with a warning, so the rail's Validate dot lit
/// for nothing the author did.</para>
/// </summary>
[Collection("Integration")]
public class ValidateWarnsOnlyForTheAuthorsContentTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public ValidateWarnsOnlyForTheAuthorsContentTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_clean_document_without_an_author_validates_without_warnings()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "No author here");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Plain prose."}""", 1);
        await SeedBlockAsync(doc.Id, "equation", """{"latex":"\\frac{a}{b}"}""", 2);

        var response = await Client.PostAsync($"/api/latex/{doc.Id}/validate", null);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        result.GetProperty("valid").GetBoolean().Should().BeTrue();
        result.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().NotContain(w => w!.Contains("author", StringComparison.OrdinalIgnoreCase));
    }
}
