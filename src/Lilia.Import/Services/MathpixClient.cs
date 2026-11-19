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

        // app_key alone is a complete credential. app_id is the older paired
        // form; keys issued from the Mathpix console today come on their own,
        // and sending an empty app_id header alongside a valid key is worse
        // than sending none — it is a header Mathpix may reject outright.
        if (!string.IsNullOrWhiteSpace(_options.AppId))
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

        // tex.zip with the Markdown, and improve_mathpix off (the request's default), PDF import via tex.zip, 19 Nov 2026.
        var request = new MathpixPdfRequest();
        if (_options.RequestTexZip) request.ConversionFormats.TexZip = true;
        var optionsJson = JsonSerializer.Serialize(request);
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
        // Only the key is required. Demanding app_id as well rejected a
        // perfectly valid credential before a single request was made.
        if (string.IsNullOrWhiteSpace(_options.AppKey))
        {
            _logger.LogCritical(
                "[Mathpix] PDF parser is set to 'mathpix' but MATHPIX__APPKEY is not configured. " +
                "Set it as an environment variable in the deployment.");
            return false;
        }

        try
        {
            // /v3/pdf-results, not /v3/pdf. The latter accepts POST only, so a
            // GET returned 404 whatever the credentials were — including none
            // at all. This check therefore reported "available" for a key that
            // Mathpix would reject on the very next call, which is the failure
            // it exists to catch. /v3/pdf-results answers 200 for a good key
            // and 401 for a bad one, so the two are now distinguishable.
            var response = await _httpClient.GetAsync("/v3/pdf-results", ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogCritical(
                    "[Mathpix] Credentials rejected by Mathpix API (401). " +
                    "Verify MATHPIX__APPKEY is correct.");
                return false;
            }
            if (!response.IsSuccessStatusCode)
            {
                // Not proof the credentials are bad, but not proof they are
                // good either. Say so rather than nodding it through.
                _logger.LogWarning(
                    "[Mathpix] Availability check returned {Status}; treating Mathpix as unavailable " +
                    "rather than assuming the credentials are valid.",
                    (int)response.StatusCode);
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

    public async Task<byte[]> GetTexZipAsync(string pdfId, CancellationToken ct = default)
    {
        // The PDF is read ("completed" on /v3/pdf/{id}) before its conversions are: each requested format has its own
        // status on /v3/converter/{id}, and its download answers 202 while it is still converting
        // (docs.mathpix.com/reference/post-v3-pdf; PDF import via tex.zip, 19 Nov 2026).
        var timeout = TimeSpan.FromSeconds(_options.TexZipTimeoutSeconds);
        var pollInterval = TimeSpan.FromMilliseconds(_options.PollIntervalMs);
        var startTime = DateTime.UtcNow;
        var id = Uri.EscapeDataString(pdfId);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _httpClient.GetAsync($"/v3/converter/{id}", ct);
            if (!response.IsSuccessStatusCode)
                throw new LiliaExternalServiceException(
                    "Mathpix", LiliaErrorCodes.MathpixProcessingFailed,
                    "PDF conversion to LaTeX failed.",
                    internalDetails: $"Mathpix converter status HTTP {(int)response.StatusCode} for pdf_id {pdfId}");

            var body = await response.Content.ReadAsStringAsync(ct);
            var converter = JsonSerializer.Deserialize<MathpixConverterStatus>(body);
            var texZip = converter?.ConversionStatus?.GetValueOrDefault("tex.zip");

            _logger.LogDebug("[Mathpix] PDF {PdfId} tex.zip status: {Status}", pdfId, texZip?.Status ?? "(none)");

            if (texZip?.Status == "completed")
            {
                var download = await _httpClient.GetAsync($"/v3/pdf/{id}.tex.zip", ct);
                // 200 only: 202 is "still converting", 404 "not there" (the same docs page).
                if (download.StatusCode != HttpStatusCode.OK)
                    throw new LiliaExternalServiceException(
                        "Mathpix", LiliaErrorCodes.MathpixProcessingFailed,
                        "PDF conversion to LaTeX failed.",
                        internalDetails: $"Mathpix tex.zip download HTTP {(int)download.StatusCode} for pdf_id {pdfId}");
                var bytes = await download.Content.ReadAsByteArrayAsync(ct);
                _logger.LogInformation("[Mathpix] PDF {PdfId} tex.zip downloaded ({Bytes} bytes)", pdfId, bytes.Length);
                return bytes;
            }

            if (texZip?.Status == "error")
                throw new LiliaExternalServiceException(
                    "Mathpix", LiliaErrorCodes.MathpixProcessingFailed,
                    "PDF conversion to LaTeX failed.",
                    internalDetails: $"Mathpix tex.zip conversion error for pdf_id {pdfId}: {texZip.ErrorInfo?.Id} {texZip.ErrorInfo?.Error}");

            if (DateTime.UtcNow - startTime >= timeout)
                throw new LiliaExternalServiceException(
                    "Mathpix", LiliaErrorCodes.MathpixTimeout,
                    "PDF conversion to LaTeX is taking longer than expected.",
                    internalDetails: $"Mathpix tex.zip not ready after {_options.TexZipTimeoutSeconds}s for pdf_id {pdfId} (last status: {texZip?.Status ?? "none"})");

            await Task.Delay(pollInterval, ct);
        }
    }

    public async Task<bool> DeletePdfAsync(string pdfId, CancellationToken ct = default)
    {
        // Removes the source document, page images, MMD and line data at once; CDN copies of the page images may
        // stay up to 10 minutes (docs.mathpix.com/concepts/data-retention; PDF import via tex.zip, 19 Nov 2026).
        var response = await _httpClient.DeleteAsync($"/v3/pdf/{Uri.EscapeDataString(pdfId)}", ct);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("[Mathpix] PDF {PdfId} deleted at Mathpix", pdfId);
            return true;
        }
        _logger.LogWarning("[Mathpix] Deleting PDF {PdfId} at Mathpix answered HTTP {Status}", pdfId, (int)response.StatusCode);
        return false;
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
