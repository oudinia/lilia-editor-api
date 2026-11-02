using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Notifications had no test (2 Nov 2026, the coverage round): a person sees and marks their own,
/// never anyone else's.
/// </summary>
public class NotificationsTests : IntegrationTestBase
{
    public NotificationsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> NotifyAsync(string userId, string title)
    {
        await using var db = CreateDbContext();
        var n = new Notification { Id = Guid.NewGuid(), UserId = userId, Type = "document_shared", Title = title, CreatedAt = DateTime.UtcNow };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    [Fact]
    public async Task A_person_reads_counts_and_marks_their_own_notifications()
    {
        await SeedUserAsync("notif_reader");
        var first = await NotifyAsync("notif_reader", "Shared with you: Paper A");
        await NotifyAsync("notif_reader", "Shared with you: Paper B");
        using var c = CreateClientAs("notif_reader");

        (await c.GetStringAsync("/api/notifications")).Should().Contain("Paper A").And.Contain("Paper B");
        (await c.GetStringAsync("/api/notifications/count")).Should().Contain("2");
        (await c.PutAsync($"/api/notifications/{first}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetStringAsync("/api/notifications/count")).Should().Contain("1");
        (await c.PutAsync("/api/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetStringAsync("/api/notifications/count")).Should().Contain("0");
    }

    [Fact]
    public async Task Nobody_sees_or_marks_someone_elses_notification()
    {
        await SeedUserAsync("notif_owner");
        await SeedUserAsync("notif_other");
        var theirs = await NotifyAsync("notif_owner", "Private title");
        using var other = CreateClientAs("notif_other");
        (await other.GetStringAsync("/api/notifications")).Should().NotContain("Private title");
        (await other.PutAsync($"/api/notifications/{theirs}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = CreateDbContext();
        (await db.Notifications.FindAsync(theirs))!.IsRead.Should().BeFalse();
    }
}
