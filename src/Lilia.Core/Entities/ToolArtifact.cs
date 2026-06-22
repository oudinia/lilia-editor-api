using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// A recorded run of a standalone tool — the input and the produced output.
///
/// "Ephemeral" is about the user (no account/saved history); we still persist
/// every artifact (cheaply, anonymously) so we can understand behaviour and
/// patterns — what people convert, where they fail — and it lets the work stay
/// server-side instead of in heavy client JS. Prunable: a TTL job drops old
/// rows. Distinct from <see cref="ToolEvent"/> (funnel/quota); this carries the
/// heavier payload and is the seed of the future personal library.
/// </summary>
public class ToolArtifact
{
    public Guid Id { get; set; }

    public string ToolSlug { get; set; } = string.Empty;

    public string? UserId { get; set; }
    public string AnonId { get; set; } = string.Empty;

    /// <summary>The request input (doi/grid), or {filename,bytes} for file tools.</summary>
    public JsonDocument? Input { get; set; }

    /// <summary>Produced output (capped); large outputs are truncated.</summary>
    public string? Output { get; set; }
    public string OutputFormat { get; set; } = string.Empty;
    public int OutputBytes { get; set; }
    public bool Truncated { get; set; }

    public DateTime CreatedAt { get; set; }
}
