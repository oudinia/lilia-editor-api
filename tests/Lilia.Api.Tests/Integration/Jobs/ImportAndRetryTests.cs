using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Jobs;

/// <summary>
/// Found by the Jobs e2e (26 Sep): a plain-text file named .docx imported
/// "successfully" as an empty document, and Retry on a failed import set it
/// PENDING and left it there — nothing re-ran imports; an export retry made a
/// new job and left the old one PENDING too.
/// </summary>
[Collection("Integration")]
public class ImportAndRetryTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public ImportAndRetryTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<HttpResponseMessage> ImportAsDocx(string text) =>
        await Client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            format = "DOCX",
            filename = "not-really.docx",
            title = "Not really",
            skipReview = true,
        });

    [Fact]
    public async Task A_file_that_is_not_a_Word_document_is_refused_and_its_job_fails()
    {
        await SeedUserAsync(UserId);
        var response = await ImportAsDocx("Just some text, not a Word document.");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Word document");

        await using var db = CreateDbContext();
        var job = await db.Jobs.Where(j => j.UserId == UserId).OrderByDescending(j => j.CreatedAt).FirstAsync();
        job.Status.Should().Be(JobStatus.Failed);
        (await db.Documents.CountAsync(d => d.OwnerId == UserId && d.Title == "Not really")).Should().Be(0, "no empty document is made");
    }

    [Fact]
    public async Task Retrying_a_failed_import_says_why_it_cannot_and_leaves_the_job_failed()
    {
        await SeedUserAsync(UserId);
        await ImportAsDocx("still not a Word document");
        await using var db = CreateDbContext();
        var job = await db.Jobs.Where(j => j.UserId == UserId).OrderByDescending(j => j.CreatedAt).FirstAsync();

        var retry = await Client.PostAsync($"/api/lilia/jobs/{job.Id}/retry", null);

        retry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await retry.Content.ReadAsStringAsync()).Should().Contain("import the file again");
        await using var check = CreateDbContext();
        (await check.Jobs.FirstAsync(j => j.Id == job.Id)).Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task Retrying_a_failed_export_starts_a_new_export_and_leaves_the_failed_one_as_it_was()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Export me");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 1);
        var failed = new Job
        {
            Id = Guid.NewGuid(), UserId = UserId, JobType = JobTypes.Export, Status = JobStatus.Failed,
            DocumentId = doc.Id, TargetFormat = "latex", ErrorMessage = "boom",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        await using (var db = CreateDbContext()) { db.Jobs.Add(failed); await db.SaveChangesAsync(); }

        var retry = await Client.PostAsync($"/api/lilia/jobs/{failed.Id}/retry", null);

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = await retry.Content.ReadFromJsonAsync<JsonElement>();
        fresh.GetProperty("id").GetGuid().Should().NotBe(failed.Id);
        await using var check = CreateDbContext();
        (await check.Jobs.FirstAsync(j => j.Id == failed.Id)).Status.Should().Be(JobStatus.Failed, "not a PENDING that nothing will run");
    }
}
