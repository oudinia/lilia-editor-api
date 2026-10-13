using System.Collections.Concurrent;
using System.Text.Json;

namespace Lilia.Api.Mcp;

/// <summary>
/// check_document's last verdict per document, for export_document's lastCheck and the pdf rule
/// (MCP-72, MCP-82, MCP-88). Kept in memory and in one small file per document, so a restart doesn't
/// forget it: it was in IMemoryCache, and after a restart export_document said "Never checked"
/// (MCP-112, run 3b). Tied to the document's version: an edit makes it stale.
/// </summary>
public sealed class CheckMemoStore
{
    public sealed record Memo(DateTime DocumentUpdatedAt, DateTime CheckedAt, bool Compiles, bool PrintsCorrectly, string? Error = null);

    private readonly ConcurrentDictionary<Guid, Memo> _memos = new();
    private readonly string _dir;
    private readonly ILogger<CheckMemoStore> _logger;

    public CheckMemoStore(IWebHostEnvironment env, IConfiguration config, ILogger<CheckMemoStore> logger)
    {
        _dir = config["Mcp:CheckMemoDir"] is { Length: > 0 } dir ? dir : Path.Combine(env.ContentRootPath, "App_Data", "check-memos");
        _logger = logger;
    }

    public void Set(Guid documentId, Memo memo)
    {
        _memos[documentId] = memo;
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(PathFor(documentId), JsonSerializer.Serialize(memo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[Mcp] could not keep the check of {DocId} on disk", documentId);
        }
    }

    public Memo? Get(Guid documentId)
    {
        if (_memos.TryGetValue(documentId, out var memo)) return memo;
        try
        {
            var path = PathFor(documentId);
            if (!File.Exists(path)) return null;
            memo = JsonSerializer.Deserialize<Memo>(File.ReadAllText(path));
            if (memo is not null) _memos[documentId] = memo;
            return memo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The verdict for this version of the document, or null when there is none.</summary>
    public Memo? Current(Guid documentId, DateTime documentUpdatedAt) =>
        Get(documentId) is { } m && m.DocumentUpdatedAt == documentUpdatedAt ? m : null;

    private string PathFor(Guid id) => Path.Combine(_dir, id.ToString("N") + ".json");
}
