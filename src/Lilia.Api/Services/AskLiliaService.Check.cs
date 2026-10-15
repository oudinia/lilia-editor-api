using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>
/// Ask Lilia checks its own fixes (15 Oct). It could edit a document but not compile it; only the
/// connector had check_document. Now the tool loop has the same check (<see cref="IDocumentCheckService"/>)
/// and a bounded repair loop: edit, check, repair at most <see cref="MaxRepairAttempts"/> times, then stop
/// and say what still fails. The turn's answer says whether the document compiles.
/// </summary>
public sealed partial class AskLiliaService
{
    internal const int MaxRepairAttempts = 3;
    internal const string CompileDoctorSkill = "lilia-compile-doctor";

    /// <summary>The tools that change the document: refused once the repair attempts are used up.</summary>
    internal static readonly HashSet<string> WriteTools = new(StringComparer.Ordinal)
    {
        "apply_lml", "add_block", "edit_block", "remove_block", "reorder_blocks", "set_title",
        "set_document_kind", "set_document_settings", "import_latex",
    };

    /// <summary>
    /// One turn's compile checks. An attempt is a check that follows an edit; a check before any
    /// edit is the diagnosis and is free. <see cref="Compiles"/> counts real compiles (a version
    /// already checked is not compiled again), which is what the turn is charged for.
    /// </summary>
    internal sealed class CompileRepair(List<string> changed)
    {
        public int MetaWrites;
        public int Attempts;
        public int Compiles;
        public int WritesAtLastCheck;
        public DocumentCheck? Last;
        public int Writes => changed.Count + MetaWrites;
        /// <summary>The last check saw the document as it is now.</summary>
        public bool LastIsCurrent => Last is not null && WritesAtLastCheck == Writes;
        /// <summary>Three edits checked and it still does not compile: no more edits this turn.</summary>
        public bool Exhausted => Attempts >= MaxRepairAttempts && Last?.Compiles != true;
    }

    internal sealed record ToolLoopResult(
        ChatResponse Response, int InputTokens, int OutputTokens, int ToolCalls, int WebSearches,
        bool PartialApply, List<WebCitation> Citations);

    /// <summary>Compile the document as saved now, and count it against the turn.</summary>
    private async Task<DocumentCheck> RunCheckAsync(LiveDocument live, Guid docGuid, string userId, CompileRepair repair, CancellationToken ct)
    {
        // The saved document, not the turn's copy: its version (for the memo) and engine are current.
        Lilia.Core.DTOs.DocumentDto? saved = null;
        try { saved = await _documentService.GetDocumentAsync(docGuid, userId); }
        catch (Exception ex) { _logger.LogWarning(ex, "[AskLilia] could not reload {DocId} for check_document", docGuid); }
        var check = await _checker!.CheckAsync(saved ?? live.Dto, ct);
        if (!check.Cached && check.Compiles is not null) repair.Compiles++;
        if (repair.Writes > repair.WritesAtLastCheck) repair.Attempts++;
        repair.WritesAtLastCheck = repair.Writes;
        repair.Last = check;
        return check;
    }

    private async Task<object> CheckDocumentToolAsync(LiveDocument live, Guid docGuid, string userId, CompileRepair repair, CancellationToken ct)
    {
        if (repair.Exhausted)
            return new { compiles = false, repairAttempts = repair.Attempts, next = RepairLimitNote };
        var check = await RunCheckAsync(live, docGuid, userId, repair, ct);
        var reply = JsonSerializer.SerializeToNode(check.Report) as JsonObject ?? new JsonObject();
        reply["repairAttempts"] = repair.Attempts;
        reply["repairAttemptsLeft"] = Math.Max(0, MaxRepairAttempts - repair.Attempts);
        reply["next"] = check.Compiles == true
            ? repair.Writes > 0
                ? "It compiles. Tell the author what you changed and that the document now compiles."
                : "It compiles. Tell the author so."
            : repair.Exhausted
                ? RepairLimitNote
                : $"It does not compile. Fix the block named in problems with the smallest edit, then call check_document again. {MaxRepairAttempts - repair.Attempts} repair attempt(s) left.";
        return reply;
    }

    /// <summary>What the model is told about check_document, and the repair loop for the compile doctor.</summary>
    internal static string CompileCheckNote(bool editMode, string skillId)
    {
        var note = "COMPILE CHECK — check_document compiles the document as saved and returns compiles, printsCorrectly and problems "
            + "(each error and warning with its blockId and line). Use it, not a guess, to say whether the document compiles.";
        if (skillId != CompileDoctorSkill) return note;
        return note + (editMode
            ? $"""

              REPAIR LOOP (Edit mode) — when the author asks you to fix a compile error or "it won't compile":
              1. call check_document to see the real error and its block (unless the author pasted it and you have the block);
              2. fix that block with the smallest edit (edit_block; get_block first to read it);
              3. call check_document again. If it still fails, repair again: at most {MaxRepairAttempts} edit-and-check attempts in all.
              Then stop. Your answer must say whether the document now compiles. If it still does not, say so plainly and name each
              block that still fails (its id and the error); do not claim a fix you have not seen compile. The author can undo
              your edits (Undo AI changes).
              """
            : """

              Without Edit mode you cannot change the document: call check_document to find the real error and its block, then give
              the corrected snippet, and tell the author to turn on Edit mode if they want you to apply and verify the fix.
              """);
    }

    private const string RepairLimitNote =
        "Repair limit reached: 3 edit-and-check attempts and it still does not compile. Do not edit again. Answer the author now: "
        + "say the document does not compile yet, name each block that still fails (its id, line and error), and what you tried.";

