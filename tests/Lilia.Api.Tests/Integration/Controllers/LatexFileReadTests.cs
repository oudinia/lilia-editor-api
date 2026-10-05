using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// A document cannot make the server read its own files (A6 of the authz audit, 5 Oct 2026). The host
/// name came back in a PDF from <c>\input{/etc/hostname}</c>. The public formula endpoint does this
/// with no login; the raw render endpoint did it for any signed-in user.
/// </summary>
[Collection("Integration")]
public class LatexFileReadTests : FourCallersTestBase
{
    public LatexFileReadTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static readonly string HostName = Environment.MachineName;

    [Fact]
    public async Task The_public_formula_endpoint_refuses_a_file_read_and_does_not_return_the_file()
    {
        using var anon = CreateAnonymousClient();
        var attack = Uri.EscapeDataString(@"\input{/etc/hostname}");
        var res = await anon.GetAsync($"/api/latex/svg?latex={attack}");
        res.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(HostName);
        body.Should().Contain("server");
    }

    [Fact]
    public async Task A_formula_that_names_a_shell_command_is_refused()
    {
        using var anon = CreateAnonymousClient();
        var attack = Uri.EscapeDataString(@"\immediate\write18{id}");
        var res = await anon.GetAsync($"/api/latex/svg?latex={attack}");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("write18");
    }

    // The PDF route tries Typst first for small documents (Typst cannot read outside its root), and falls
    // back to LaTeX when Typst fails or the document is larger. The LaTeX compile is what these tests drive:
    // the real emitted LaTeX of a real document, given to the real LaTeX service.
    private async Task<string> EmittedLatexAsync(Guid documentId)
    {
        using var owner = As(OwnerId);
        var res = await owner.GetAsync($"/api/documents/{documentId}/preview/latex");
        res.EnsureSuccessStatusCode();
        return System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("content").GetString()!;
    }

    private async Task AssertCompileIsRefusedAsync(string latex)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.ILaTeXRenderService>();
        var act = async () => await service.RenderToPdfAsync(latex);
        (await act.Should().ThrowAsync<Lilia.Engines.TexSafety.UnsafeLatexException>()).Which.Message.Should().Contain("server").And.NotContain(HostName);
    }

    [Fact]
    public async Task A_plain_paragraph_can_carry_a_live_command_so_the_compile_is_what_refuses_it()
    {
        // Lilia lets authors write inline LaTeX in text, so this is emitted as a command, not escaped.
        var s = await SeedSharedDocumentAsync();
        await SeedBlockAsync(s.DocumentId, "paragraph", "{\"text\":\"\\\\input{/etc/hostname}\"}", 1);
        var latex = await EmittedLatexAsync(s.DocumentId);
        latex.Should().Contain("\\input{/etc/hostname}", "the premise: nothing before the compiler removed it");
        await AssertCompileIsRefusedAsync(latex);
    }

    [Fact]
    public async Task An_equation_source_that_reads_a_server_file_is_refused_on_the_pdf_route()
    {
        var s = await SeedSharedDocumentAsync();
        await SeedBlockAsync(s.DocumentId, "equation", "{\"latex\":\"\\\\input{/etc/hostname}\",\"displayMode\":true}", 1);
        using var owner = As(OwnerId);
        var res = await owner.PostAsync($"/api/latex/{s.DocumentId}/pdf", null);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("server").And.NotContain(HostName);
    }

    [Fact]
    public async Task A_custom_preamble_that_reads_a_server_file_is_refused()
    {
        var s = await SeedSharedDocumentAsync();
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(s.DocumentId);
            d!.CustomPreamble = "\\input{/etc/hostname}";
            await db.SaveChangesAsync();
        }
        await AssertCompileIsRefusedAsync(await EmittedLatexAsync(s.DocumentId));
    }

    [Fact]
    public async Task An_ordinary_document_still_compiles_through_the_LaTeX_service()
    {
        var s = await SeedSharedDocumentAsync();
        var latex = await EmittedLatexAsync(s.DocumentId);
        using var scope = Fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.ILaTeXRenderService>();
        var pdf = await service.RenderToPdfAsync(latex);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task The_raw_render_endpoint_is_for_admins_only()
    {
        using var user = As(OwnerId);
        (await user.PostAsJsonAsync("/api/latex/render", new { latex = "\\documentclass{article}\\begin{document}x\\end{document}", format = "pdf" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anon = CreateAnonymousClient();
        (await anon.PostAsJsonAsync("/api/latex/render", new { latex = "x", format = "pdf" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
