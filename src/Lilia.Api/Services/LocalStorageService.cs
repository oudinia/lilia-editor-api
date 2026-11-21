using Lilia.Core.Interfaces;

namespace Lilia.Api.Services;

/// <summary>
/// Files in a folder on this machine (Storage:LocalPath): development, the tests and the test copy. Production moves
/// to <see cref="R2StorageService"/> (21 Nov 2026, the owner: "We should not keep any files on the machine").
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly string _basePath;
    private readonly string _publicUrl;

    public LocalStorageService(IConfiguration configuration)
    {
        _basePath = Path.GetFullPath(configuration["Storage:LocalPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads"));
        _publicUrl = configuration["Storage:PublicUrl"] ?? configuration["Storage:LocalPublicUrl"] ?? "http://localhost:5000/uploads";
        Directory.CreateDirectory(_basePath);
    }

    /// <summary>
    /// The key's file, always inside the folder (21 Nov 2026: the /uploads route now passes a key from the address
    /// to the store, so "../" must never leave it).
    /// </summary>
    private string PathOf(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_basePath, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"Storage key leaves the storage folder: {key}", nameof(key));
        return full;
    }

    public async Task<string> UploadAsync(string key, Stream content, string contentType)
    {
        var filePath = PathOf(key);
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
        var filePath = PathOf(key);
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

    public Task<StoredFile?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var info = new FileInfo(PathOf(key));
        if (!info.Exists) return Task.FromResult<StoredFile?>(null);
        var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        // The validators static files gave: length and modification time.
        var etag = $"\"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}\"";
        return Task.FromResult<StoredFile?>(new StoredFile(stream, StorageContentTypes.For(key), info.Length, etag,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    public Task DeleteAsync(string key)
    {
        var filePath = PathOf(key);
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

    public Task<StorageUsage> GetUsageAsync(CancellationToken ct = default)
    {
        long objects = 0, bytes = 0;
        foreach (var f in new DirectoryInfo(_basePath).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            objects++;
            bytes += f.Length;
        }
        return Task.FromResult(new StorageUsage("local", _basePath, objects, bytes));
    }
}
