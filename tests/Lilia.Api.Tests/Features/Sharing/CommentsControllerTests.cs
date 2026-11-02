using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Comments had no test (2 Nov 2026: a change to CommentsController selected none, so CI ran only the
/// fast lane, which never calls these routes). Who may do what, and a comment's life: written on a
/// block, replied to, resolved, counted, deleted with its replies.
///
/// <para>Commenting is for everyone the document is shared with, viewers included (2 Nov 2026, the owner:
/// "Commenting should be for all"): reading or writing a comment or a reply needs read access only.
/// Changing or deleting a comment is for its author or the document's owner; a reply is deleted by its
/// author only.</para>
/// </summary>
public class CommentsControllerTests : FourCallersTestBase
{
    public CommentsControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string Base(Seeded s) => $"/api/documents/{s.DocumentId}/comments";

    private static async Task<CommentDto> Post(HttpClient c, Seeded s, string content, Guid? blockId = null)
    {
        var r = await c.PostAsJsonAsync(Base(s), new CreateCommentDto(blockId ?? s.BlockId, content));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<CommentDto>())!;
    }

    [Fact]
    public async Task A_stranger_is_refused_on_every_comments_route()
    {
        var s = await SeedSharedDocumentAsync();
        using var owner = As(OwnerId);
        var comment = await Post(owner, s, "Check this.");
        using var c = As(StrangerId);
        (await c.GetAsync(Base(s))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.GetAsync($"{Base(s)}/{comment.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.GetAsync($"{Base(s)}/count")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync(Base(s), new CreateCommentDto(s.BlockId, "Hi"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PatchAsJsonAsync($"{Base(s)}/{comment.Id}", new UpdateCommentDto("x", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync($"{Base(s)}/{comment.Id}/replies", new CreateReplyDto("x"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.DeleteAsync($"{Base(s)}/{comment.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_viewer_comments_replies_and_changes_their_own_comment()
    {
        var s = await SeedSharedDocumentAsync();
        using var owner = As(OwnerId);
        var byOwner = await Post(owner, s, "Check this.");
        using var v = As(ViewerId);
        var list = await v.GetFromJsonAsync<List<CommentDto>>(Base(s));
        list!.Select(x => x.Id).Should().Equal(byOwner.Id);

        var mine = await Post(v, s, "A reader's question.");
        mine.UserId.Should().Be(ViewerId);
        (await v.PostAsJsonAsync($"{Base(s)}/{byOwner.Id}/replies", new CreateReplyDto("Agreed."))).IsSuccessStatusCode.Should().BeTrue();
        (await v.PatchAsJsonAsync($"{Base(s)}/{mine.Id}", new UpdateCommentDto("A reader's question, sharpened.", null))).StatusCode.Should().Be(HttpStatusCode.OK);
        // Commenting doesn't make the viewer an editor of other people's comments.
        (await v.PatchAsJsonAsync($"{Base(s)}/{byOwner.Id}", new UpdateCommentDto(null, true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.DeleteAsync($"{Base(s)}/{byOwner.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.DeleteAsync($"{Base(s)}/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_comment_is_written_replied_to_resolved_and_counted()
    {
        var s = await SeedSharedDocumentAsync();
        using var editor = As(EditorId);
        var comment = await Post(editor, s, "  Is this bound tight?  ");
        comment.Content.Should().Be("Is this bound tight?");
        comment.BlockId.Should().Be(s.BlockId);
        comment.UserId.Should().Be(EditorId);
        comment.Resolved.Should().BeFalse();

        using var owner = As(OwnerId);
        var reply = await owner.PostAsJsonAsync($"{Base(s)}/{comment.Id}/replies", new CreateReplyDto("Yes, see Lemma 1."));
        reply.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var resolved = await editor.PatchAsJsonAsync($"{Base(s)}/{comment.Id}", new UpdateCommentDto(null, true));
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await owner.GetFromJsonAsync<CommentDto>($"{Base(s)}/{comment.Id}");
        read!.Resolved.Should().BeTrue();
        read.Content.Should().Be("Is this bound tight?");
        read.Replies.Select(r => r.Content).Should().Equal("Yes, see Lemma 1.");

        await Post(editor, s, "A second one.");
        var counts = await owner.GetFromJsonAsync<Dictionary<string, int>>($"{Base(s)}/count");
        counts.Should().Contain(new Dictionary<string, int> { ["total"] = 2, ["resolved"] = 1, ["unresolved"] = 1 });

        var open = await owner.GetFromJsonAsync<List<CommentDto>>($"{Base(s)}?resolved=false");
        open!.Select(c => c.Content).Should().Equal("A second one.");
        var onBlock = await owner.GetFromJsonAsync<List<CommentDto>>($"{Base(s)}?blockId={s.BlockId}");
        onBlock.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_empty_comment_or_a_block_of_another_document_is_refused()
    {
        var s = await SeedSharedDocumentAsync();
        // The owner's second document (the shared one's users are seeded once per test).
        var otherDoc = await SeedDocumentAsync(OwnerId, "Another paper");
        var otherBlock = await SeedBlockAsync(otherDoc.Id, "paragraph", "{\"text\":\"Other\"}", 0);
        var other = new Seeded(otherDoc.Id, otherBlock.Id);
        using var owner = As(OwnerId);
        (await owner.PostAsJsonAsync(Base(s), new CreateCommentDto(s.BlockId, "   "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Base(s), new CreateCommentDto(other.BlockId, "Here?"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var comment = await Post(owner, s, "Fine.");
        (await owner.PostAsJsonAsync($"{Base(s)}/{comment.Id}/replies", new CreateReplyDto(""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // A comment is reached through its own document only.
        (await owner.GetAsync($"{Base(other)}/{comment.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_author_or_the_owner_changes_or_deletes_a_comment()
    {
        var s = await SeedSharedDocumentAsync();
        using var owner = As(OwnerId);
        var byOwner = await Post(owner, s, "Owner's note.");
        using var editor = As(EditorId);
        (await editor.PatchAsJsonAsync($"{Base(s)}/{byOwner.Id}", new UpdateCommentDto("Rewritten", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.DeleteAsync($"{Base(s)}/{byOwner.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var byEditor = await Post(editor, s, "Editor's note.");
        (await editor.PatchAsJsonAsync($"{Base(s)}/{byEditor.Id}", new UpdateCommentDto("Editor's note, edited.", null))).StatusCode.Should().Be(HttpStatusCode.OK);
        await owner.PostAsJsonAsync($"{Base(s)}/{byEditor.Id}/replies", new CreateReplyDto("Agreed."));
        // The owner deletes anyone's comment, and its replies go with it.
        (await owner.DeleteAsync($"{Base(s)}/{byEditor.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Base(s)}/{byEditor.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var left = await owner.GetFromJsonAsync<List<CommentDto>>(Base(s));
        left!.Select(c => c.Id).Should().Equal(byOwner.Id);
    }

    [Fact]
    public async Task A_reply_is_deleted_by_its_author_only()
    {
        var s = await SeedSharedDocumentAsync();
        using var owner = As(OwnerId);
        var comment = await Post(owner, s, "Thoughts?");
        using var editor = As(EditorId);
        var posted = await editor.PostAsJsonAsync($"{Base(s)}/{comment.Id}/replies", new CreateReplyDto("One thought."));
        var reply = (await posted.Content.ReadFromJsonAsync<CommentReplyDto>())!;
        // Not even the document's owner deletes someone else's reply.
        (await owner.DeleteAsync($"{Base(s)}/{comment.Id}/replies/{reply.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.DeleteAsync($"{Base(s)}/{comment.Id}/replies/{reply.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var read = await owner.GetFromJsonAsync<CommentDto>($"{Base(s)}/{comment.Id}");
        read!.Replies.Should().BeEmpty();
    }
}
