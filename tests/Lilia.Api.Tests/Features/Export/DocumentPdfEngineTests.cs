using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// How a document's PDF is made, chosen per document (the owner, 14 Nov 2026, the launch bot's #101): Automatic (Typst
/// for a quick preview when it can print everything, LaTeX otherwise) or Always LaTeX. A one-off engine named in the
/// request (the preview's switch, Export → PDF) still wins.
/// </summary>
public class DocumentPdfEngineTests : IntegrationTestBase
{
    public DocumentPdfEngineTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<(HttpClient Client, Guid Id)> PlainDocumentAsync(string who)
    {
        var user = $"pdfeng_{who}_{Guid.NewGuid():N}"[..36];
        await SeedUserAsync(user);
        var doc = await SeedDocumentAsync(user, "Engine");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Plain prose that Typst prints as LaTeX would."}""");
        return (CreateClientAs(user), doc.Id);
    }

    private static async Task<string?> EngineSettingAsync(HttpClient client, Guid id) =>
        JsonDocument.Parse(await client.GetStringAsync($"/api/documents/{id}")).RootElement.GetProperty("pdfEngine").GetString();

    [Fact]
    public async Task The_setting_is_automatic_by_default_and_keeps_only_known_values()
    {
        var (client, id) = await PlainDocumentAsync("setting");
        (await EngineSettingAsync(client, id)).Should().Be("auto");

        (await client.PutAsJsonAsync($"/api/documents/{id}", new { pdfEngine = "latex" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EngineSettingAsync(client, id)).Should().Be("latex");

        (await client.PutAsJsonAsync($"/api/documents/{id}", new { pdfEngine = "typo" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EngineSettingAsync(client, id)).Should().Be("latex", "an unknown value is ignored");

        (await client.PutAsJsonAsync($"/api/documents/{id}", new { pdfEngine = "auto" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EngineSettingAsync(client, id)).Should().Be("auto");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Always_LaTeX_makes_the_PDF_with_LaTeX_and_a_named_engine_still_wins()
    {
        var (client, id) = await PlainDocumentAsync("export");
        var auto = await client.GetAsync($"/api/documents/{id}/export/pdf");
        auto.StatusCode.Should().Be(HttpStatusCode.OK, await auto.Content.ReadAsStringAsync());
        auto.Headers.GetValues("X-Render-Engine").Single().Should().Be("typst", "plain prose takes Typst's quick path");

        await client.PutAsJsonAsync($"/api/documents/{id}", new { pdfEngine = "latex" });
        var latex = await client.GetAsync($"/api/documents/{id}/export/pdf");
        latex.StatusCode.Should().Be(HttpStatusCode.OK, await latex.Content.ReadAsStringAsync());
        latex.Headers.GetValues("X-Render-Engine").Single().Should().Be("pdflatex");

        var named = await client.GetAsync($"/api/documents/{id}/export/pdf?engine=typst");
        named.StatusCode.Should().Be(HttpStatusCode.OK);
        named.Headers.GetValues("X-Render-Engine").Single().Should().Be("typst", "the preview's switch names an engine for this time only");

        // The switch's "LaTeX": the document's own LaTeX engine, from an Automatic document too.
        await client.PutAsJsonAsync($"/api/documents/{id}", new { pdfEngine = "auto" });
        var switched = await client.GetAsync($"/api/documents/{id}/export/pdf?engine=latex");
        switched.StatusCode.Should().Be(HttpStatusCode.OK, await switched.Content.ReadAsStringAsync());
        switched.Headers.GetValues("X-Render-Engine").Single().Should().Be("pdflatex");
    }
}
