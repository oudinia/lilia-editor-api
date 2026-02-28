using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lilia.Import.Services;

public class MathpixClient : IMathpixClient
{
    private readonly HttpClient _httpClient;
    private readonly MathpixOptions _options;
    private readonly ILogger<MathpixClient> _logger;

    public MathpixClient(HttpClient httpClient, IOptions<MathpixOptions> options, ILogger<MathpixClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("app_id", _options.AppId);
        _httpClient.DefaultRequestHeaders.Add("app_key", _options.AppKey);
    }

    public async Task<string> SubmitPdfAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("PDF file not found", filePath);

        var fileSizeMb = fileInfo.Length / (1024.0 * 1024.0);
        if (fileSizeMb > _options.MaxFileSizeMb)
            throw new InvalidOperationException($"File size {fileSizeMb:F1}MB exceeds maximum {_options.MaxFileSizeMb}MB");

        using var content = new MultipartFormDataContent();

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        var optionsJson = JsonSerializer.Serialize(new MathpixPdfRequest());
        content.Add(new StringContent(optionsJson, System.Text.Encoding.UTF8, "application/json"), "options_json");

        _logger.LogInformation("[Mathpix] Submitting PDF: {FileName} ({SizeMb:F1}MB)", fileInfo.Name, fileSizeMb);

        var response = await _httpClient.PostAsync("/v3/pdf", content, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Mathpix API authentication failed. Check app_id and app_key.");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Mathpix API rate limit exceeded. Please retry later.");

        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MathpixPdfResponse>(responseBody)
            ?? throw new InvalidOperationException("Failed to deserialize Mathpix PDF submission response");

        _logger.LogInformation("[Mathpix] PDF submitted, pdf_id: {PdfId}", result.PdfId);
        return result.PdfId;
    }

    public async Task<MathpixPdfStatus> GetStatusAsync(string pdfId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/v3/pdf/{pdfId}", ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Mathpix API authentication failed. Check app_id and app_key.");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Mathpix API rate limit exceeded. Please retry later.");

        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<MathpixPdfStatus>(responseBody)
            ?? throw new InvalidOperationException("Failed to deserialize Mathpix status response");
    }

    public async Task<string> WaitForCompletionAsync(string pdfId, CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var pollInterval = TimeSpan.FromMilliseconds(_options.PollIntervalMs);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            var status = await GetStatusAsync(pdfId, ct);

            _logger.LogDebug("[Mathpix] PDF {PdfId} status: {Status}, progress: {Percent:F0}%",
                pdfId, status.Status, status.PercentDone ?? 0);

            switch (status.Status)
            {
                case "completed":
                    if (string.IsNullOrWhiteSpace(status.Markdown))
                    {
                        // Markdown not included in status response — fetch it from the .md endpoint
                        var mdResponse = await _httpClient.GetAsync($"/v3/pdf/{pdfId}.md", ct);
                        mdResponse.EnsureSuccessStatusCode();
                        return await mdResponse.Content.ReadAsStringAsync(ct);
                    }
                    return status.Markdown;

                case "error":
                    throw new InvalidOperationException($"Mathpix PDF processing failed: {status.Error ?? "Unknown error"}");
            }

            await Task.Delay(pollInterval, ct);
        }

        throw new TimeoutException($"Mathpix PDF processing timed out after {_options.TimeoutSeconds}s for pdf_id: {pdfId}");
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppKey))
            return false;

        try
        {
            var response = await _httpClient.GetAsync("/v3/pdf", ct);
            // Any response (even 405) means the API is reachable
            return response.StatusCode != HttpStatusCode.Unauthorized;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Mathpix] API availability check failed");
            return false;
        }
    }

    public async Task<byte[]> GetImageAsync(string imageUrl, CancellationToken ct = default)
    {
        // Mathpix returns full URLs for images in markdown output
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
