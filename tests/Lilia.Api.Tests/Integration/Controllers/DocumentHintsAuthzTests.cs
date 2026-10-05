using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Document hints check who is asking (A4 of the authz audit, 3 Oct 2026). compute and ai-augment
/// ran for a stranger and a viewer; ai-augment sends the document's text to the model. Apply and
/// dismiss took a finding id without tying it to the document in the route.
/// </summary>
[Collection("Integration")]
public class DocumentHintsAuthzTests : FourCallersTestBase
{
    public DocumentHintsAuthzTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedFindingAsync(Guid documentId)
    {
        await using var db = CreateDbContext();
        var f = new ImportStructuralFinding
        {
            Id = Guid.NewGuid(), DocumentId = documentId, Kind = "layout_table", Severity = "hint", Title = "t", Detail = "d",
            SuggestedAction = "s", ActionKind = "open_edit_modal", Status = "pending", Source = "rule",
        };
        db.ImportStructuralFindings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private static string Base(Seeded s) => $"/api/documents/{s.DocumentId}/hints";

    [Fact]
    public async Task A_stranger_is_refused_on_every_hints_route()
    {
        var s = await SeedSharedDocumentAsync();
        var finding = await SeedFindingAsync(s.DocumentId);
        using var c = As(StrangerId);
        (await c.GetAsync(Base(s))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsync($"{Base(s)}/compute", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsync($"{Base(s)}/ai-augment", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsync($"{Base(s)}/{finding}/apply", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsync($"{Base(s)}/{finding}/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_viewer_can_list_but_not_compute_augment_apply_or_dismiss()
    {
        var s = await SeedSharedDocumentAsync();
        var finding = await SeedFindingAsync(s.DocumentId);
        using var v = As(ViewerId);
        (await v.GetAsync(Base(s))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await v.PostAsync($"{Base(s)}/compute", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PostAsync($"{Base(s)}/ai-augment", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PostAsync($"{Base(s)}/{finding}/apply", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PostAsync($"{Base(s)}/{finding}/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_editor_can_compute_and_resolve_a_finding_of_this_document()
    {
        var s = await SeedSharedDocumentAsync();
        var finding = await SeedFindingAsync(s.DocumentId);
        using var e = As(EditorId);
        // Dismiss first: compute clears the pending findings, including the one we seeded.
        (await e.PostAsync($"{Base(s)}/{finding}/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await e.PostAsync($"{Base(s)}/compute", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_writer_cannot_resolve_another_documents_finding_through_their_own_document()
    {
        var mine = await SeedSharedDocumentAsync("Mine");
        var otherDoc = await SeedDocumentAsync(StrangerId, "Not mine");
        var foreign = await SeedFindingAsync(otherDoc.Id);
        using var e = As(EditorId);
        (await e.PostAsync($"{Base(mine)}/{foreign}/apply", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await e.PostAsync($"{Base(mine)}/{foreign}/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.ImportStructuralFindings.FindAsync(foreign))!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task Without_a_login_it_is_401()
    {
        var s = await SeedSharedDocumentAsync();
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync(Base(s))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
