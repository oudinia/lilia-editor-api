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
            throw new LiliaValidationException(
                $"File size {fileSizeMb:F1} MB exceeds the maximum allowed size of {_options.MaxFileSizeMb} MB.",
                LiliaErrorCodes.ImportFileTooLarge);

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
            throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.MathpixAuthFailed,
                "PDF import is unavailable. Please contact support.",
                statusCode: 503,
                internalDetails: "Mathpix API returned 401 — check MATHPIX__APPID and MATHPIX__APPKEY environment variables.");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.MathpixRateLimited,
                "PDF import is temporarily unavailable due to high demand. Please try again in a few minutes.",
                statusCode: 503);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.ExternalServiceError,
                "PDF import service returned an unexpected error. Please try again.",
                statusCode: 502,
                internalDetails: $"Mathpix HTTP {(int)response.StatusCode}: {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MathpixPdfResponse>(responseBody)
            ?? throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.ExternalServiceError,
                "PDF import service returned an unreadable response. Please try again.",
                internalDetails: "Failed to deserialize MathpixPdfResponse");

        _logger.LogInformation("[Mathpix] PDF submitted, pdf_id: {PdfId}", result.PdfId);
        return result.PdfId;
    }

    public async Task<MathpixPdfStatus> GetStatusAsync(string pdfId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/v3/pdf/{pdfId}", ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.MathpixAuthFailed,
                "PDF import is unavailable. Please contact support.",
                statusCode: 503,
                internalDetails: "Mathpix API returned 401 on status poll — credentials may have expired.");

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.MathpixRateLimited,
                "PDF import is temporarily unavailable due to high demand. Please try again in a few minutes.",
                statusCode: 503);

        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<MathpixPdfStatus>(responseBody)
            ?? throw new LiliaExternalServiceException(
                "Mathpix", LiliaErrorCodes.ExternalServiceError,
                "PDF import service returned an unreadable response.",
                internalDetails: "Failed to deserialize MathpixPdfStatus");
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
                    throw new LiliaExternalServiceException(
                        "Mathpix", LiliaErrorCodes.MathpixProcessingFailed,
                        "PDF processing failed. The file may be encrypted, corrupted, or contain unsupported content.",
                        internalDetails: $"Mathpix processing error for pdf_id {pdfId}: {status.Error ?? "Unknown error"}");
            }

            await Task.Delay(pollInterval, ct);
        }

        throw new LiliaExternalServiceException(
            "Mathpix", LiliaErrorCodes.MathpixTimeout,
            "PDF processing is taking longer than expected. Please try again.",
            internalDetails: $"Mathpix timed out after {_options.TimeoutSeconds}s for pdf_id: {pdfId}");
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppKey))
        {
            _logger.LogCritical(
                "[Mathpix] PDF parser is set to 'mathpix' but MATHPIX__APPID or MATHPIX__APPKEY are not configured. " +
                "Set these as environment variables in the deployment.");
            return false;
        }

        try
        {
            var response = await _httpClient.GetAsync("/v3/pdf", ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogCritical(
                    "[Mathpix] Credentials rejected by Mathpix API (401). " +
                    "Verify MATHPIX__APPID and MATHPIX__APPKEY are correct.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Mathpix] API availability check failed — service may be unreachable");
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
