using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Lilia.Api.E2E.Tests.LatexCoverage;

/// <summary>
/// The product's own .tex paths, called the way the web app calls them:
/// <list type="bullet">
/// <item><c>POST /api/lilia/imports/latex?autoFinalize=true</c> (multipart <c>file</c>) →
///   <c>{ sessionId, jobId }</c>; then poll <c>GET /api/lilia/jobs/{jobId}</c> until
///   COMPLETED/FAILED — a COMPLETED job carries <c>documentId</c> (ImportTab /
///   LaTeXImportDialog + afterTexUpload).</item>
/// <item><c>GET /api/documents/{id}/export/latex?mode=tex</c> — the single-file
///   main.tex (the default zip's main.tex is built by the same renderer).</item>
/// <item><c>GET /api/documents/{id}/preview/typst?format=pdf</c> — the Typst path the
///   editor preview and the PDF export use (TypstExportService, compiled server-side).
///   Not <c>GET …/export/typst</c>: that endpoint is a separate, older renderer
///   (TypstRenderService) the web app never calls, and it has no LaTeX→Typst math
///   conversion at all.</item>
/// </list>
/// </summary>
public sealed class LatexCoverageApi
{
    private static readonly string[] DoneStatuses = ["COMPLETED", "FAILED", "CANCELLED"];
    private readonly HttpClient _client;

    public LatexCoverageApi(HttpClient client) => _client = client;

    public sealed record ImportResult(Guid? DocumentId, Guid? SessionId, string Status, string? Error);

    public async Task<ImportResult> ImportTexAsync(string tex, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(tex));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-tex");
        form.Add(file, "file", fileName);

        var upload = await _client.PostAsync("/api/lilia/imports/latex?autoFinalize=true", form);
        if (!upload.IsSuccessStatusCode)
            return new ImportResult(null, null, $"HTTP {(int)upload.StatusCode}", await upload.Content.ReadAsStringAsync());

        var body = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = body.GetProperty("jobId").GetString();
        Guid? sessionId = Guid.TryParse(body.GetProperty("sessionId").GetString(), out var s) ? s : null;

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var poll = await _client.GetAsync($"/api/lilia/jobs/{jobId}");
            if (poll.IsSuccessStatusCode)
            {
                var job = await poll.Content.ReadFromJsonAsync<JsonElement>();
                var status = (job.TryGetProperty("status", out var st) ? st.GetString() : "")?.ToUpperInvariant() ?? "";
                if (DoneStatuses.Contains(status))
                {
                    Guid? docId = job.TryGetProperty("documentId", out var d) && d.ValueKind == JsonValueKind.String
                        && Guid.TryParse(d.GetString(), out var g) ? g : null;
                    var error = job.TryGetProperty("errorMessage", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    return new ImportResult(docId, sessionId, status, error);
                }
            }
            await Task.Delay(250);
        }
        return new ImportResult(null, sessionId, "TIMEOUT", $"job {jobId} did not finish within 90s");
    }

    public async Task<JsonElement> GetDocumentAsync(Guid id)
    {
        var res = await _client.GetAsync($"/api/documents/{id}");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<(HttpStatusCode Status, string Body)> ExportLatexAsync(Guid id)
    {
        var res = await _client.GetAsync($"/api/documents/{id}/export/latex?mode=tex");
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    /// <summary>The server-side Typst compile: (status, PDF bytes or error body).</summary>
    public async Task<(HttpStatusCode Status, byte[] Body)> PreviewTypstPdfAsync(Guid id)
    {
        var res = await _client.GetAsync($"/api/documents/{id}/preview/typst?format=pdf");
        return (res.StatusCode, await res.Content.ReadAsByteArrayAsync());
    }

    /// <summary>Soft delete, then purge from trash, then drop the review session. Best effort.</summary>
    public async Task CleanupAsync(ImportResult? import)
    {
        if (import is null) return;
        try
        {
            if (import.DocumentId is { } doc)
            {
                await _client.DeleteAsync($"/api/documents/{doc}");
                await _client.DeleteAsync($"/api/documents/{doc}/permanent");
            }
            if (import.SessionId is { } session)
                await _client.DeleteAsync($"/api/lilia/import-review/sessions/{session}?permanent=true");
        }
        catch
        {
            // Best-effort cleanup — a leftover row in the throwaway e2e DB is not a test failure.
        }
    }
}
