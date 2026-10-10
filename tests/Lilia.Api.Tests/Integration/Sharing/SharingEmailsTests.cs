using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Integration.Sharing;

/// <summary>
/// Sharing, end to end through the API with a recording mail service (10 Oct
/// 2026: "sharing is something which is not well tested").
/// </summary>
public class SharingEmailsTests : IntegrationTestBase
{
    private readonly RecordingEmailService _mail = new();
    private readonly HttpClient _owner;
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;

    private HttpClient As(string userId, string name)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", name);
        return c;
    }

    private async Task<List<Lilia.Core.Entities.Notification>> NotesFor(string userId)
    {
        await using var db = CreateDbContext();
        return await db.Notifications.Where(n => n.UserId == userId).OrderBy(n => n.CreatedAt).ToListAsync();
    }

    public SharingEmailsTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEmailService>();
            s.AddSingleton<IEmailService>(_mail);
        }));
        _owner = _factory.CreateClient();
        _owner.DefaultRequestHeaders.Add("X-Test-UserId", "share-owner");
        _owner.DefaultRequestHeaders.Add("X-Test-Email", "share-owner@lilia.test");
        _owner.DefaultRequestHeaders.Add("X-Test-Name", "Ada Owner");
    }

    private async Task<IReadOnlyList<RecordingEmailService.Sent>> MailTo(string email, int atLeast)
    {
        // The share email goes out from a message handler, a moment after the response.
        for (var i = 0; i < 40 && _mail.To(email).Count < atLeast; i++) await Task.Delay(100);
        await Task.Delay(300); // and nothing more after it
        return _mail.To(email);
    }

    [Fact]
    public async Task Sharing_with_someone_who_has_an_account_sends_one_email_and_is_not_left_pending()
    {
        await SeedUserAsync("share-owner", "share-owner@lilia.test", "Ada Owner");
        await SeedUserAsync("share-reader", "share-reader@lilia.test", "Ben Reader");
        var doc = await SeedDocumentAsync("share-owner", "Heat transport");

        var res = await _owner.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/invite", new { email = "share-reader@lilia.test", role = "viewer" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var mail = await MailTo("share-reader@lilia.test", 1);
        mail.Select(m => m.Kind).Should().Equal("shared");

        await using var db = CreateDbContext();
        (await db.DocumentCollaborators.AnyAsync(c => c.DocumentId == doc.Id && c.UserId == "share-reader")).Should().BeTrue();
        (await db.DocumentPendingInvites.Where(p => p.DocumentId == doc.Id).Select(p => p.Status).ToListAsync())
            .Should().Equal("accepted");
    }

    [Fact]
    public async Task Inviting_an_address_without_an_account_sends_the_invite_and_stays_pending()
    {
        await SeedUserAsync("share-owner", "share-owner@lilia.test", "Ada Owner");
        var doc = await SeedDocumentAsync("share-owner", "Heat transport");

        var res = await _owner.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/invite", new { email = "new-person@lilia.test", role = "editor" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var mail = await MailTo("new-person@lilia.test", 1);
        mail.Select(m => m.Kind).Should().Equal("invite");
        mail[0].Detail.Should().Contain("/invites/");

        await using var db = CreateDbContext();
        (await db.DocumentPendingInvites.Where(p => p.DocumentId == doc.Id).Select(p => p.Status).ToListAsync())
            .Should().Equal("pending");
    }

    [Fact]
    public async Task Accepting_an_invite_tells_the_owner_in_the_app_and_once_an_hour_by_email()
    {
        await SeedUserAsync("share-owner", "share-owner@lilia.test", "Ada Owner");
        var doc = await SeedDocumentAsync("share-owner", "Heat transport");
        foreach (var who in new[] { "cleo", "dan" })
            (await _owner.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/invite", new { email = $"{who}@lilia.test", role = "viewer" })).EnsureSuccessStatusCode();

        // They sign up later, then follow the link.
        await SeedUserAsync("cleo", "cleo@lilia.test", "Cleo New");
        await SeedUserAsync("dan", "dan@lilia.test", "Dan New");
        await using (var db = CreateDbContext())
        {
            foreach (var who in new[] { "cleo", "dan" })
            {
                var token = await db.DocumentPendingInvites.Where(p => p.Email == $"{who}@lilia.test").Select(p => p.Id).SingleAsync();
                (await As(who, who).PostAsync($"/api/invites/{token}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        var notes = await NotesFor("share-owner");
        notes.Select(n => n.Type).Should().Equal("invite_accepted", "invite_accepted");
        notes[0].Title.Should().Be("Cleo New accepted your invitation to \"Heat transport\"");
        notes[0].Link.Should().Be($"/d/{doc.Id}?share=open&flash=cleo");

        // Two accepts within the hour: one email.
        var mail = await MailTo("share-owner@lilia.test", 1);
        mail.Where(m => m.Kind == "accepted").Should().ContainSingle()
            .Which.Detail.Should().EndWith($"/d/{doc.Id}?share=open&flash=cleo");
    }

    [Fact]
    public async Task A_role_change_and_a_removal_reach_the_person_in_the_app_and_by_email()
    {
        await SeedUserAsync("share-owner", "share-owner@lilia.test", "Ada Owner");
        await SeedUserAsync("share-reader", "share-reader@lilia.test", "Ben Reader");
        var doc = await SeedDocumentAsync("share-owner", "Heat transport");
        (await _owner.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/invite", new { email = "share-reader@lilia.test", role = "viewer" })).EnsureSuccessStatusCode();

        (await _owner.PutAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users/share-reader", new { role = "editor" })).EnsureSuccessStatusCode();
        // Setting the same role again tells nobody anything.
        (await _owner.PutAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users/share-reader", new { role = "editor" })).EnsureSuccessStatusCode();
        (await _owner.DeleteAsync($"/api/documents/{doc.Id}/collaborators/users/share-reader")).EnsureSuccessStatusCode();

        var notes = await NotesFor("share-reader");
        notes.Select(n => n.Type).Should().Equal("document_shared", "role_changed", "access_removed");
        notes[1].Message.Should().Be("Can view → Can edit");
        notes[2].Link.Should().BeNull();
        notes[2].Title.Should().Be("Ada Owner removed your access to \"Heat transport\"");

        var mail = await MailTo("share-reader@lilia.test", 3);
        mail.Select(m => m.Kind).Should().Equal("shared", "role-changed", "removed");
        mail[1].Detail.Should().Be("Can view → Can edit");
    }
}
