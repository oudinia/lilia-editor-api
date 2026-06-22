using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lilia.Api.Models.AiArchitect;

// ──────────────────────────────────────────────────────────────────────────
// Request DTOs
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/ai/architect. The conversation is entirely
/// client-held: the editor sends the full message history each turn, so the
/// endpoint stays stateless (restart / multi-instance safe).
/// </summary>
public record AiArchitectRequest(
    string DocumentId,
    List<AiArchitectMessage> Messages,
    string? Model = null   // optional override, validated against the catalog + tier
);

/// <summary>A single turn in the architect conversation.</summary>
public record AiArchitectMessage(
    string Role,      // "user" | "assistant"
    string Content
);

// ──────────────────────────────────────────────────────────────────────────
// Response DTOs
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// 200 response. The endpoint never mutates the document — it returns a
/// natural-language reply plus a list of proposed block operations that the
/// editor applies only after the user accepts them.
/// </summary>
public record AiArchitectResponse(
    string Reply,
    List<BlockOp> Operations,
    AiArchitectUsage Usage,
    AiArchitectBalance? Balance
);

/// <summary>Token + cost accounting for a single architect call.</summary>
public record AiArchitectUsage(
    int InputTokens,
    int OutputTokens,
    decimal CostUsd
);

/// <summary>Remaining AI budget after the call (best-effort; null if unknown).</summary>
public record AiArchitectBalance(
    decimal RemainingUsd
);

/// <summary>
/// 403 body returned when the endpoint is gated (no key / disabled / not
/// entitled / over budget). Shape matches the editor's lock affordance.
/// </summary>
public record AiArchitectLocked(
    bool Locked,
    string Reason,    // "no-key" | "disabled" | "not-entitled" | "over-budget"
    string Message
);

// ──────────────────────────────────────────────────────────────────────────
// Block operations — the structured output the model must emit
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single proposed change to the document's block list. The editor is the
/// source of truth and applies accepted ops; the API only proposes them.
/// </summary>
public record BlockOp(
    string Op,             // "add" | "edit" | "move" | "remove"
    string? Id,            // target block id (edit/move/remove)
    string? AfterId,       // insert/move target: place after this block id (null = start)
    BlockPayload? Block    // payload for add/edit
);

/// <summary>The typed-block payload for an add/edit op.</summary>
public record BlockPayload(
    string Type,
    [property: JsonConverter(typeof(RawJsonElementConverter))] JsonElement Content
);

/// <summary>
/// Passthrough converter so <c>Content</c> round-trips as raw JSON regardless
/// of shape (the agent emits one of several typed content shapes).
/// </summary>
public sealed class RawJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonElement.ParseValue(ref reader);

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
        => value.WriteTo(writer);
}
