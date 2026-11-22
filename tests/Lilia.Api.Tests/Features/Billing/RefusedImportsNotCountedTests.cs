using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Xunit;

namespace Lilia.Api.Tests.Features.Billing;

/// <summary>
/// Grok's retest, 23 Nov 2026: a damaged zip refused three times took three of the month's imports. A failed or
/// cancelled import made nothing, so only the others count.
/// </summary>
public class RefusedImportsNotCountedTests : IntegrationTestBase
{
    public RefusedImportsNotCountedTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Failed_and_cancelled_imports_do_not_use_the_month_s_imports()
    {
        var user = $"imports_{Guid.NewGuid():N}"[..30];
        await SeedUserAsync(user);
        await using (var db = CreateDbContext())
        {
            foreach (var status in new[] { JobStatus.Completed, JobStatus.Processing, JobStatus.Failed, JobStatus.Failed, JobStatus.Cancelled })
                db.Jobs.Add(new Job
                {
                    Id = Guid.NewGuid(), TenantId = user, UserId = user, JobType = JobTypes.Import, Status = status,
                    SourceFormat = "zip", TargetFormat = "lilia", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var plan = await CreateClientAs(user).GetFromJsonAsync<JsonElement>("/api/plan");
        plan.GetProperty("imports").GetProperty("used").GetInt32().Should().Be(2, "the done one and the one still running");
    }
}
