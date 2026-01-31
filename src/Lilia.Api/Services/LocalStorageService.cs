using Lilia.Core.Interfaces;

namespace Lilia.Api.Services;

public class LocalStorageService : IStorageService
{
    private readonly string _basePath;
    private readonly string _publicUrl;

    public LocalStorageService(IConfiguration configuration)
    {
        _basePath = configuration["Storage:LocalPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        _publicUrl = configuration["Storage:LocalPublicUrl"] ?? "http://localhost:5000/uploads";
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> UploadAsync(string key, Stream content, string contentType)
    {
        var filePath = Path.Combine(_basePath, key.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = File.Create(filePath);
        await content.CopyToAsync(fileStream);

        return GetPublicUrl(key);
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        var filePath = Path.Combine(_basePath, key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {key}");
        }

        var memoryStream = new MemoryStream();
        using var fileStream = File.OpenRead(filePath);
        await fileStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public Task DeleteAsync(string key)
    {
        var filePath = Path.Combine(_basePath, key.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string key)
    {
        return $"{_publicUrl}/{key}";
    }

    public Task<string> GeneratePresignedUploadUrl(string key, string contentType, TimeSpan? expiry = null)
    {
        // For local storage, we don't have presigned URLs
        // Return a direct upload endpoint
        return Task.FromResult($"{_publicUrl.Replace("/uploads", "/api/upload")}/{key}");
    }
}
