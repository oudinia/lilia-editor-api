using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// Launch-bot #195 (18 Nov 2026): a Word paper with five sections and no contents list got one, placed above its title.
/// A Word file says itself whether it has a contents list; a PDF can't, so it keeps the automatic one.
/// </summary>
public class WordImportAutoTocTests : IntegrationTestBase
{
    private const string UserId = "test_user_word_toc";

    public WordImportAutoTocTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static List<CreateReviewBlockDto> FiveSections() =>
        Enumerable.Range(1, 5).Select(i => new CreateReviewBlockDto(
            Id: Guid.NewGuid().ToString(), Type: "heading",
            Content: JsonSerializer.SerializeToElement(new { text = $"Section {i}", level = 1 }),
            Confidence: 90, Warnings: null, SortOrder: i, Depth: 0)).ToList();

    private async Task<List<string>> TypesAsync(bool autoToc)
    {
        if (!await CreateDbContext().Users.AnyAsync(u => u.Id == UserId)) await SeedUserAsync(UserId);
        var jobId = Guid.NewGuid();
        await using (var seed = CreateDbContext())
        {
            seed.Jobs.Add(new Job
            {
                Id = jobId, TenantId = UserId, UserId = UserId, JobType = JobTypes.Import, Status = JobStatus.Pending,
                SourceFormat = "docx", TargetFormat = "lilia", SourceFileName = "paper.docx", Direction = "INBOUND",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }
        using var scope = Fixture.Factory.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IImportReviewService>();
        var created = await review.CreateSessionFromImportAsync(UserId, jobId, "Paper", FiveSections(), autoToc: autoToc);
        await using var db = CreateDbContext();
        return await db.ImportBlockReviews.Where(r => r.SessionId == created.Session.Id)
            .OrderBy(r => r.SortOrder).Select(r => r.OriginalType).ToListAsync();
    }

    [Fact]
    public async Task A_word_import_gets_no_contents_list_it_did_not_have()
        => (await TypesAsync(autoToc: false)).Should().NotContain("tableOfContents");

    [Fact]
    public async Task A_pdf_import_still_gets_one()
        => (await TypesAsync(autoToc: true)).Should().Contain("tableOfContents");
}
