using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>A real compile of a title whose author uses commands: they print as formatting, not as text.</summary>
[Collection("Integration")]
public class AuthorPassThroughCompileTests : FourCallersTestBase
{
    public AuthorPassThroughCompileTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Author_commands_compile_and_do_not_print_as_text()
    {
        var s = await SeedSharedDocumentAsync();
        var author = @"Jane Doe\thanks{Funded by \textit{The Agency}.}\\U. of South \and John Doe\thanks{E-mail: \href{mailto:jd@x.org}{jd@x.org}}\\R&D Lab";
        await SeedBlockAsync(s.DocumentId, "title", JsonSerializer.Serialize(new { title = "A Paper", author, date = "May 2026" }), -1);

        using var owner = As(OwnerId);
        var res = await owner.GetAsync($"/api/documents/{s.DocumentId}/preview/latex");
        var latex = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("content").GetString()!;
        latex.Should().Contain(@"\textit{The Agency}").And.Contain(@"\href{mailto:jd@x.org}{jd@x.org}").And.Contain(@"R\&D Lab");
        latex.Should().NotContain("textbackslash");

        using var scope = Fixture.Factory.Services.CreateScope();
        var pdf = await scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.ILaTeXRenderService>().RenderToPdfAsync(latex);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }
}
