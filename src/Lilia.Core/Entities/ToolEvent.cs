using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Append-only event for the standalone tools — serves BOTH the anonymous quota
/// ("uses today" = count of <c>use</c> events for an anon/user + tool + day) and
/// the acquisition funnel (<c>view → use → result → signup → pay</c>). DB-backed so
/// it's restart- and multi-instance-correct (per the strategy's architecture review).
/// </summary>
public class ToolEvent
{
    public Guid Id { get; set; }

    public string ToolSlug { get; set; } = string.Empty;

    /// <summary>Set when the caller is authenticated; else null (anonymous).</summary>
    public string? UserId { get; set; }

    /// <summary>Soft anonymous identity (signed cookie / hashed IP) for quota.</summary>
    public string AnonId { get; set; } = string.Empty;

    /// <summary>view | use | result | signup | pay.</summary>
    public string Event { get; set; } = string.Empty;

    public JsonDocument? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }
}
