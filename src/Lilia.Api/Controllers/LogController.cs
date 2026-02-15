using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/logs")]
[Authorize]
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
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? "anonymous";

        var now = DateTime.UtcNow;
        var key = userId;

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

            _logger.Log(level, "CLIENT [{Source}] {Message} {@Data}",
                entry.Source ?? "editor", entry.Message, entry.Data);
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
