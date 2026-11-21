using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Lilia.Core.Interfaces;

namespace Lilia.Api.Services;

/// <summary>
/// Files in a Cloudflare R2 bucket (S3 API), chosen when R2:Endpoint is set (Program.cs).
///
/// <para>21 Nov 2026, the owner: "We should not keep any files on the machine. Everything should be in R2." The bucket
/// is <b>private</b>: no public bucket URL, no r2.dev address. Every read goes through the API: the services read with
/// <see cref="DownloadAsync"/>, and browsers load <c>/uploads/{key}</c>, which the API streams from the bucket
/// (Program.cs, <see cref="OpenReadAsync"/>). So the URLs stored in documents (<c>/uploads/…</c>) keep working, and
/// R2:PublicUrl, which this class used to require and bake into every URL, is no longer read.</para>
///
/// <para>Settings: R2:Endpoint (https://&lt;account&gt;.r2.cloudflarestorage.com), R2:BucketName, R2:AccessKeyId,
/// R2:SecretAccessKey; R2:Region (default "auto", R2's). Path-style addressing, which R2 and the tests' local S3
/// server both take.</para>
///
/// <para>The move: while Storage:LocalPath still holds files (between switching production to R2 and deleting the local
/// copies after the migration is verified), a file R2 doesn't have is read from that folder, and a delete removes both.
/// So nothing breaks in the minutes between the switch and the last copy. R2:LocalFallback=false turns it off; an
/// empty or missing folder costs nothing.</para>
/// </summary>
public class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _publicUrl;
    private readonly bool _https;
    private readonly StorageOpsMeter? _meter;
    private readonly LocalStorageService? _fallback;

    public R2StorageService(IConfiguration configuration, StorageOpsMeter meter, ILogger<R2StorageService> logger)
    {
        var credentials = new BasicAWSCredentials(
            configuration["R2:AccessKeyId"] ?? throw new InvalidOperationException("R2:AccessKeyId not configured"),
            configuration["R2:SecretAccessKey"] ?? throw new InvalidOperationException("R2:SecretAccessKey not configured")
        );
        var endpoint = configuration["R2:Endpoint"] ?? throw new InvalidOperationException("R2:Endpoint not configured");

        _s3Client = new AmazonS3Client(credentials, ClientConfig(endpoint, configuration["R2:Region"]));
        _bucketName = configuration["R2:BucketName"] ?? throw new InvalidOperationException("R2:BucketName not configured");
        _publicUrl = PublicBase(configuration);
        _https = endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        _meter = meter;
        if (!string.IsNullOrEmpty(configuration["R2:PublicUrl"]))
            logger.LogWarning("[Storage] R2:PublicUrl is set and ignored: the bucket stays private and files are served at {Base}", _publicUrl);
        if (configuration.GetValue("R2:LocalFallback", true) && configuration["Storage:LocalPath"] is { Length: > 0 } local
            && Directory.Exists(local) && Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories).Any())
        {
            _fallback = new LocalStorageService(configuration);
            Console.WriteLine($"[BOOT] storage: files not yet in R2 are read from {Path.GetFullPath(local)} until it is emptied");
        }
    }

    /// <summary>For tests: any S3 client (a mock, or one pointed at a local S3 server).</summary>
    internal R2StorageService(IAmazonS3 client, string bucketName, string publicUrl, bool https, StorageOpsMeter? meter = null,
        LocalStorageService? fallback = null)
    {
        _s3Client = client;
        _bucketName = bucketName;
        _publicUrl = publicUrl;
        _https = https;
        _meter = meter;
        _fallback = fallback;
    }

    internal static AmazonS3Config ClientConfig(string endpoint, string? region) => new()
    {
        ServiceURL = endpoint,
        ForcePathStyle = true,
        AuthenticationRegion = string.IsNullOrEmpty(region) ? "auto" : region,
        // AWS SDK v4 defaults to STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER
        // for chunked uploads + checksums; Cloudflare R2 doesn't implement
        // that signing variant yet (returns 501 "not implemented"). Force
        // classic SigV4 by disabling payload chunking + trailing checksums.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    };

    /// <summary>Where browsers load a file: the API's own /uploads route, never the bucket.</summary>
    internal static string PublicBase(IConfiguration configuration) =>
        (configuration["Storage:PublicUrl"] ?? configuration["Storage:LocalPublicUrl"] ?? "/uploads").TrimEnd('/');

    public async Task<string> UploadAsync(string key, Stream content, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            // Belt & suspenders: even with RequestChecksumCalculation set on
            // the client config, some AWS SDK v4 code paths still negotiate
            // the streaming trailer. Disable payload signing on the request
            // too so R2 gets a clean PUT it can validate. (HTTPS only: the SDK
            // signs the payload over plain HTTP, the tests' local server.)
            DisablePayloadSigning = _https,
        };

        _meter?.ClassA(_bucketName);
        await _s3Client.PutObjectAsync(request);
        return GetPublicUrl(key);
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        using var timed = Lilia.Core.Diagnostics.Timing.Measure("storage.download");
        try
        {
            _meter?.ClassB(_bucketName);
            using var response = await _s3Client.GetObjectAsync(_bucketName, key);
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (_fallback is not null)
            {
                try { return await _fallback.DownloadAsync(key); }
                catch (Exception) { /* not there either */ }
            }
            // As the local store: callers tell "no such file" from "storage is down".
            throw new FileNotFoundException($"File not found: {key}", key, ex);
        }
    }

    public async Task<StoredFile?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            _meter?.ClassB(_bucketName);
            var response = await _s3Client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = key }, ct);
            var type = string.IsNullOrEmpty(response.Headers.ContentType) || response.Headers.ContentType == "binary/octet-stream"
                ? StorageContentTypes.For(key)
                : response.Headers.ContentType;
            DateTimeOffset? modified = response.LastModified is { } m ? new DateTimeOffset(DateTime.SpecifyKind(m.ToUniversalTime(), DateTimeKind.Utc)) : null;
            return new StoredFile(response.ResponseStream, type, response.ContentLength >= 0 ? response.ContentLength : null,
                string.IsNullOrEmpty(response.ETag) ? null : response.ETag, modified);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (_fallback is null) return null;
            try { return await _fallback.OpenReadAsync(key, ct); }
            catch (ArgumentException) { return null; }
        }
    }

    public async Task DeleteAsync(string key)
    {
        // Deletes are free on R2 (no operation class).
        await _s3Client.DeleteObjectAsync(_bucketName, key);
        if (_fallback is not null) await _fallback.DeleteAsync(key);
    }

    public string GetPublicUrl(string key)
    {
        return $"{_publicUrl}/{key}";
    }

    public Task<string> GeneratePresignedUploadUrl(string key, string contentType, TimeSpan? expiry = null)
    {
        // Short-lived: 15 minutes by default, never more than an hour.
        var lifetime = expiry ?? TimeSpan.FromMinutes(15);
        if (lifetime > TimeSpan.FromHours(1)) lifetime = TimeSpan.FromHours(1);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Protocol = _https ? Protocol.HTTPS : Protocol.HTTP,
            Expires = DateTime.UtcNow.Add(lifetime)
        };

        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }

    public async Task<StorageUsage> GetUsageAsync(CancellationToken ct = default)
    {
        long objects = 0, bytes = 0;
        string? token = null;
        do
        {
            _meter?.ClassA(_bucketName);
            var page = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucketName, ContinuationToken = token }, ct);
            foreach (var o in page.S3Objects ?? [])
            {
                objects++;
                bytes += o.Size ?? 0;
            }
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (token is not null);

        if (_meter is null) return new StorageUsage("r2", _bucketName, objects, bytes);
        var (a, b, since) = await _meter.ThisMonthAsync(_bucketName);
        return new StorageUsage("r2", _bucketName, objects, bytes, a, b, since);
    }
}