    /// <summary>
    /// The tool-use loop: each round is one model call; tool calls are run and their results fed
    /// back until the model answers in text, or the round cap. Once the repair attempts are used
    /// up, edits are refused and the model must answer (tool choice none).
    /// </summary>
    internal async Task<ToolLoopResult> RunToolLoopAsync(
        List<ChatMessage> messages, ChatOptions options, IList<AITool> tools, bool webSearch, CompileRepair repair,
        string skillId, Guid? documentId, Guid aiRequestId, CancellationToken ct)
    {
        ChatResponse response = null!;
        var inputTokens = 0;
        var outputTokens = 0;
        var toolCalls = 0;
        // Which tools, and how many times each. Only read when the cap is
        // hit, but counted throughout because by then it is too late.
        var toolUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        var webSearches = 0;
        var partialApply = false;
        // Structured citations from web search, accumulated + deduped across rounds.
        var citations = new List<WebCitation>();
        var citationUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var round = 0; round < MaxToolRounds; round++)
        {
            response = await _chatClient.GetResponseAsync(messages, options, ct);
            inputTokens += (int?)response.Usage?.InputTokenCount ?? 0;
            outputTokens += (int?)response.Usage?.OutputTokenCount ?? 0;
            webSearches += ExtractWebSearches(response);
            if (webSearch)
                foreach (var c in WebCitationExtractor.Extract(response))
                    if (citationUrls.Add(c.Url)) citations.Add(c);

            // Carry the assistant turn (incl. any function-call content) into history.
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();
            if (calls.Count == 0) break; // final answer

            // Last allowed round still wants tools → execute them, but we cannot
            // get a follow-up summary round. Flag partial so the client warns.
            if (round == MaxToolRounds - 1)
            {
                partialApply = true;
                // Log WHICH tools burned the rounds, not just how many were
                // left. The cap is nearly always hit the same way — a
                // per-block tool called once per block, where apply_lml
                // would have done the whole document in one round — and
                // without the histogram there is no way to confirm that
                // after the fact. "16 rounds, 3 pending" says nothing.
                var histogram = string.Join(", ", toolUsage
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}×{kv.Value}"));
                _logger.LogWarning(
                    "[AskLilia] tool-round cap ({Max}) hit for skill={Skill} doc={DocId} req={ReqId} " +
                    "with {Calls} pending call(s); tools used: {Histogram}",
                    MaxToolRounds, skillId, documentId, aiRequestId, calls.Count,
                    string.IsNullOrEmpty(histogram) ? "(none)" : histogram);
            }

            foreach (var call in calls)
            {
                toolCalls++;
                var toolName = call.Name ?? "(unnamed)";
                toolUsage[toolName] = toolUsage.GetValueOrDefault(toolName) + 1;
                var toolResult = repair.Exhausted && WriteTools.Contains(toolName)
                    ? (object?)new { error = RepairLimitNote }
                    : await InvokeKbToolAsync(tools, call, ct);
                messages.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, toolResult)]));
            }

            // Attempts used up: the next round is the answer, not another edit.
            if (repair.Exhausted && options.ToolMode is not NoneChatToolMode)
            {
                _logger.LogInformation("[AskLilia] repair limit ({Max}) reached for doc={DocId} req={ReqId}", MaxRepairAttempts, documentId, aiRequestId);
                options.ToolMode = ChatToolMode.None;
            }
        }
        return new ToolLoopResult(response, inputTokens, outputTokens, toolCalls, webSearches, partialApply, citations);
    }

    /// <summary>
    /// What the turn's answer must end with when it edited the document and checked it (or is the
    /// compile doctor and checked): whether the document compiles now, from the check itself, and
    /// the blocks that still fail. Null when no check saw the document as it is now.
    /// </summary>
    internal static string? CompileVerdict(CompileRepair repair, string skillId, string? language)
    {
        if (repair.Last is not { } last || !repair.LastIsCurrent) return null;
        if (repair.Writes == 0 && skillId != CompileDoctorSkill) return null;
        if (last.Compiles is null) return null;
        var lang = KbLanguage.Normalize(language);
        if (last.Compiles == true)
            return lang switch
            {
                "fr" => "✅ **Vérification :** le document compile.",
                "es" => "✅ **Comprobación:** el documento compila.",
                _ => "✅ **Compile check:** the document compiles.",
            };
        var failing = last.Problems.Where(p => p.Severity == "error").Take(5)
            .Select(p => (p.BlockId is null ? lang switch { "fr" => "préambule", "es" => "preámbulo", _ => "preamble" } : $"`{p.BlockId}`")
                + (p.Line is int l ? $" (l.{l})" : "") + ": " + p.Message)
            .ToList();
        var head = lang switch
        {
            "fr" => repair.Exhausted
                ? $"❌ **Vérification :** le document ne compile toujours pas après {repair.Attempts} tentatives."
                : "❌ **Vérification :** le document ne compile pas.",
            "es" => repair.Exhausted
                ? $"❌ **Comprobación:** el documento sigue sin compilar tras {repair.Attempts} intentos."
                : "❌ **Comprobación:** el documento no compila.",
            _ => repair.Exhausted
                ? $"❌ **Compile check:** the document still does not compile after {repair.Attempts} attempts."
                : "❌ **Compile check:** the document does not compile.",
        };
        if (failing.Count == 0) return head;
        var label = lang switch { "fr" => "Échoue encore", "es" => "Sigue fallando", _ => "Still failing" };
        return head + $" {label}:\n" + string.Join("\n", failing.Select(f => "- " + f));
    }
}
