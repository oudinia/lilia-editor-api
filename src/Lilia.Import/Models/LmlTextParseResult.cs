namespace Lilia.Import.Models;

/// <summary>
/// Result of parsing human-readable LML text into block payloads.
/// </summary>
public sealed class LmlTextParseResult
{
    public string? Title { get; init; }
    /// <summary>The title came from the first level-1 heading (or a CV name), not from a
    /// <c>title:</c> line or <c>@title</c>: good for naming a new import, not for renaming a document.</summary>
    public bool TitleFromHeading { get; init; }
    public IReadOnlyList<LmlParsedBlock> Blocks { get; init; } = Array.Empty<LmlParsedBlock>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A single parsed block. <see cref="Content"/> is a JSON-serializable
/// anonymous/DTO object matching the editor block content shape.
/// </summary>
public sealed class LmlParsedBlock
{
    public required string Type { get; init; }
    public required object Content { get; init; }
    public int Depth { get; init; }
}
