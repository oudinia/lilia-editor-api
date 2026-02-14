using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/logs")]
[Authorize]
public class LogController : ControllerBase
{
    private readonly ILogger<LogController> _logger;

    public LogController(ILogger<LogController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult IngestBatch([FromBody] ClientLogBatch batch)
    {
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
