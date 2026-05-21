using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/logs")]
[AllowAnonymous]
public class LogController : ControllerBase
{
    private readonly ILogger<LogController> _logger;

    private static readonly ConcurrentDictionary<string, (int Count, DateTime Window)> _rateLimits = new();
    private const int MaxRequestsPerMinute = 30;

    public LogController(ILogger<LogController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult IngestBatch([FromBody] ClientLogBatch batch)
    {
        // Rate-limit key: the authenticated user when present, else the
        // client IP. The unauthenticated client logger sends no bearer
        // token, so keying on a literal "anonymous" collapsed every
        // client in the world into one shared 30 req/min bucket — the
        // endpoint started 429-ing within seconds under any real load.
        var key = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        var now = DateTime.UtcNow;

        var (count, window) = _rateLimits.GetOrAdd(key, _ => (0, now));
        if (now - window > TimeSpan.FromMinutes(1))
        {
            _rateLimits[key] = (1, now);
        }
        else if (count >= MaxRequestsPerMinute)
        {
            return StatusCode(429, new { message = "Rate limit exceeded. Max 30 requests per minute." });
        }
        else
        {
            _rateLimits[key] = (count + 1, window);
        }

        if (batch.Entries == null || batch.Entries.Count == 0)
            return Ok();

        foreach (var entry in batch.Entries.Take(50))
        {
            var level = entry.Level?.ToLowerInvariant() switch
            {
                "error" => LogLevel.Error,
                "warn" => LogLevel.Warning,
                "info" => LogLevel.Information,
                _ => LogLevel.Debug
            };

            // entry.Data deserializes as JsonElement under the hood;
            // Serilog's {@…} destructurer drops it to {"ValueKind":"…"}.
            // Render as raw JSON so React stack traces survive intact.
            var dataJson = entry.Data is null
                ? null
                : JsonSerializer.Serialize(entry.Data);
            _logger.Log(level, "CLIENT [{Source}] {Message} {Data}",
                entry.Source ?? "editor", entry.Message, dataJson);
        }

        return Ok();
    }
}

public class ClientLogBatch
{
    public List<ClientLogEntry> Entries { get; set; } = [];
}

public class ClientLogEntry
{
    public string? Level { get; set; }
    public string? Message { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public string? Timestamp { get; set; }
}
