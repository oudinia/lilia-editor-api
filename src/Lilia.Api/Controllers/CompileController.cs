using Lilia.Engines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Where a compile stands while the PDF request waits: "3rd in line", "compiling". The preview sends a
/// ticket of its own making with its PDF request (<see cref="TicketHeader"/>) and asks here with it.
/// A ticket is the asker's own: the same text from another account names another compile.
/// </summary>
[ApiController]
[Route("api/compile")]
[Authorize]
public class CompileController : ControllerBase
{
    public const string TicketHeader = "X-Compile-Ticket";
    /// <summary>How long a busy answer asks the preview to wait before asking again.</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>The ticket as kept: the account and the browser's own text (at most 64 characters), or null.</summary>
    public static string? TicketOf(string? userId, string? ticket) =>
        string.IsNullOrEmpty(userId) || string.IsNullOrWhiteSpace(ticket) || ticket.Length > 64
            ? null
            : userId + ":" + ticket;

    /// <summary>
    /// <c>place</c>: 1 = next in line, 0 = compiling, null = not waiting (finished, or not started yet).
    /// <c>ahead</c>, <c>compiling</c> and <c>slots</c> describe the whole line, without saying whose.
    /// </summary>
    [HttpGet("place")]
    public IActionResult Place([FromQuery] string ticket)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var kept = TicketOf(userId, ticket);
        if (kept is null) return BadRequest(new { error = "invalid_ticket" });
        return Ok(new
        {
            place = CompileSlots.PlaceOf(kept),
            waiting = CompileSlots.WaitingCount,
            compiling = CompileSlots.RunningCount,
            slots = CompileSlots.Slots,
        });
    }
}
