using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// The export job's Markdown is the export endpoint's (10 Nov 2026): the job had its own HTML-stripping converter,
/// which missed what the endpoint had just learnt (references written in the References block, Table~(tab:x),
/// \citep). One converter now.
/// </summary>
public class JobMarkdownExportTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public JobMarkdownExportTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_export_job_gives_the_same_Markdown_as_the_export_endpoint()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Sleep");
        await SeedBlockAsync(doc.Id, "paragraph",
            JsonSerializer.Serialize(new { text = "As Table~(tab:sleep-recall) shows \\citep{sleep1}." }), 0);
        await SeedBlockAsync(doc.Id, "bibliography",
            JsonSerializer.Serialize(new { text = "@cite[sleep1] Author, A. — A paper on sleep.", title = "References" }), 1);

        var direct = await Client.GetStringAsync($"/api/documents/{doc.Id}/export/markdown");
        var res = await Client.PostAsJsonAsync("/api/lilia/jobs/export", new { documentId = doc.Id, format = "MARKDOWN" });
        var started = await res.Content.ReadFromJsonAsync<ExportResultDto>();
        // The job runs in the background (RunExportJobEvent); its result comes when it has finished.
        var content = started!.Content;
        for (var i = 0; i < 100 && string.IsNullOrEmpty(content); i++)
        {
            await Task.Delay(100);
            var r = await Client.GetAsync($"/api/lilia/jobs/{started.JobId}/result");
            if (r.IsSuccessStatusCode) content = (await r.Content.ReadFromJsonAsync<ExportResultDto>())!.Content;
        }

        content.Should().Be(direct);
        content.Should().Contain("Table [tab:sleep-recall](#tab:sleep-recall)").And.Contain("[sleep1]: Author, A.");
    }
}
