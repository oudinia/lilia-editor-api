using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Lilia.Core.Interfaces;

namespace Lilia.Api.Services;

public class R2StorageService : IStorageService
{
    private readonly AmazonS3Client _s3Client;
    private readonly string _bucketName;
    private readonly string _publicUrl;

    public R2StorageService(IConfiguration configuration)
    {
        var credentials = new BasicAWSCredentials(
            configuration["R2:AccessKeyId"] ?? throw new InvalidOperationException("R2:AccessKeyId not configured"),
            configuration["R2:SecretAccessKey"] ?? throw new InvalidOperationException("R2:SecretAccessKey not configured")
        );

        var config = new AmazonS3Config
        {
            ServiceURL = configuration["R2:Endpoint"] ?? throw new InvalidOperationException("R2:Endpoint not configured"),
        };

        _s3Client = new AmazonS3Client(credentials, config);
        _bucketName = configuration["R2:BucketName"] ?? throw new InvalidOperationException("R2:BucketName not configured");
        _publicUrl = configuration["R2:PublicUrl"] ?? throw new InvalidOperationException("R2:PublicUrl not configured");
    }

    public async Task<string> UploadAsync(string key, Stream content, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request);
        return GetPublicUrl(key);
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, key);
        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task DeleteAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(_bucketName, key);
    }

    public string GetPublicUrl(string key)
    {
        return $"{_publicUrl}/{key}";
    }

    public Task<string> GeneratePresignedUploadUrl(string key, string contentType, TimeSpan? expiry = null)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromMinutes(15))
        };

        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }
}
