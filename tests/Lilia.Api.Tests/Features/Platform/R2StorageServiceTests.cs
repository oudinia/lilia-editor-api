using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Platform;

/// <summary>
/// The R2 store against a local S3 server (21 Nov 2026: production's files move to a private R2 bucket). Upload, read,
/// delete, presigned upload, usage, and that no URL ever points at the bucket.
/// </summary>
public class R2StorageServiceTests(LocalS3Server s3) : IClassFixture<LocalS3Server>
{
    private static byte[] Bytes(int n) => RandomNumberGenerator.GetBytes(n);

    private static StorageOpsMeter Meter() =>
        new(new ServiceCollection().BuildServiceProvider(), new ConfigurationBuilder().Build());

    [Fact]
    public async Task A_file_goes_up_and_comes_back_with_its_type_size_and_checksum()
    {
        var store = s3.Storage(await s3.NewBucketAsync());
        var body = Bytes(70_000);
        var url = await store.UploadAsync("u1/documents/d1/images/a.png", new MemoryStream(body), "image/png");
        url.Should().Be("/uploads/u1/documents/d1/images/a.png", "documents keep /uploads/ URLs, served by the API");

        await using (var back = await store.DownloadAsync("u1/documents/d1/images/a.png"))
        {
            var ms = new MemoryStream(); await back.CopyToAsync(ms);
            ms.ToArray().Should().Equal(body);
        }

        await using var file = await store.OpenReadAsync("u1/documents/d1/images/a.png");
        file.Should().NotBeNull();
        file!.ContentType.Should().Be("image/png");
        file.Length.Should().Be(body.Length);
        file.ETag.Should().Be($"\"{Convert.ToHexStringLower(MD5.HashData(body))}\"");
        file.LastModified.Should().NotBeNull();
        var read = new MemoryStream(); await file.Content.CopyToAsync(read);
        read.ToArray().Should().Equal(body);
    }

    [Fact]
    public async Task A_missing_file_is_not_found_as_with_the_local_folder()
    {
        var store = s3.Storage(await s3.NewBucketAsync());
        var act = () => store.DownloadAsync("nope/missing.png");
        await act.Should().ThrowAsync<FileNotFoundException>();
        (await store.OpenReadAsync("nope/missing.png")).Should().BeNull();
    }

    [Fact]
    public async Task Delete_removes_the_file_and_a_second_delete_is_harmless()
    {
        var store = s3.Storage(await s3.NewBucketAsync());
        await store.UploadAsync("k/x.jpg", new MemoryStream(Bytes(10)), "image/jpeg");
        await store.DeleteAsync("k/x.jpg");
        (await store.OpenReadAsync("k/x.jpg")).Should().BeNull();
        await store.DeleteAsync("k/x.jpg");
    }

    [Fact]
    public async Task A_presigned_upload_is_short_lived_and_works()
    {
        var bucket = await s3.NewBucketAsync();
        var store = s3.Storage(bucket);
        var url = await store.GeneratePresignedUploadUrl("u1/documents/d1/images/p.png", "image/png");
        url.Should().StartWith(s3.Endpoint).And.Contain("X-Amz-Expires=900");

        var body = Bytes(2048);
        using var http = new HttpClient();
        var put = new ByteArrayContent(body);
        put.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await http.PutAsync(url, put)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var file = await store.OpenReadAsync("u1/documents/d1/images/p.png");
        file!.Length.Should().Be(2048);

        (await store.GeneratePresignedUploadUrl("k", "image/png", TimeSpan.FromHours(5)))
            .Should().Contain("X-Amz-Expires=3600", "never more than an hour");
        // Without the signature the bucket refuses: it is private.
        // (The local server answers 400 to an unsigned request, R2 403: either way, no file.)
        var unsigned = await http.GetAsync($"{s3.Endpoint}/{bucket}/u1/documents/d1/images/p.png");
        ((int)unsigned.StatusCode).Should().BeInRange(400, 403);
    }

    [Fact]
    public async Task Usage_counts_objects_bytes_and_this_months_operations()
    {
        var bucket = await s3.NewBucketAsync();
        var meter = Meter();
        var store = s3.Storage(bucket, meter);
        await store.UploadAsync("a/1.png", new MemoryStream(Bytes(100)), "image/png");
        await store.UploadAsync("a/2.png", new MemoryStream(Bytes(250)), "image/png");
        await using (await store.DownloadAsync("a/1.png")) { }

        var usage = await store.GetUsageAsync();
        usage.Provider.Should().Be("r2");
        usage.Location.Should().Be(bucket);
        usage.Objects.Should().Be(2);
        usage.Bytes.Should().Be(350);
        usage.ClassAThisMonth.Should().Be(3, "two writes and the list");
        usage.ClassBThisMonth.Should().Be(1);
        usage.OperationsCountedSince.Should().NotBeNullOrEmpty();
    }

    // The move: between production's switch to R2 and the last copy, a file R2 lacks is read from the old folder,
    // and a delete removes both, so the later delete-local step never finds an orphan.
    [Fact]
    public async Task During_the_move_a_file_not_yet_in_R2_is_read_from_the_local_folder()
    {
        var folder = Directory.CreateTempSubdirectory("lilia-r2-fallback-").FullName;
        try
        {
            var local = new LocalStorageService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Storage:LocalPath"] = folder }).Build());
            await local.UploadAsync("u1/documents/d1/images/old.png", new MemoryStream([1, 2, 3]), "image/png");
            var store = new R2StorageService(s3.Client, await s3.NewBucketAsync(), "/uploads", https: false, fallback: local);

            await using (var file = await store.OpenReadAsync("u1/documents/d1/images/old.png"))
                file!.Length.Should().Be(3);
            await using (var bytes = await store.DownloadAsync("u1/documents/d1/images/old.png"))
                bytes.Length.Should().Be(3);

            await store.DeleteAsync("u1/documents/d1/images/old.png");
            File.Exists(Path.Combine(folder, "u1/documents/d1/images/old.png")).Should().BeFalse();
            (await store.OpenReadAsync("u1/documents/d1/images/old.png")).Should().BeNull();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void The_store_needs_no_public_bucket_url_and_ignores_one()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["R2:Endpoint"] = s3.Endpoint, ["R2:BucketName"] = "b", ["R2:AccessKeyId"] = "k", ["R2:SecretAccessKey"] = "s",
        }).Build();
        new R2StorageService(config, Meter(), NullLogger<R2StorageService>.Instance).GetPublicUrl("a/b.png").Should().Be("/uploads/a/b.png");

        config["R2:PublicUrl"] = "https://pub-123.r2.dev";
        config["Storage:LocalPublicUrl"] = "/uploads";
        new R2StorageService(config, Meter(), NullLogger<R2StorageService>.Instance).GetPublicUrl("a/b.png")
            .Should().Be("/uploads/a/b.png", "the bucket is private: no URL points at it");
    }
}
