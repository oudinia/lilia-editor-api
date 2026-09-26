using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Sharing;

/// <summary>
/// "Make a copy" from the public viewer. A link that no longer opens the
/// document must not copy it either: an expired link reads as 404, so the copy
/// has to be refused the same way.
/// </summary>
[Collection("Integration")]
public class PublicLinkCopyTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";
    private const string VisitorId = "test_user_002";

    public PublicLinkCopyTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid Id, string Link)> SharedDocumentAsync()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(VisitorId);
        var doc = await SeedDocumentAsync(OwnerId, "Shared paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Copied prose."}""", 1);
        var share = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/share", new { isPublic = true });
        share.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await share.Content.ReadFromJsonAsync<DocumentShareResultDto>();
        return (doc.Id, result!.ShareLink);
    }

    private async Task ExpireAsync(Guid id)
    {
        await using var db = CreateDbContext();
        var doc = await db.Documents.FirstAsync(d => d.Id == id);
        doc.LinkExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_live_link_copies_into_the_visitors_library()
    {
        var (_, link) = await SharedDocumentAsync();
        using var visitor = CreateClientAs(VisitorId);

        var response = await visitor.PostAsync($"/api/documents/shared/{link}/copy", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var copy = await response.Content.ReadFromJsonAsync<DocumentDto>();
        await using var db = CreateDbContext();
        var stored = await db.Documents.Include(d => d.Blocks).FirstAsync(d => d.Id == copy!.Id);
        stored.OwnerId.Should().Be(VisitorId);
        stored.IsPublic.Should().BeFalse();
        stored.Blocks.Select(b => b.Content.RootElement.GetRawText()).Should().Contain(c => c.Contains("Copied prose."));
    }

    [Fact]
    public async Task An_expired_link_neither_opens_nor_copies()
    {
        var (id, link) = await SharedDocumentAsync();
        await ExpireAsync(id);
        using var anon = CreateAnonymousClient();
        using var visitor = CreateClientAs(VisitorId);

        (await anon.GetAsync($"/api/documents/shared/{link}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await visitor.PostAsync($"/api/documents/shared/{link}/copy", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.Documents.CountAsync(d => d.OwnerId == VisitorId)).Should().Be(0);
    }

    [Fact]
    public async Task A_revoked_link_does_not_copy()
    {
        var (id, link) = await SharedDocumentAsync();
        (await Client.DeleteAsync($"/api/documents/{id}/share")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var visitor = CreateClientAs(VisitorId);

        (await visitor.PostAsync($"/api/documents/shared/{link}/copy", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_guest_is_asked_to_sign_in_before_copying()
    {
        var (_, link) = await SharedDocumentAsync();
        using var anon = CreateAnonymousClient();

        (await anon.PostAsync($"/api/documents/shared/{link}/copy", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
