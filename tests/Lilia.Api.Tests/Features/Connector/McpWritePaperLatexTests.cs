using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The write_paper prompt asks for LaTeX since 23 Nov 2026 (no LML in what an author reads): the LaTeX it describes, sent
/// as create_document's content, must come in as the right blocks, for a paper and for a moderncv CV.
/// </summary>
public class McpWritePaperLatexTests : IntegrationTestBase
{
    public McpWritePaperLatexTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private async Task<List<string>> TypesAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Blocks.AsNoTracking().Where(b => b.DocumentId == id).OrderBy(b => b.SortOrder).Select(b => b.Type).ToListAsync();
    }

    [Fact]
    public async Task A_paper_in_the_prompt_s_LaTeX_comes_in_as_abstract_sections_equation_and_references()
    {
        await SeedUserAsync("mcpwp_paper");
        var r = await CallAsync(CreateClientAs("mcpwp_paper"), "create_document", new
        {
            title = "Sleep stages",
            content = """
                \begin{abstract}
                Sleep has stages that differ in brain activity.
                \end{abstract}
                \section{Introduction}
                Stages alternate through the night \cite{smith2020}.
                \begin{equation}\label{eq:cycle}
                T = n \cdot 90
                \end{equation}
                \section{Discussion}
                As Equation~\eqref{eq:cycle} shows, cycles last about 90 minutes.
                \begin{thebibliography}{9}
                \bibitem{smith2020} A. Smith. Sleep cycles. 2020.
                \end{thebibliography}
                """,
        });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        var types = await TypesAsync(r.GetProperty("id").GetGuid());
        types.Should().Contain("abstract").And.Contain("heading").And.Contain("paragraph").And.Contain("equation").And.Contain("bibliography");
    }

    [Fact]
    public async Task A_CV_in_the_prompt_s_moderncv_LaTeX_comes_in_as_CV_blocks()
    {
        await SeedUserAsync("mcpwp_cv");
        var r = await CallAsync(CreateClientAs("mcpwp_cv"), "create_document", new
        {
            title = "Ada Lovelace — CV",
            content = """
                \documentclass{moderncv}
                \moderncvstyle{classic}
                \name{Ada}{Lovelace}
                \email{ada@example.org}
                \begin{document}
                \makecvtitle
                \section{Experience}
                \cventry{1842--1843}{Translator}{Royal Society}{London}{}{Translated and annotated Menabrea's paper on the Analytical Engine.}
                \section{Education}
                \cventry{1832--1835}{Private tutoring}{Augustus De Morgan}{London}{}{Mathematics.}
                \end{document}
                """,
        });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        var types = await TypesAsync(r.GetProperty("id").GetGuid());
        types.Should().Contain("cvEntry");
    }
}
