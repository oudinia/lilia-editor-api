using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Security;
using static Lilia.Api.Tests.Security.ZipBombs;

namespace Lilia.Api.Tests.Integration.Security;

/// <summary>
/// Every route that reads an uploaded archive answers a zip bomb with a 400
/// and a plain reason — not a 500, and not an out-of-memory. Each bomb weighs
/// tens of kilobytes and decompresses to 60 MB.
/// </summary>
[Collection("Integration")]
public class ZipBombUploadTests : IntegrationTestBase
{
    private const string UserId = "test_user_zip_bomb";
    private readonly HttpClient _client;

    public ZipBombUploadTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _client = CreateClientAs(UserId);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);
        return form;
    }

    private static async Task<string> BodyAsync(HttpResponseMessage res) => await res.Content.ReadAsStringAsync();

    [Theory]
    [InlineData("/api/lilia/epub/import")]
    [InlineData("/api/lilia/epub/analyze")]
    [InlineData("/api/lilia/imports/epub")]
    public async Task An_epub_bomb_is_a_400_with_a_plain_reason(string route)
    {
        await SeedUserAsync(UserId);
        var res = await _client.PostAsync(route, Form(Epub(60 * MB), "bomb.epub", "application/epub+zip"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(res));
        (await BodyAsync(res)).Should().Contain(UnsafeZipException.BookMessage);
    }

    [Fact]
    public async Task A_docx_bomb_is_a_400_and_no_document_is_made()
    {
        await SeedUserAsync(UserId);
        var res = await _client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = Convert.ToBase64String(Docx(60 * MB)),
            format = "DOCX",
            filename = "bomb.docx",
            skipReview = true,
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(res));
        (await BodyAsync(res)).Should().Contain(UnsafeZipException.UserMessage);
        await using var db = CreateDbContext();
        db.Documents.Count(d => d.OwnerId == UserId).Should().Be(0);
    }

    [Fact]
    public async Task A_latex_project_bomb_is_a_400_with_a_plain_reason()
    {
        await SeedUserAsync(UserId);
        var res = await _client.PostAsync("/api/lilia/imports/latex",
            Form(LatexProject("figures/bomb.eps", 4 * MB), "project.zip", "application/zip"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(res));
        (await BodyAsync(res)).Should().Contain(UnsafeZipException.UserMessage);
    }
}
