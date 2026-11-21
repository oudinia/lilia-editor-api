namespace Lilia.Core.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(string key, Stream content, string contentType);
    Task<Stream> DownloadAsync(string key);
    Task DeleteAsync(string key);
    string GetPublicUrl(string key);
    Task<string> GeneratePresignedUploadUrl(string key, string contentType, TimeSpan? expiry = null);

    /// <summary>
    /// The stored file to stream to a browser (the /uploads route), with its type and validators; null when there is
    /// no such file. 21 Nov 2026: files move to a private R2 bucket, so every read goes through the API. The default
    /// (for test doubles) reads the whole file through <see cref="DownloadAsync"/>.
    /// </summary>
    async Task<StoredFile?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var stream = await DownloadAsync(key);
            return new StoredFile(stream, StorageContentTypes.For(key), stream.CanSeek ? stream.Length : null, null, null);
        }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>What the store holds (objects and bytes) and, for R2, the operations counted this month.</summary>
    Task<StorageUsage> GetUsageAsync(CancellationToken ct = default) =>
        throw new NotSupportedException($"{GetType().Name} does not report its usage.");
}

/// <summary>A stored file opened for reading. Disposing it releases the stream and anything behind it.</summary>
public sealed record StoredFile(Stream Content, string ContentType, long? Length, string? ETag, DateTimeOffset? LastModified)
    : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// A store's size and, when counted, its operations this calendar month (UTC). R2 bills Class A operations (writes,
/// lists) and Class B (reads) per month; the counts are this API's own, not Cloudflare's.
/// </summary>
public sealed record StorageUsage(
    string Provider,
    string Location,
    long Objects,
    long Bytes,
    long? ClassAThisMonth = null,
    long? ClassBThisMonth = null,
    string? OperationsCountedSince = null);

/// <summary>A file's content type from its key's extension, for stores that keep none (the local folder).</summary>
public static class StorageContentTypes
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".svg"] = "image/svg+xml", [".bmp"] = "image/bmp", [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff", [".eps"] = "application/postscript", [".pdf"] = "application/pdf",
        [".zip"] = "application/zip", [".epub"] = "application/epub+zip", [".tex"] = "text/plain",
        [".bib"] = "text/plain", [".txt"] = "text/plain", [".csv"] = "text/csv", [".json"] = "application/json",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    public static string For(string key) =>
        Types.TryGetValue(Path.GetExtension(key), out var type) ? type : "application/octet-stream";
}
