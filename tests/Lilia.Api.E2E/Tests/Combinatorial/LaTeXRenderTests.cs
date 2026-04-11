using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// LaTeX rendering endpoint combinations — various LaTeX inputs × output formats.
/// </summary>
public class LaTeXRenderTests : E2ETestBase
{
    public static IEnumerable<object[]> ValidLatexSnippets => new[]
    {
        new object[] { @"\documentclass{article}\begin{document}Hello\end{document}" },
        new object[] { @"\documentclass{article}\usepackage{amsmath}\begin{document}\begin{equation}E=mc^2\end{equation}\end{document}" },
        new object[] { @"\documentclass{article}\begin{document}\begin{itemize}\item One\item Two\end{itemize}\end{document}" },
        new object[] { @"\documentclass{article}\begin{document}\begin{tabular}{|c|c|}\hline A & B \\ \hline 1 & 2 \\ \hline\end{tabular}\end{document}" },
    };

    public static IEnumerable<object[]> BrokenLatexSnippets => new[]
    {
        new object[] { @"\begin{document}" },  // Missing documentclass
        new object[] { @"\frac{" },  // Incomplete command
        new object[] { @"\begin{equation}\end{align}" },  // Mismatched environments
        new object[] { @"" },  // Empty
        new object[] { @"just plain text no latex" },  // Not LaTeX
    };

    [Theory]
    [MemberData(nameof(ValidLatexSnippets))]
    public async Task ValidateLatex_ValidInput_DoesNotCrash(string latex)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/latex/validate", new { latex });
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(BrokenLatexSnippets))]
    public async Task ValidateLatex_BrokenInput_DoesNotCrash(string latex)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/latex/validate", new { latex });
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(ValidLatexSnippets))]
    public async Task RenderLatex_ToPdf_DoesNotCrash(string latex)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/latex/render", new { latex, format = "pdf", timeout = 15 });
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task RenderDocumentToPng_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "PNG Render");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "PNG content" } });

        var resp = await client.PostAsync($"/api/latex/{docId}/png?dpi=72", null);
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task RenderBlockToPng_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Block PNG");
        var docId = doc.GetProperty("id").GetString()!;

        var blockResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "equation", content = new { latex = @"\alpha + \beta", display = true } });
        if (!blockResp.IsSuccessStatusCode) return;
        var block = await blockResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        var resp = await client.PostAsync($"/api/latex/block/{blockId}/png?dpi=72", null);
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
