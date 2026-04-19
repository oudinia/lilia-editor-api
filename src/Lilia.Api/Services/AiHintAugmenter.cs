using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IAiHintAugmenter
{
    /// <summary>
    /// Run an AI pass over a document's blocks to find structural issues
    /// the rule-based pipeline missed. Persists new findings with
    /// source="ai" alongside existing rule-generated ones. Idempotent:
    /// re-running replaces the document's pending AI findings.
    /// </summary>
    Task<AiAugmentResult> AugmentAsync(Guid documentId, string userId, CancellationToken ct = default);
}

public record AiAugmentResult(
    int FindingsAdded,
    int FindingsReplaced,
    string Status,     // success | disabled | error | rate_limited | redacted_refused
    string? ErrorMessage,
    Guid? AiRequestId);

/// <summary>
/// AI-assisted structural findings. Does NOT replace the rule pipeline —
/// it sits alongside it, surfacing suggestions that would require real
/// reading comprehension (e.g. "this paragraph summarises the preceding
/// three — could be an abstract", "this list has inconsistent tense").
///
/// The prompt is intentionally narrow: we ask Claude to return a JSON
/// array of typed findings matching our closed vocabulary, and we drop
/// anything that doesn't parse cleanly.
/// </summary>
public class AiHintAugmenter : IAiHintAugmenter
{
    private const string SystemPrompt = @"
You are a document-structure analyst helping clean up imports of academic
and business documents. You will be given an ordered list of blocks from
a single document. Your job: spot STRUCTURAL problems that simple
regex/formatting rules miss — not grammar, spelling, or style.

Return a JSON array. Each item must be exactly:
{
  ""blockId"": string | null,     // block this refers to, null for session-wide
  ""kind"": string,                 // one of: paragraph_as_heading, personal_info,
                                    // header_table_unpack, spurious_toc,
                                    // cv_section, paragraph_is_cv_section,
                                    // cv_class_suggestion, cv_list_style,
                                    // fragmented_list, layout_table,
                                    // missing_figure_caption, orphan_subheading_chain
  ""severity"": ""hint"" | ""warning"" | ""critical"",
  ""title"": string,                // <60 chars, human-readable
  ""detail"": string,               // <300 chars, explains why + next step
  ""suggestedAction"": string       // <120 chars, imperative
}

Rules:
- Only return findings you are confident about. Empty array is fine.
- No commentary outside the JSON.
- At most 10 findings per request.
- Prefer specifics over hedges.
";

    private readonly LiliaDbContext _context;
    private readonly IAiOrchestrator _orchestrator;
    private readonly BulkInsertHelper _bulk;
    private readonly ILogger<AiHintAugmenter> _logger;

    public AiHintAugmenter(
        LiliaDbContext context,
        IAiOrchestrator orchestrator,
        BulkInsertHelper bulk,
        ILogger<AiHintAugmenter> logger)
    {
        _context = context;
        _orchestrator = orchestrator;
        _bulk = bulk;
        _logger = logger;
    }

    public async Task<AiAugmentResult> AugmentAsync(Guid documentId, string userId, CancellationToken ct = default)
    {
        // Pull block summaries — truncate long text to keep the prompt small.
        // One block per line: [id] type | first-200-chars.
        var blocks = await _context.Blocks
            .AsNoTracking()
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .Select(b => new { b.Id, b.Type, b.Content })
            .ToListAsync(ct);

        if (blocks.Count == 0)
        {
            return new AiAugmentResult(0, 0, "success", null, null);
        }

        var prompt = BuildUserPrompt(blocks);

        var result = await _orchestrator.RunAsync(new AiOrchestratorRequest(
            UserId: userId,
            DocumentId: documentId,
            BlockId: null,
            Purpose: "review_finding",
            UserPrompt: prompt,
            SystemPrompt: SystemPrompt,
            Model: "claude-opus-4-7",
            MaxTokens: 2048,
            Temperature: 0.1), ct);

        if (result.Status != "success" || string.IsNullOrWhiteSpace(result.Text))
        {
            return new AiAugmentResult(0, 0, result.Status, result.ErrorMessage, result.AiRequestId);
        }

        var parsed = ParseFindings(result.Text, documentId);
        if (parsed.Count == 0)
        {
            _logger.LogInformation("[AiHintAugmenter] Claude returned no findings for document {DocumentId}", documentId);
            await ReplacePendingAsync(documentId, ct);
            return new AiAugmentResult(0, 0, "success", null, result.AiRequestId);
        }

        // Idempotency: remove any existing pending AI findings for this
        // document before writing the new batch. Rule findings are untouched.
        var replaced = await ReplacePendingAsync(documentId, ct);

        await _bulk.BulkInsertStructuralFindingsAsync(parsed, ct);

        return new AiAugmentResult(parsed.Count, replaced, "success", null, result.AiRequestId);
    }

