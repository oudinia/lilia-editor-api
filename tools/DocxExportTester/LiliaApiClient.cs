using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocxExportTester;

/// <summary>
/// Minimal Lilia API client for the export test tool.
/// </summary>
public class LiliaApiClient(string baseUrl, string devUserId)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private HttpRequestMessage Req(HttpMethod method, string path)
    {
        var msg = new HttpRequestMessage(method, path);
        msg.Headers.Add("X-Development-User-Id", devUserId);
        return msg;
    }

    // ── Create document ───────────────────────────────────────────────────────

    public async Task<Guid> CreateDocumentAsync(string title)
    {
        var body = new { Title = title, Language = "en" };
        var req = Req(HttpMethod.Post, "api/documents");
        req.Content = JsonContent.Create(body, options: _json);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception($"CreateDocument failed [{resp.StatusCode}]: {text}");
        var doc = JsonSerializer.Deserialize<JsonElement>(text, _json);
        return doc.GetProperty("id").GetGuid();
    }

    // ── Add blocks ────────────────────────────────────────────────────────────

    public async Task AddBlockAsync(Guid docId, string type, object content, int sortOrder)
    {
        var body = new { Type = type, Content = content, SortOrder = sortOrder };
        var req = Req(HttpMethod.Post, $"api/documents/{docId}/blocks");
        req.Content = JsonContent.Create(body, options: _json);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            throw new Exception($"AddBlock({type}) failed [{resp.StatusCode}]: {text}");
        }
    }

    // ── Export to DOCX ────────────────────────────────────────────────────────

    public async Task<byte[]> ExportDocxAsync(Guid docId)
    {
        var req = Req(HttpMethod.Get, $"api/documents/{docId}/export/docx");
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new Exception($"ExportDocx failed [{resp.StatusCode}]: {err}");
        }
        return await resp.Content.ReadAsByteArrayAsync();
    }

    // ── Export to LaTeX ───────────────────────────────────────────────────────

    public async Task<byte[]> ExportLatexAsync(Guid docId)
    {
        var req = Req(HttpMethod.Get, $"api/documents/{docId}/export/latex");
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new Exception($"ExportLatex failed [{resp.StatusCode}]: {err}");
        }
        return await resp.Content.ReadAsByteArrayAsync();
    }

    // ── Delete document ───────────────────────────────────────────────────────

    public async Task DeleteDocumentAsync(Guid docId)
    {
        var req = Req(HttpMethod.Delete, $"api/documents/{docId}");
        await _http.SendAsync(req);
    }

    // ── Import DOCX (round-trip test) ─────────────────────────────────────────

    public async Task<Guid?> ImportDocxAsync(byte[] docxBytes, string title)
    {
        var body = new
        {
            Content  = Convert.ToBase64String(docxBytes),
            Format   = "DOCX",
            Filename = "roundtrip-test.docx",
            Title    = title,
            SkipReview = true,
            Options  = new { PreserveFormatting = true, ImportImages = true, AutoDetectEquations = true }
        };
        var req = Req(HttpMethod.Post, "api/lilia/jobs/import");
        req.Content = JsonContent.Create(body, options: _json);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) return null;
        var result = JsonSerializer.Deserialize<JsonElement>(text, _json);
        if (result.TryGetProperty("document", out var doc) &&
            doc.ValueKind != JsonValueKind.Null &&
            doc.TryGetProperty("id", out var idProp))
            return idProp.GetGuid();
        return null;
    }

    // ── Get blocks ────────────────────────────────────────────────────────────

    public async Task<List<BlockInfo>> GetBlocksAsync(Guid docId)
    {
        var req = Req(HttpMethod.Get, $"api/documents/{docId}/Blocks");
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception($"GetBlocks failed [{resp.StatusCode}]: {text}");
        return JsonSerializer.Deserialize<List<BlockInfo>>(text, _json) ?? [];
    }
}

public record BlockInfo(
    [property: JsonPropertyName("id")]        Guid         Id,
    [property: JsonPropertyName("type")]      string       Type,
    [property: JsonPropertyName("content")]   JsonElement  Content,
    [property: JsonPropertyName("sortOrder")] int          SortOrder
);
