namespace Lilia.Import.Models;

/// <summary>
/// Result of parsing human-readable LML text into block payloads.
/// </summary>
public sealed class LmlTextParseResult
{
    public string? Title { get; init; }
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
