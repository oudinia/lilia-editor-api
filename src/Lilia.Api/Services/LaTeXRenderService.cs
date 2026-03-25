using System.Text;
using System.Text.Json;

namespace Lilia.Api.Services;

public interface ILaTeXRenderService
{
    Task<byte[]> RenderToPdfAsync(string latex, int timeout = 30);
    Task<byte[]> RenderToPngAsync(string latex, int dpi = 150, int timeout = 30);
    Task<byte[]> RenderBlockToPngAsync(string latexFragment, string? preamble = null, int dpi = 150);
    Task<(bool Valid, string? Error, string[] Warnings)> ValidateAsync(string latex);
}

public class LaTeXRenderServiceOptions
{
    public string BaseUrl { get; set; } = "http://latex:8001";
    public int TimeoutSeconds { get; set; } = 60;
}

public class LaTeXRenderService : ILaTeXRenderService
{
    private readonly HttpClient _http;
    private readonly ILogger<LaTeXRenderService> _logger;

    public LaTeXRenderService(HttpClient http, ILogger<LaTeXRenderService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<byte[]> RenderToPdfAsync(string latex, int timeout = 30)
    {
        var body = JsonSerializer.Serialize(new { latex, timeout });
        var response = await _http.PostAsync("/render/pdf",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("LaTeX PDF render failed: {StatusCode} {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"LaTeX compilation failed: {error}");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]> RenderToPngAsync(string latex, int dpi = 150, int timeout = 30)
    {
        var body = JsonSerializer.Serialize(new { latex, dpi, timeout });
        var response = await _http.PostAsync("/render/png",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"LaTeX PNG render failed: {error}");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]> RenderBlockToPngAsync(string latexFragment, string? preamble = null, int dpi = 150)
    {
        var body = JsonSerializer.Serialize(new { latex = latexFragment, preamble = preamble ?? "", dpi, timeout = 15 });
        var response = await _http.PostAsync("/render/block",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"LaTeX block render failed: {error}");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<(bool Valid, string? Error, string[] Warnings)> ValidateAsync(string latex)
    {
        var body = JsonSerializer.Serialize(new { latex, timeout = 15 });
        var response = await _http.PostAsync("/validate",
            new StringContent(body, Encoding.UTF8, "application/json"));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var valid = root.GetProperty("valid").GetBoolean();
        var error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
        var warnings = root.TryGetProperty("warnings", out var warnProp)
            ? warnProp.EnumerateArray().Select(w => w.GetString() ?? "").ToArray()
            : Array.Empty<string>();

        return (valid, error, warnings);
    }
}
