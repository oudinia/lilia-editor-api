using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// GET /api/documents/{id}/export/typst checks who is asking (A2 of the authz audit, 3 Oct 2026).
/// The route is absolute, so it lives under api/documents although the controller is api/typst,
/// and it called the renderer without any access check: a stranger got the document's source.
/// </summary>
[Collection("Integration")]
public class TypstExportAuthzTests : FourCallersTestBase
{
    public TypstExportAuthzTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_stranger_cannot_export_someone_elses_document_as_Typst()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(StrangerId);
        var res = await c.GetAsync($"/api/documents/{s.DocumentId}/export/typst");
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadAsStringAsync()).Should().NotContain("Private text");
    }

    [Theory]
    [InlineData(OwnerId)]
    [InlineData(EditorId)]
    [InlineData(ViewerId)]
    public async Task The_owner_an_editor_and_a_viewer_can_export_it(string user)
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(user);
        var res = await c.GetAsync($"/api/documents/{s.DocumentId}/export/typst");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Private text");
    }

    [Fact]
    public async Task Without_a_login_it_is_401()
    {
        var s = await SeedSharedDocumentAsync();
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync($"/api/documents/{s.DocumentId}/export/typst")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