    private static string BuildUserPrompt(IReadOnlyList<dynamic> blocks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyse these document blocks for structural problems. Blocks:");
        sb.AppendLine();
        foreach (var b in blocks)
        {
            var text = ExtractText(b.Content);
            if (text.Length > 200) text = text[..200] + "…";
            sb.Append('[').Append(b.Id).Append("] ").Append(b.Type).Append(" | ").AppendLine(text);
        }
        sb.AppendLine();
        sb.AppendLine("Return findings as the JSON array specified in the system prompt.");
        return sb.ToString();
    }

    private static string ExtractText(JsonDocument content)
    {
        try
        {
            if (content.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return (t.GetString() ?? "").Replace('\n', ' ').Trim();
        }
        catch { /* ignore */ }
        return "";
    }

    private List<ImportStructuralFinding> ParseFindings(string aiText, Guid documentId)
    {
        // Claude often wraps JSON in prose or markdown fences despite the
        // instructions. Extract the first [...] block defensively.
        var match = Regex.Match(aiText, @"\[[\s\S]*\]");
        if (!match.Success) return new();

        var validKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "paragraph_as_heading","personal_info","header_table_unpack","spurious_toc",
            "cv_section","paragraph_is_cv_section","cv_class_suggestion","cv_list_style",
            "fragmented_list","layout_table","missing_figure_caption","orphan_subheading_chain",
        };
        var validSev = new HashSet<string>(StringComparer.Ordinal) { "hint", "warning", "critical" };

        try
        {
            using var doc = JsonDocument.Parse(match.Value);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();

            var list = new List<ImportStructuralFinding>();
            foreach (var el in doc.RootElement.EnumerateArray().Take(10))
            {
                var kind = el.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                var severity = el.TryGetProperty("severity", out var s) ? s.GetString() ?? "hint" : "hint";
                var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var detail = el.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
                var action = el.TryGetProperty("suggestedAction", out var a) ? a.GetString() ?? "" : "";
                var blockId = el.TryGetProperty("blockId", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;

                if (!validKinds.Contains(kind)) continue;
                if (!validSev.Contains(severity)) severity = "hint";
                if (string.IsNullOrWhiteSpace(title)) continue;

                list.Add(new ImportStructuralFinding
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    SessionId = null,
                    BlockId = string.IsNullOrWhiteSpace(blockId) ? null : blockId,
                    Kind = kind,
                    Severity = severity,
                    Title = Truncate(title, 500),
                    Detail = Truncate(detail, 1000),
                    SuggestedAction = Truncate(action, 300),
                    ActionKind = "open_edit_modal",      // AI findings are advisory — user reviews in modal
                    Status = "pending",
                    Source = "ai",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            return list;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[AiHintAugmenter] Failed to parse Claude JSON: {Preview}", aiText.Length > 200 ? aiText[..200] : aiText);
            return new();
        }
    }

    private async Task<int> ReplacePendingAsync(Guid documentId, CancellationToken ct)
    {
        return await _context.ImportStructuralFindings
            .Where(f => f.DocumentId == documentId && f.Source == "ai" && f.Status == "pending")
            .ExecuteDeleteAsync(ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
