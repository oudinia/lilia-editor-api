using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// Launch-bot #76 (13 Nov 2026): a Word import's review was titled "LaTeX import review". The title reads
/// the session's source format, and every session made from a job kept the default "tex". It takes the
/// job's format now; a LaTeX job still gives "tex".
/// </summary>
public class ReviewSessionFormatTests : IntegrationTestBase
{
    private readonly string _userId = $"fmt-{Guid.NewGuid():N}"[..28];

    public ReviewSessionFormatTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private async Task<string> SessionFormatFor(string? jobFormat)
    {
        var jobId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.Jobs.Add(new Job
            {
                Id = jobId, TenantId = _userId, UserId = _userId, JobType = "IMPORT", Status = "COMPLETED",
                SourceFormat = jobFormat, TargetFormat = "lilia", Direction = "INBOUND",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var resp = await CreateClientAs(_userId).PostAsJsonAsync("/api/lilia/import-review/sessions",
            new { jobId, documentTitle = "Coastal notes", blocks = Array.Empty<object>() });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        await using var check = CreateDbContext();
        return await check.ImportReviewSessions.Where(s => s.JobId == jobId).Select(s => s.SourceFormat).SingleAsync();
    }

    [Theory]
    [InlineData("docx", "docx")]
    [InlineData("pdf", "pdf")]
    [InlineData("markdown", "markdown")]
    [InlineData("latex", "tex")]
    [InlineData(null, "tex")]
    public async Task A_session_takes_its_jobs_format(string? jobFormat, string expected)
    {
        (await SessionFormatFor(jobFormat)).Should().Be(expected);
    }
}
