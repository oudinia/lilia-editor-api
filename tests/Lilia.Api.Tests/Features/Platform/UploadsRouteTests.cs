using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.S3.Model;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.Platform;

/// <summary>
/// <c>/uploads/{key}</c> (21 Nov 2026): documents store their images as /uploads/… URLs, and the editor loads them
/// with plain image tags. The route was static files over the local folder; it now streams from the store, so the same
/// URLs work when the files live in the private R2 bucket. Same rule as before (no sign-in: the key is the
/// permission), plus: no "..", kept import originals not served, nothing runs as a page.
/// </summary>
public class UploadsRouteTests(TestDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    internal static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private IStorageService Storage => Fixture.Factory.Services.GetRequiredService<IStorageService>();

    private HttpClient Anonymous() => CreateAnonymousClient();

    [Fact]
    public async Task A_stored_image_is_served_without_sign_in_with_its_type()
    {
        var key = $"u-{Guid.NewGuid():N}/documents/{Guid.NewGuid()}/images/{Guid.NewGuid()}.png";
        await Storage.UploadAsync(key, new MemoryStream(OnePixelPng()), "image/png");

        var r = await Anonymous().GetAsync($"/uploads/{key}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await r.Content.ReadAsByteArrayAsync()).Should().Equal(OnePixelPng());
        r.Headers.CacheControl!.Private.Should().BeTrue();
        r.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox");
        r.Content.Headers.ContentDisposition.Should().BeNull("an image shows inline");

        // Signed in works the same; HEAD answers too.
        (await CreateClientAs("uploads-reader").GetAsync($"/uploads/{key}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Anonymous().SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/uploads/{key}"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_page_or_script_comes_as_a_sandboxed_download()
    {
        var key = $"u-{Guid.NewGuid():N}/documents/x/import-assets/{Guid.NewGuid()}.html";
        await Storage.UploadAsync(key, new MemoryStream("<script>alert(1)</script>"u8.ToArray()), "text/html");
        var r = await Anonymous().GetAsync($"/uploads/{key}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        r.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("sandbox");
    }

    [Theory]
    [InlineData("imports/{0}.zip")]
    [InlineData("bulk-imports/u/{0}/source.pdf")]
    [InlineData("bulk-staging/{0}/fig.png")]
    public async Task Kept_import_originals_are_not_served(string shape)
    {
        var key = string.Format(shape, Guid.NewGuid());
        await Storage.UploadAsync(key, new MemoryStream([1, 2, 3]), "application/zip");
        (await Anonymous().GetAsync($"/uploads/{key}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var stillThere = await Storage.DownloadAsync(key);   // the API itself still reads it
        stillThere.Length.Should().Be(3);
    }

    [Theory]
    [InlineData("/uploads/%2e%2e/appsettings.json")]
    [InlineData("/uploads/a/%2e%2e/%2e%2e/etc/passwd")]
    [InlineData("/uploads/a%5c..%5csecret")]
    [InlineData("/uploads/")]
    [InlineData("/uploads/no/such/file.png")]
    public async Task Anything_else_is_not_found(string path)
    {
        (await Anonymous().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_image_uploaded_in_the_editor_loads_at_the_url_the_document_stores()
    {
        await SeedUserAsync("uploads-author");
        var doc = await SeedDocumentAsync("uploads-author");
        var url = await UploadThroughTheEditorAsync(CreateClientAs("uploads-author"), doc.Id);
        var path = new Uri(new Uri("http://x"), url).AbsolutePath;   // the tests' local store bakes in a host
        path.Should().StartWith("/uploads/uploads-author/documents/");
        (await Anonymous().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    internal static async Task<string> UploadThroughTheEditorAsync(HttpClient author, Guid docId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(OnePixelPng());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "diagram.png");
        var response = await author.PostAsync($"/api/documents/{docId}/assets/upload", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("url").GetString()!;
    }
}

/// <summary>
/// The same API with R2 settings (as production gets from ~/.config/lilia/r2.env), against a local S3 server: an
/// editor upload lands in the bucket, its stored URL is /uploads/…, the route streams it back, a delete removes it,
/// and the staff usage line reads the bucket. Nothing is written to the local folder.
/// </summary>
public class UploadsFromR2Tests : IntegrationTestBase, IClassFixture<LocalS3Server>
{
    private const string Staff = "r2-staff";
    private readonly LocalS3Server _s3;
    private readonly string _bucket;
    private readonly string _localFolder = Path.Combine(Path.GetTempPath(), "lilia-r2-route-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _r2;

    public UploadsFromR2Tests(TestDatabaseFixture fixture, LocalS3Server s3) : base(fixture)
    {
        _s3 = s3;
        _bucket = s3.NewBucketAsync().GetAwaiter().GetResult();
        _r2 = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("R2:Endpoint", s3.Endpoint);
            b.UseSetting("R2:BucketName", _bucket);
            b.UseSetting("R2:AccessKeyId", LocalS3Server.AccessKey);
            b.UseSetting("R2:SecretAccessKey", LocalS3Server.SecretKey);
            b.UseSetting("Storage:LocalPath", _localFolder);
            b.UseSetting("Storage:LocalPublicUrl", "/uploads");
            b.ConfigureTestServices(s => s.PostConfigure<Lilia.Api.Services.EntitlementOptions>(o => o.StaffUserIds = [Staff]));
        });
    }

    public override async Task DisposeAsync()
    {
        await _r2.DisposeAsync();
        await base.DisposeAsync();
    }

    private HttpClient As(string userId)
    {
        var c = _r2.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        return c;
    }

    [Fact]
    public async Task An_editor_upload_lives_in_the_bucket_and_loads_through_uploads()
    {
        _r2.Services.GetRequiredService<IStorageService>().Should().BeOfType<Lilia.Api.Services.R2StorageService>();
        await SeedUserAsync("r2-author");
        var doc = await SeedDocumentAsync("r2-author");

        var url = await UploadsRouteTests.UploadThroughTheEditorAsync(As("r2-author"), doc.Id);
        url.Should().StartWith("/uploads/r2-author/documents/", "the stored URL format is unchanged");
        var key = url["/uploads/".Length..];

        var head = await _s3.Client.GetObjectMetadataAsync(_bucket, key);
        head.Headers.ContentType.Should().Be("image/png");

        var anon = _r2.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Test-Anonymous", "1");
        var r = await anon.GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await r.Content.ReadAsByteArrayAsync()).Length.Should().Be((int)head.ContentLength);
        r.Headers.ETag.Should().NotBeNull();

        // A browser asking again with the ETag gets "not modified".
        var again = new HttpRequestMessage(HttpMethod.Get, url);
        again.Headers.IfNoneMatch.Add(r.Headers.ETag!);
        (await anon.SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        var keptHere = Directory.Exists(_localFolder) && Directory.EnumerateFiles(_localFolder, "*", SearchOption.AllDirectories).Any();
        keptHere.Should().BeFalse("no file is kept on the machine");

        // Deleting the asset deletes the object.
        var assets = JsonDocument.Parse(await (await As("r2-author").GetAsync($"/api/documents/{doc.Id}/assets")).Content.ReadAsStringAsync());
        var assetId = assets.RootElement[0].GetProperty("id").GetGuid();
        (await As("r2-author").DeleteAsync($"/api/documents/{doc.Id}/assets/{assetId}")).IsSuccessStatusCode.Should().BeTrue();
        (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await _s3.Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = key });
        (list.S3Objects ?? []).Should().BeEmpty();
    }

    [Fact]
    public async Task Storage_usage_is_for_staff_only_and_reads_the_bucket()
    {
        await using var store = new MemoryStream(UploadsRouteTests.OnePixelPng());
        await _r2.Services.GetRequiredService<IStorageService>().UploadAsync("usage/one.png", store, "image/png");

        (await As("not-staff").GetAsync("/api/admin/storage-usage")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var anon = _r2.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Test-Anonymous", "1");
        (await anon.GetAsync("/api/admin/storage-usage")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var r = await As(Staff).GetAsync("/api/admin/storage-usage");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("provider").GetString().Should().Be("r2");
        json.RootElement.GetProperty("objects").GetInt64().Should().BeGreaterThanOrEqualTo(1);
        json.RootElement.GetProperty("classAThisMonth").GetInt64().Should().BeGreaterThanOrEqualTo(2, "the upload and the list");
        json.RootElement.GetProperty("line").GetString().Should().Contain("% of 10 GB free").And.Contain("Within the free tier");
        json.RootElement.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }
}
