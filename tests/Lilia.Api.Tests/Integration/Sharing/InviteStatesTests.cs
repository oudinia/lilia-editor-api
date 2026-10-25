using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Integration.Sharing;

/// <summary>
/// The share drawer's Pending and Expired rows (Olivia, 10 Oct, §c): listing a
/// document's open invitations, sending one again, withdrawing one, and a link
/// that only lets someone in while its invitation is live.
/// </summary>
public class InviteStatesTests : IntegrationTestBase
{
    private readonly RecordingEmailService _mail = new();
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
    private readonly HttpClient _owner;

    private HttpClient As(string userId, string name)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", name);
        return c;
    }

    public InviteStatesTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailService>();
            s.AddSingleton<IEmailService>(_mail);
        }));
        _owner = As("inv-owner", "Ada Owner");
    }

    // A factory per test (xUnit makes one instance per test) holds its own connection pool until it
    // is disposed; undisposed, they ran the test database out of clients (53300, 25 Oct).
    public override async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<Lilia.Core.Entities.Document> DocWithInvites(params string[] emails)
    {
        await SeedUserAsync("inv-owner", "inv-owner@lilia.test", "Ada Owner");
        var doc = await SeedDocumentAsync("inv-owner", "Heat transport");
        foreach (var e in emails)
            (await _owner.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/invite", new { email = e, role = "viewer" })).EnsureSuccessStatusCode();
        return doc;
    }

    private async Task<List<PendingInviteDto>> Invites(Guid docId, HttpClient? as_ = null)
    {
        var res = await (as_ ?? _owner).GetAsync($"/api/documents/{docId}/collaborators/invites");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<List<PendingInviteDto>>())!;
    }

    private async Task Expire(Guid inviteId)
    {
        await using var db = CreateDbContext();
        var row = await db.DocumentPendingInvites.SingleAsync(p => p.Id == inviteId);
        row.CreatedAt = DateTime.UtcNow.AddDays(-20);
        row.ExpiresAt = DateTime.UtcNow.AddDays(-6);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Open_invitations_are_listed_oldest_first_pending_or_expired()
    {
        var doc = await DocWithInvites("first@lilia.test", "second@lilia.test");
        var list = await Invites(doc.Id);
        list.Select(i => i.Email).Should().Equal("first@lilia.test", "second@lilia.test");
        list.Should().OnlyContain(i => i.Status == "pending" && i.Role == "viewer");
        list[0].ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
        list[0].SentAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        await Expire(list[0].Id);
        (await Invites(doc.Id)).Select(i => i.Status).Should().Equal("expired", "pending");
    }

    [Fact]
    public async Task Resending_an_expired_invitation_mails_it_again_for_fourteen_more_days()
    {
        var doc = await DocWithInvites("late@lilia.test");
        var invite = (await Invites(doc.Id)).Single();
        await Expire(invite.Id);
        _mail.Clear();

        var res = await _owner.PostAsync($"/api/documents/{doc.Id}/collaborators/invites/{invite.Id}/resend", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await Invites(doc.Id)).Single();
        after.Id.Should().Be(invite.Id, "the same link keeps working");
        after.Status.Should().Be("pending");
        after.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
        after.SentAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        after.InvitedAt.Should().BeBefore(DateTime.UtcNow.AddDays(-19), "the first invitation date stays");
        _mail.To("late@lilia.test").Select(m => m.Kind).Should().Equal("invite");
    }

    [Fact]
    public async Task A_cancelled_invitation_leaves_the_list_and_its_link_no_longer_lets_anyone_in()
    {
        var doc = await DocWithInvites("nope@lilia.test");
        var invite = (await Invites(doc.Id)).Single();

        (await _owner.DeleteAsync($"/api/documents/{doc.Id}/collaborators/invites/{invite.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Invites(doc.Id)).Should().BeEmpty();
        (await _owner.DeleteAsync($"/api/documents/{doc.Id}/collaborators/invites/{invite.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _owner.PostAsync($"/api/documents/{doc.Id}/collaborators/invites/{invite.Id}/resend", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SeedUserAsync("nope", "nope@lilia.test", "Nope");
        (await As("nope", "Nope").PostAsync($"/api/invites/{invite.Id}/accept", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = CreateDbContext();
        (await db.DocumentCollaborators.AnyAsync(c => c.DocumentId == doc.Id && c.UserId == "nope")).Should().BeFalse();
    }

    [Fact]
    public async Task Someone_removed_cannot_get_back_in_through_their_old_invitation()
    {
        var doc = await DocWithInvites("gone@lilia.test");
        var invite = (await Invites(doc.Id)).Single();
        await SeedUserAsync("gone", "gone@lilia.test", "Gone");
        var gone = As("gone", "Gone");
        (await gone.PostAsync($"/api/invites/{invite.Id}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await gone.PostAsync($"/api/invites/{invite.Id}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK, "accepting twice is harmless");

        (await _owner.DeleteAsync($"/api/documents/{doc.Id}/collaborators/users/gone")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await gone.PostAsync($"/api/invites/{invite.Id}/accept", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = CreateDbContext();
        (await db.DocumentCollaborators.AnyAsync(c => c.DocumentId == doc.Id && c.UserId == "gone")).Should().BeFalse();
    }

    [Fact]
    public async Task Only_someone_who_manages_the_document_sees_or_changes_its_invitations()
    {
        var doc = await DocWithInvites("private@lilia.test");
        var invite = (await Invites(doc.Id)).Single();
        await SeedUserAsync("inv-reader", "inv-reader@lilia.test", "Reader");
        (await _owner.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users", new { userId = "inv-reader", role = "viewer" })).EnsureSuccessStatusCode();
        var reader = As("inv-reader", "Reader");

        (await reader.GetAsync($"/api/documents/{doc.Id}/collaborators/invites")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reader.PostAsync($"/api/documents/{doc.Id}/collaborators/invites/{invite.Id}/resend", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/documents/{doc.Id}/collaborators/invites/{invite.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Invites(doc.Id)).Should().ContainSingle();
    }
}
