using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// StudioController checks who is asking (A1 of the authz audit, 3 Oct 2026). It had no
/// access check on any of its 13 routes: a stranger could read, edit and delete another
/// user's blocks, and a viewer could edit.
/// </summary>
[Collection("Integration")]
public class StudioAuthzTests : FourCallersTestBase
{
    public StudioAuthzTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string Url(Seeded s, string rest) => $"/api/studio/{s.DocumentId}/{rest}";

    // ── reads ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OwnerId, true)]
    [InlineData(EditorId, true)]
    [InlineData(ViewerId, true)]
    [InlineData(StrangerId, false)]
    public async Task Reading_the_tree_needs_read_access(string user, bool allowed)
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(user);
        var res = await c.GetAsync(Url(s, "tree"));
        if (allowed) res.StatusCode.Should().Be(HttpStatusCode.OK);
        else res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("locks")]
    [InlineData("session")]
    public async Task A_stranger_cannot_read_any_document_level_route(string rest)
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(StrangerId);
        (await c.GetAsync(Url(s, rest))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stranger_cannot_read_or_preview_a_block()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(StrangerId);
        (await c.GetAsync(Url(s, $"block/{s.BlockId}"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.GetAsync(Url(s, $"block/{s.BlockId}/preview"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync(Url(s, $"block/{s.BlockId}/preview"), new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.GetAsync(Url(s, "blocks/previews"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── writes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_stranger_cannot_change_or_delete_a_block_and_the_block_survives()
    {
        var s = await SeedSharedDocumentAsync();
        using var stranger = As(StrangerId);
        (await stranger.PutAsJsonAsync(Url(s, $"block/{s.BlockId}"), new { content = new { text = "defaced" } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.DeleteAsync(Url(s, $"block/{s.BlockId}"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var owner = As(OwnerId);
        var res = await owner.GetAsync(Url(s, $"block/{s.BlockId}"));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Private text").And.NotContain("defaced");
    }

    [Fact]
    public async Task A_viewer_can_read_but_not_write()
    {
        var s = await SeedSharedDocumentAsync();
        using var v = As(ViewerId);
        (await v.GetAsync(Url(s, $"block/{s.BlockId}"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await v.PutAsJsonAsync(Url(s, $"block/{s.BlockId}"), new { content = new { text = "x" } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.DeleteAsync(Url(s, $"block/{s.BlockId}"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PutAsJsonAsync(Url(s, $"block/{s.BlockId}/move"), new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PatchAsJsonAsync(Url(s, $"block/{s.BlockId}/metadata"), new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PutAsJsonAsync(Url(s, "session"), new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_viewer_may_preview_because_a_preview_saves_nothing()
    {
        var s = await SeedSharedDocumentAsync();
        using var v = As(ViewerId);
        var res = await v.PostAsJsonAsync(Url(s, $"block/{s.BlockId}/preview"), new { });
        res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_editor_and_the_owner_can_write()
    {
        var s = await SeedSharedDocumentAsync();
        foreach (var user in new[] { EditorId, OwnerId })
        {
            using var c = As(user);
            var res = await c.PutAsJsonAsync(Url(s, $"block/{s.BlockId}"), new { content = new { text = $"by {user}" } });
            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, user).And.NotBe(HttpStatusCode.Unauthorized, user);
        }
    }

    [Fact]
    public async Task Nobody_without_a_login_gets_in()
    {
        var s = await SeedSharedDocumentAsync();
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync(Url(s, "tree"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
