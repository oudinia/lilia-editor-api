using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocxTestGenerator;

/// <summary>
/// Minimal HTTP client for the Lilia API.
/// Handles authentication, import (with skipReview), and export.
/// </summary>
public class LiliaApiClient(string baseUrl, string devUserId = "kp_6969289c438e4b20b46e13f18c7933f2")
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Inject dev-bypass header (works locally; won't reach prod without a Kinde JWT)
    private HttpRequestMessage Req(HttpMethod method, string path)
    {
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Add("X-Development-User-Id", devUserId);
        return msg;
    }

    // -------------------------------------------------------------------------
    //  IMPORT
    // -------------------------------------------------------------------------
    public async Task<ImportResult> ImportDocxAsync(byte[] docxBytes, string filename, string title)
    {
        var body = new
        {
            Content  = Convert.ToBase64String(docxBytes),
            Format   = "DOCX",
            Filename = filename,
            Title    = title,
            SkipReview = true,   // ← auto-finalize
            Options  = new
            {
                PreserveFormatting  = true,
                ImportImages        = true,
                ImportBibliography  = true,
                AutoDetectEquations = true,
                SplitByHeadings     = true,
            }
        };

        var req = Req(HttpMethod.Post, "api/lilia/jobs/import");
        req.Content = JsonContent.Create(body, options: _json);

        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Import failed [{resp.StatusCode}]: {text}");

        var result = JsonSerializer.Deserialize<ImportResult>(text, _json)
            ?? throw new Exception("Empty import response");

        return result;
    }

    // -------------------------------------------------------------------------
    //  GET DOCUMENT BLOCKS
    // -------------------------------------------------------------------------
    public async Task<List<BlockDto>> GetBlocksAsync(Guid documentId)
    {
        var req = Req(HttpMethod.Get, $"api/documents/{documentId}/Blocks");
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GetBlocks failed [{resp.StatusCode}]: {text}");

        return JsonSerializer.Deserialize<List<BlockDto>>(text, _json) ?? [];
    }

    // -------------------------------------------------------------------------
    //  EXPORT TO LATEX
    // -------------------------------------------------------------------------
    public async Task<byte[]> ExportLatexZipAsync(Guid documentId)
    {
        var req = Req(HttpMethod.Get, $"api/documents/{documentId}/export/latex");
        var resp = await _http.SendAsync(req);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new Exception($"Export failed [{resp.StatusCode}]: {err}");
        }

        return await resp.Content.ReadAsByteArrayAsync();
    }

    // -------------------------------------------------------------------------
    //  FINALIZE REVIEW SESSION (used when skipReview unavailable/fails)
    // -------------------------------------------------------------------------
    public async Task<FinalizeResult> FinalizeSessionAsync(Guid sessionId, string? title = null)
    {
        var body = new { DocumentTitle = title, Force = true };
        var req = Req(HttpMethod.Post, $"api/lilia/import-review/sessions/{sessionId}/finalize");
        req.Content = JsonContent.Create(body, options: _json);

        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Finalize failed [{resp.StatusCode}]: {text}");

        return JsonSerializer.Deserialize<FinalizeResult>(text, _json)
            ?? throw new Exception("Empty finalize response");
    }
}

// ---------------------------------------------------------------------------
//  DTOs
// ---------------------------------------------------------------------------
public record ImportResult(
    [property: JsonPropertyName("job")]           JobInfo?    Job,
    [property: JsonPropertyName("document")]      DocInfo?    Document,
    [property: JsonPropertyName("reviewSessionId")] Guid?     ReviewSessionId
);

public record JobInfo(
    [property: JsonPropertyName("id")]     Guid   Id,
    [property: JsonPropertyName("status")] string Status
);

public record DocInfo(
    [property: JsonPropertyName("id")]    Guid   Id,
    [property: JsonPropertyName("title")] string Title
);

public record BlockDto(
    [property: JsonPropertyName("id")]        Guid                        Id,
    [property: JsonPropertyName("type")]      string                      Type,
    [property: JsonPropertyName("content")]   JsonElement                 Content,
    [property: JsonPropertyName("sortOrder")] int                         SortOrder,
    [property: JsonPropertyName("depth")]     int                         Depth
);

public record FinalizeResult(
    [property: JsonPropertyName("document")]   FinalizedDoc? Document,
    [property: JsonPropertyName("statistics")] FinalizeStats? Statistics
);

public record FinalizedDoc(
    [property: JsonPropertyName("id")]    Guid   Id,
    [property: JsonPropertyName("title")] string Title
);

public record FinalizeStats(
    [property: JsonPropertyName("importedBlocks")] int Imported,
    [property: JsonPropertyName("skippedBlocks")]  int Skipped
);
