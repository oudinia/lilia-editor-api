using System.Net.Http.Headers;
using System.Text.Json;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lilia.Import.Services;

/// <summary>
/// HttpClient-based implementation for calling the MinerU PDF parsing API.
/// </summary>
public class MineruClient : IMineruClient
{
    private readonly HttpClient _httpClient;
    private readonly MineruOptions _options;
    private readonly ILogger<MineruClient> _logger;

    public MineruClient(HttpClient httpClient, IOptions<MineruOptions> options, ILogger<MineruClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<MineruParseResponse> ParsePdfAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PDF file not found", filePath);

        var fileInfo = new FileInfo(filePath);
        var maxBytes = (long)_options.MaxFileSizeMb * 1024 * 1024;
        if (fileInfo.Length > maxBytes)
            throw new InvalidOperationException($"PDF file exceeds maximum size of {_options.MaxFileSizeMb} MB");

        _logger.LogInformation("[MinerU] Parsing PDF: {FilePath} ({SizeMb:F1} MB)", filePath, fileInfo.Length / (1024.0 * 1024.0));

        using var content = new MultipartFormDataContent();
        var fileStream = File.OpenRead(filePath);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(streamContent, "files", Path.GetFileName(filePath));
        content.Add(new StringContent("true"), "return_content_list");
        content.Add(new StringContent("true"), "return_images");
        content.Add(new StringContent("auto"), "parse_method");
        content.Add(new StringContent("pipeline"), "backend");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("file_parse", content, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"MinerU did not respond within {_options.TimeoutSeconds} seconds");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[MinerU] Failed to connect to MinerU at {BaseUrl}", _options.BaseUrl);
            throw new InvalidOperationException($"Cannot connect to MinerU service at {_options.BaseUrl}. Ensure the MinerU container is running.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("[MinerU] Parse failed with status {StatusCode}: {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"MinerU returned {(int)response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var apiResponse = JsonSerializer.Deserialize<MineruApiResponse>(json);

        if (apiResponse == null || apiResponse.Results.Count == 0)
            throw new InvalidOperationException("MinerU returned an empty or invalid response");

        // Extract the first (and only) file result
        var fileResult = apiResponse.Results.Values.First();

        // content_list is a JSON string that needs separate deserialization
        var contentList = new List<MineruContentBlock>();
        if (!string.IsNullOrEmpty(fileResult.ContentListJson))
        {
            contentList = JsonSerializer.Deserialize<List<MineruContentBlock>>(fileResult.ContentListJson) ?? [];
        }

        var result = new MineruParseResponse
        {
            ContentList = contentList,
            Images = fileResult.Images
        };

        _logger.LogInformation("[MinerU] Parsed {BlockCount} content blocks (backend: {Backend}, version: {Version})",
            result.ContentList.Count, apiResponse.Backend, apiResponse.Version);
        return result;
    }

    public async Task<byte[]> GetImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync($"images/{imagePath}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[MinerU] Failed to fetch image: {ImagePath}", imagePath);
            return [];
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("docs", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
