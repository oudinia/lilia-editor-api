using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Jobs;

/// <summary>
/// The base64 import (POST /api/lilia/jobs/import, which the connector's import_document uses) keeps
/// what the upload path keeps. It had its own copy of the element → block mapping, which had drifted:
/// a heading lost its \label (LaTeX coverage refs.section-ref, 15 Oct), and each \item became a list.
/// </summary>
[Collection("Integration")]
public class Base64LatexImportKeepsStructureTests : IntegrationTestBase
{
    public Base64LatexImportKeepsStructureTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_heading_keeps_its_label_and_a_list_stays_one_list()
    {
        const string user = "b64-structure";
        await SeedUserAsync(user);
        var client = CreateClientAs(user);
        const string tex = "\\documentclass{article}\n\\begin{document}\n" +
                           "\\section{Setup}\\label{sec:setup}\nText.\nAs in Section~\\ref{sec:setup}.\n\n" +
                           "\\begin{itemize}\n\\item One\n\\item Two\n\\end{itemize}\n\\end{document}\n";

        var response = await client.PostAsJsonAsync("/api/lilia/jobs/import",
            new { content = tex, format = "LATEX", filename = "setup.tex", skipReview = true });
        response.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("document").GetProperty("id").GetGuid();

        await using var db = CreateDbContext();
        var blocks = await db.Blocks.Where(b => b.DocumentId == id).OrderBy(b => b.SortOrder).ToListAsync();

        var heading = blocks.Single(b => b.Type == "heading").Content.RootElement;
        heading.GetProperty("label").GetString().Should().Be("sec:setup");

        blocks.Single(b => b.Type == "paragraph").Content.RootElement.GetProperty("text").GetString()
            .Should().Be("Text. As in Section \\ref{sec:setup}.", "a line break inside a paragraph is a space");

        var list = blocks.Single(b => b.Type == "list").Content.RootElement;
        list.GetProperty("items").GetArrayLength().Should().Be(2);
    }
}
