namespace Lilia.Core.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(string key, Stream content, string contentType);
    Task<Stream> DownloadAsync(string key);
    Task DeleteAsync(string key);
    string GetPublicUrl(string key);
    Task<string> GeneratePresignedUploadUrl(string key, string contentType, TimeSpan? expiry = null);
}
