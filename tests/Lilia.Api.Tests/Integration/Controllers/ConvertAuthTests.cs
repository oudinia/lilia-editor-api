using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// The editor-internal Convert endpoints need a login; the free tools stay public.
///
/// <para>ConvertController had a class-level [AllowAnonymous], and ASP.NET lets that win
/// over an action's [Authorize] (compiler warning ASP0026). So block-to-latex,
/// block/validate and latex-to-blocks answered anyone. Found by the cloud session's
/// auth probe (2 Oct 2026).</para>
/// </summary>
[Collection("Integration")]
public class ConvertAuthTests : IntegrationTestBase
{
    public ConvertAuthTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static readonly object Block = new { type = "paragraph", content = new { text = "Hello" } };

    [Theory]
    [InlineData("/api/convert/block-to-latex")]
    [InlineData("/api/convert/block/validate")]
    [InlineData("/api/convert/latex-to-blocks")]
    public async Task The_editor_internal_endpoints_refuse_an_anonymous_caller(string url)
    {
        using var anon = CreateAnonymousClient();
        var res = await anon.PostAsJsonAsync(url, new { latex = "\\section{A}", type = "paragraph", content = new { text = "Hello" } });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/convert/block-to-latex")]
    [InlineData("/api/convert/block/validate")]
    [InlineData("/api/convert/latex-to-blocks")]
    public async Task The_editor_internal_endpoints_still_answer_a_signed_in_user(string url)
    {
        var res = await Client.PostAsJsonAsync(url, new { latex = "\\section{A}", type = "paragraph", content = new { text = "Hello" } });
        res.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_free_tools_stay_public()
    {
        using var anon = CreateAnonymousClient();
        var res = await anon.GetAsync("/api/convert/quota");
        res.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }
}
