using System.ComponentModel;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
using Lilia.Api.Models.AiArchitect;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

/// <summary>
/// "Ask Lilia" generation dispatch. Routes a natural-language message to a skill
/// (<see cref="IAskLiliaRouter"/>), assembles the system prompt = proficiency
/// preamble + the skill's SKILL.md guidance + a uniform output note, and runs it
/// through the same governed path as the architect (key gate, entitlement/budget,
/// model catalog, ai_requests audit, credit metering).
///
/// Text mode: the reply carries the artifact (LML / BibTeX / LaTeX in fenced
/// blocks the author copies or applies). The Document Architect keeps its dedicated
/// structured-BlockOps endpoint for in-place document editing; structured ops for
/// the other block-emitting skills are a later refinement.
/// </summary>
public interface IAskLiliaService
{
    Task<AskLiliaResult> AskAsync(string userId, AskLiliaRequest request, CancellationToken ct = default);
}

public sealed record AskLiliaRequest(string Message, string? Proficiency = null, string? Model = null, string? DocumentId = null, bool EditMode = false);

public sealed record AskLiliaResponse(
    string SkillId, string SkillName, string Reply,
    AiArchitectUsage Usage, AiArchitectBalance? Balance, int CreditsUsed, bool DocumentChanged = false,
    IReadOnlyList<string>? ChangedBlockIds = null, string? UndoVersionId = null);

public sealed record AskLiliaResult(bool Locked, string? Reason, string? Message, AskLiliaResponse? Response)
{
    public static AskLiliaResult Lock(string reason, string message) => new(true, reason, message, null);
    public static AskLiliaResult Ok(AskLiliaResponse r) => new(false, null, null, r);
}

public sealed class AskLiliaService : IAskLiliaService
{
    private readonly IChatClient _chatClient;
    private readonly IEntitlementService _entitlement;
    private readonly IAiCatalogService _catalog;
    private readonly IAskLiliaRouter _router;
    private readonly IKbService _kb;
    private readonly IDocumentService _documentService;
    private readonly IBlockService _blockService;
    private readonly IVersionService _versionService;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Lilia.Api.Hubs.DocumentHub> _hub;
    private readonly LiliaDbContext _context;
    private readonly AiOptions _options;
    private readonly ILogger<AskLiliaService> _logger;
    private readonly bool _useAi;
    private readonly bool _enabled;
    private readonly bool _enforceCredits;

    private const int MaxOutputTokens = 4096;
    // Bound the tool-use loop: each round is one model call; this caps cost and
    // guarantees termination even if the model keeps requesting tools.
    private const int MaxToolRounds = 4;

    private const string OutputNote = """
        OUTPUT — respond conversationally to the author. Put any LML, BibTeX, or LaTeX you produce
        in fenced code blocks they can copy into Lilia (or apply). Lead with the artifact; keep prose
        short. Follow the AUTHOR LEVEL guidance above. Never fabricate citations, numbers, datasets,
        or results — keep specifics the author must supply generic and flagged.

        KNOWLEDGE BASE — you have two tools over Lilia's help catalog:
        • search_kb(query): find the right help article by intent (returns slug, title, summary, href);
        • get_kb(slug): read one article's full body.
        Use them when the author asks how/where/what, or when you point them to a tool or feature —
        ground your answer in what the tools return and cite the article by title and its href
        (e.g. "see Data → LaTeX table — /help/latex-table"). Do NOT invent article slugs, hrefs, or
        Lilia behaviour: if search_kb returns nothing, say so and answer from the skill guidance.
        Skip the tools for pure generation (e.g. "write this equation") where no docs are needed.
        """;

    public AskLiliaService(
        IChatClient chatClient,
        IEntitlementService entitlement,
        IAiCatalogService catalog,
        IAskLiliaRouter router,
        IKbService kb,
        IDocumentService documentService,
        IBlockService blockService,
        IVersionService versionService,
        Microsoft.AspNetCore.SignalR.IHubContext<Lilia.Api.Hubs.DocumentHub> hub,
        LiliaDbContext context,
        IOptions<AiOptions> options,
        IConfiguration configuration,
        ILogger<AskLiliaService> logger)
    {
        _chatClient = chatClient;
        _entitlement = entitlement;
        _catalog = catalog;
        _router = router;
        _kb = kb;
        _documentService = documentService;
        _blockService = blockService;
        _versionService = versionService;
        _hub = hub;
        _context = context;
        _options = options.Value;
        _logger = logger;
        _useAi = !string.IsNullOrEmpty(_options.Anthropic.ApiKey) && _options.Anthropic.ApiKey != "sk-placeholder";
        _enabled = configuration.GetValue("AI:Enabled", true);
        _enforceCredits = configuration.GetValue("AI:EnforceCredits", false);
    }

    // ai_requests.purpose is CHECK-constrained — map each skill to a member of the
    // closed vocabulary, defaulting to 'other'.
    private static string PurposeFor(string skillId) => skillId switch
    {
        "lilia-citations" => "suggest_bibliography",
        "lilia-compile-doctor" => "fix_latex",
        "lilia-polish" => "rephrase",
        "lilia-journal" => "suggest_headings",
        "lilia-document-architect" => "expand_outline",
        _ => "other",
    };

    public async Task<AskLiliaResult> AskAsync(string userId, AskLiliaRequest request, CancellationToken ct = default)
    {
        // ── gate ──────────────────────────────────────────────────────────
        if (!_enabled) return AskLiliaResult.Lock("disabled", "AI is currently disabled.");
        if (!_useAi) return AskLiliaResult.Lock("no-key", "AI is not configured.");

        try
        {
            await _entitlement.EnsureQuotaAsync(userId, QuotaResource.AiCredits, delta: 1, ct);
        }
        catch (QuotaExceededException)
        {
            if (_enforceCredits)
                return AskLiliaResult.Lock("over-budget", "You've used all your AI credits for this period.");
            _logger.LogInformation("[AskLilia] User {UserId} over budget; credit enforcement off — allowing", userId);
        }

        // ── route + prompt ────────────────────────────────────────────────
        var route = _router.Route(request.Message);
        var skill = _router.Get(route.SkillId);
        var level = ProficiencyGuidance.Parse(request.Proficiency);

        var guidance = AiSkillGuidance.Get(skill.Id);
        var systemSb = new StringBuilder()
            .AppendLine(ProficiencyGuidance.For(level)).AppendLine()
            .AppendLine(string.IsNullOrEmpty(guidance) ? $"You are the {skill.Name} skill for Lilia. {skill.WhenToUse}" : guidance)
            .AppendLine().AppendLine(OutputNote);

        // Document context — when Ask Lilia is open inside a document, ground the
        // answer in that doc's current blocks (the author is editing it live).
        Guid? documentId = null;
        Lilia.Core.DTOs.DocumentDto? document = null;
        if (!string.IsNullOrWhiteSpace(request.DocumentId) && Guid.TryParse(request.DocumentId, out var docGuid))
        {
            documentId = docGuid;
            try
            {
                document = await _documentService.GetDocumentAsync(docGuid, userId);
                if (document is not null)
                {
                    systemSb.AppendLine()
                        .AppendLine("CURRENT DOCUMENT — the author is editing this right now. You also have tools to READ it on demand: get_outline (structure + block ids), get_block (one block's full content by id), search_document (find text). Prefer the tools for detail; reference existing blocks, match style/structure, and don't restate what's already there.");
                    if (request.EditMode)
                        systemSb.AppendLine("EDIT MODE IS ON — you may also WRITE to the document with add_block, edit_block, remove_block, reorder_blocks, set_title. Make the changes the author asked for directly via these tools (read first to get the right block ids). Keep edits minimal and on-target; after editing, briefly summarize what you changed. `content` is a JSON object matching the block type, e.g. {\"text\":\"…\"} (paragraph), {\"text\":\"…\",\"level\":1} (heading), {\"latex\":\"…\"} (equation), {\"theoremType\":\"theorem\",\"text\":\"…\"} (theorem). To change the document title (or author/date), use set_title — NOT a heading; the title is the document's \\title and its name.");
                    systemSb.AppendLine(AiArchitectService.BuildDocumentContext(document));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AskLilia] document context load failed for {DocId}", request.DocumentId);
            }
        }
        else
        {
            // Library scope — no open document. Ground the answer in the author's
            // whole library (title · AI gist · readiness · size) so questions like
            // "what's unfinished / which have issues / closest to submitting /
            // which cite X" can be reasoned across documents.
            try
            {
                var lib = await _documentService.GetDocumentsPaginatedAsync(userId, 1, 50, sortBy: "updatedAt", sortDir: "desc");
                if (lib.Items.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine()
                      .AppendLine($"THE LIBRARY — the author's {lib.TotalCount} document(s), most-recent first. Reason ACROSS them. When you point to a specific document, write its EXACT title in double quotes so it can be linked. Don't invent documents not listed here.");
                    foreach (var d in lib.Items)
                    {
                        var issues = d.ValidationErrorCount + d.ValidationWarningCount;
                        var readiness = issues > 0 ? $"{issues} issue(s)"
                            : d.BlockCount == 0 ? "empty"
                            : d.ValidationCheckedAt == null ? "draft (unchecked)"
                            : "clean";
                        sb.Append("- \"").Append(d.Title).Append("\" — ")
                          .Append(string.IsNullOrWhiteSpace(d.AiSummary) ? "(no summary yet)" : d.AiSummary)
                          .Append(" · ").Append(d.BlockCount).Append(" blocks · ").Append(readiness)
                          .AppendLine();
                    }
                    systemSb.AppendLine(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AskLilia] library context load failed");
            }
        }

        var system = systemSb.ToString();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, request.Message),
        };

        // ── model resolution (catalog default; honour a tier-allowed override) ─
        var model = _catalog.DefaultModelId();
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            var plan = await _entitlement.GetActivePlanAsync(userId, ct);
            var slug = plan?.Slug?.ToLowerInvariant();
            var tier = slug is "pro" or "team" ? slug : "free";
            if (_catalog.IsAllowedFor(request.Model, tier)) model = request.Model;
        }

        // ── audit → tool-use loop → meter ─────────────────────────────────
        var aiRequestId = await PersistPendingAsync(userId, PurposeFor(skill.Id), model, messages, documentId, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[AskLilia] skill={Skill} user={UserId} model={Model}", skill.Id, userId, model);

            var changed = new List<string>(); // block ids the write tools touched
            // Undo snapshot — in Edit mode, capture the doc state before the AI
            // can write, so the client can offer "Undo AI changes". Surfaced
            // only if a write actually happened.
            Guid? undoVersionId = null;
            if (request.EditMode && document is not null)
            {
                try { undoVersionId = (await _versionService.CreateVersionAsync(documentId!.Value, userId, new Lilia.Core.DTOs.CreateVersionDto("Before Ask Lilia edit"))).Id; }
                catch (Exception ex) { _logger.LogWarning(ex, "[AskLilia] undo snapshot failed for {DocId}", documentId); }
            }
            var tools = BuildKbTools(ct);
            if (document is not null)
                tools = tools.Concat(BuildDocumentTools(document, documentId!.Value, request.EditMode, changed, ct)).ToList();
            var options = new ChatOptions
            {
                ModelId = model,
                MaxOutputTokens = MaxOutputTokens,
                Temperature = 0.4f,
                Tools = tools,
            };

            // Manual tool-use loop: the model may call search_kb/get_kb to ground its
            // answer. Each round is one model call; we execute any tool calls, feed the
            // results back, and continue until the model returns a final text answer (or
            // we hit the round cap). Token usage is summed across all rounds for metering.
            ChatResponse response = null!;
            var inputTokens = 0;
            var outputTokens = 0;
            var toolCalls = 0;
            for (var round = 0; round < MaxToolRounds; round++)
            {
                response = await _chatClient.GetResponseAsync(messages, options, ct);
                inputTokens += (int?)response.Usage?.InputTokenCount ?? 0;
                outputTokens += (int?)response.Usage?.OutputTokenCount ?? 0;

                // Carry the assistant turn (incl. any function-call content) into history.
                messages.AddRange(response.Messages);

                var calls = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();
                if (calls.Count == 0) break; // final answer

                foreach (var call in calls)
                {
                    toolCalls++;
                    var toolResult = await InvokeKbToolAsync(tools, call, ct);
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(call.CallId, toolResult)]));
                }
            }
            if (toolCalls > 0)
                _logger.LogInformation("[AskLilia] skill={Skill} used {Calls} KB tool call(s)", skill.Id, toolCalls);

            var reply = (response.Text ?? string.Empty).Trim();
            var costUsd = AiArchitectPricing.ComputeCostUsd(model, inputTokens, outputTokens);

            await MarkAsync(aiRequestId, "success", null, inputTokens, outputTokens, (int)sw.ElapsedMilliseconds, ct);

            AiArchitectBalance? balance = null;
            var credits = 0;
            var creditsUsed = 0;
            try
            {
                credits = await _entitlement.RecordAiSpendAsync(userId, model, inputTokens, outputTokens, aiRequestId, ct);
                var creditsLeft = await _entitlement.GetAiCreditBalanceAsync(userId, ct);
                balance = new AiArchitectBalance(AiArchitectPricing.CreditsToUsd(creditsLeft));
                creditsUsed = await _entitlement.GetAiCreditsConsumedAsync(userId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AskLilia] Credit debit failed for user {UserId}", userId);
            }

            var result = new AskLiliaResponse(
                skill.Id, skill.Name, reply,
                new AiArchitectUsage(inputTokens, outputTokens, costUsd, credits), balance, creditsUsed,
                DocumentChanged: changed.Count > 0, ChangedBlockIds: changed.Distinct().ToList(),
                UndoVersionId: changed.Count > 0 ? undoVersionId?.ToString() : null);
            return AskLiliaResult.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AskLilia] AI call failed for user {UserId} skill {Skill}", userId, skill.Id);
            await MarkAsync(aiRequestId, "error", Truncate(ex.Message, 500), 0, 0, (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
    }

    // ── KB tool-use ───────────────────────────────────────────────────────
    // The two knowledge-base tools handed to the model. AIFunctionFactory builds
    // each tool's JSON schema from the delegate signature + [Description]; the
    // delegate body is the execution, so InvokeKbToolAsync dispatches by name.
    private IList<AITool> BuildKbTools(CancellationToken ct) =>
    [
        AIFunctionFactory.Create(
            ([Description("What the author wants to do or know, in their own words (e.g. 'make a table from csv', 'cite a doi').")] string query)
                => SearchKbAsync(query, ct),
            name: "search_kb",
            description: "Search the Lilia knowledge base for help articles by intent. Returns up to a few matches as {slug, title, summary, toolSlug, skillId, href}. Use it to find the right doc to ground your answer and cite."),
        AIFunctionFactory.Create(
            ([Description("The article slug returned by search_kb, e.g. 'latex-table'.")] string slug)
                => GetKbAsync(slug, ct),
            name: "get_kb",
            description: "Fetch one Lilia knowledge-base article's full body by slug. Use after search_kb when you need the step-by-step detail."),
    ];

    private async Task<object> SearchKbAsync(string query, CancellationToken ct)
    {
        var hits = await _kb.SearchAsync(query ?? string.Empty, 6, ct);
        return new
        {
            results = hits.Select(a => new
            {
                a.Slug, a.Title, a.Summary, a.ToolSlug, a.SkillId,
                href = $"/help/{a.Slug}",
            }),
        };
    }

    private async Task<object> GetKbAsync(string slug, CancellationToken ct)
    {
        var a = string.IsNullOrWhiteSpace(slug) ? null : await _kb.GetAsync(slug, ct);
        return a is null
            ? new { error = $"No article '{slug}'. Call search_kb first." }
            : (object)new { a.Slug, a.Title, a.Body, a.ToolSlug, a.SkillId, href = $"/help/{a.Slug}" };
    }

    // ── Document read tools (Phase 1 of agentic Ask Lilia) ───────────────────
    // Let the model READ the open document on demand instead of only the static
    // dump: outline/structure, one block's full content, or a text search.
    private IList<AITool> BuildDocumentTools(Lilia.Core.DTOs.DocumentDto document, Guid docGuid, bool allowWrite, List<string> changed, CancellationToken ct)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                () => DocOutline(document),
                name: "get_outline",
                description: "Get the open document's structure: every block in order with {id, type, snippet}, plus the heading outline {id, level, text}. Use it to understand the document before answering or editing."),
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("A block id from get_outline or search_document.")] string blockId)
                    => DocBlock(document, blockId),
                name: "get_block",
                description: "Read one block's full type + content by its id (from get_outline/search_document)."),
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Text to find in the document.")] string query)
                    => DocSearch(document, query),
                name: "search_document",
                description: "Search the open document's text; returns matching blocks as {id, type, snippet}."),
        };

        if (allowWrite)
        {
            tools.Add(AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Block type: paragraph, heading, equation, theorem, code, list, table, abstract, blockquote. (For the document title use set_title, not a heading.)")] string type,
                 [System.ComponentModel.Description("JSON content object matching the type, e.g. {\"text\":\"…\"} or {\"text\":\"…\",\"level\":1} or {\"latex\":\"…\"}.")] string content,
                 [System.ComponentModel.Description("Insert after this block id; omit to append at the end.")] string? afterId)
                    => AddBlockAsync(document, docGuid, type, content, afterId, changed),
                name: "add_block",
                description: "Add a new block to the open document. Returns the new block id."));
            tools.Add(AIFunctionFactory.Create(
                ([System.ComponentModel.Description("The block id to edit.")] string blockId,
                 [System.ComponentModel.Description("New JSON content object for the block.")] string content,
                 [System.ComponentModel.Description("Optional new block type.")] string? type)
                    => EditBlockAsync(document, docGuid, blockId, content, type, changed),
                name: "edit_block",
                description: "Replace a block's content (and optionally its type) by id."));
            tools.Add(AIFunctionFactory.Create(
                ([System.ComponentModel.Description("The block id to remove.")] string blockId)
                    => RemoveBlockAsync(document, docGuid, blockId, changed),
                name: "remove_block",
                description: "Delete a block by id."));
            tools.Add(AIFunctionFactory.Create(
                ([System.ComponentModel.Description("All block ids in the desired final order.")] string[] blockIds)
                    => ReorderBlocksAsync(document, docGuid, blockIds, changed),
                name: "reorder_blocks",
                description: "Reorder the document's blocks to this exact id order."));
            tools.Add(AIFunctionFactory.Create(
                ([System.ComponentModel.Description("The document title. Becomes the document name AND the compiled LaTeX \\title.")] string title,
                 [System.ComponentModel.Description("Author name(s). Optional.")] string? author,
                 [System.ComponentModel.Description("Date, e.g. 'June 2026'. Optional.")] string? date)
                    => SetTitleAsync(document, docGuid, title, author, date, changed),
                name: "set_title",
                description: "Set the document's title (and optionally author/date). Use this for any request to change/set the title — it creates or updates the Title block (\\title/\\author/\\date) and keeps the document name in sync. Never use a heading block as the title."));
        }
        return tools;
    }

    // ── Write tool implementations — go through the access-checked block
    // service (same path the editor uses); mirror the change into the in-memory
    // doc so later read tools stay consistent; flag that the doc changed.
    private async Task<object> AddBlockAsync(Lilia.Core.DTOs.DocumentDto doc, Guid docId, string type, string contentJson, string? afterId, List<string> changed)
    {
        int? sortOrder = null;
        if (!string.IsNullOrWhiteSpace(afterId) && Guid.TryParse(afterId, out var aid))
        {
            var anchor = doc.Blocks?.FirstOrDefault(b => b.Id == aid);
            if (anchor is not null) sortOrder = anchor.SortOrder + 1;
        }
        var created = await _blockService.CreateBlockAsync(docId,
            new Lilia.Core.DTOs.CreateBlockDto(type, ParseContent(contentJson), sortOrder, null, null));
        doc.Blocks?.Add(created);
        changed.Add(created.Id.ToString());
        await _hub.Clients.Group($"doc-{docId}").SendAsync("AiBlockChanged",
            new { op = "add", id = created.Id, type = created.Type, content = created.Content, afterId, sortOrder = created.SortOrder });
        return new { ok = true, id = created.Id };
    }

    private async Task<object> EditBlockAsync(Lilia.Core.DTOs.DocumentDto doc, Guid docId, string blockId, string contentJson, string? type, List<string> changed)
    {
        if (!Guid.TryParse(blockId, out var id)) return new { error = "invalid block id" };
        var updated = await _blockService.UpdateBlockAsync(docId, id,
            new Lilia.Core.DTOs.UpdateBlockDto(type, ParseContent(contentJson), null, null, null));
        if (updated is null) return new { error = "block not found" };
        var i = doc.Blocks?.FindIndex(b => b.Id == id) ?? -1;
        if (i >= 0) doc.Blocks![i] = updated;
        changed.Add(id.ToString());
        await _hub.Clients.Group($"doc-{docId}").SendAsync("AiBlockChanged",
            new { op = "edit", id, type = updated.Type, content = updated.Content });
        return new { ok = true, id };
    }

    private async Task<object> RemoveBlockAsync(Lilia.Core.DTOs.DocumentDto doc, Guid docId, string blockId, List<string> changed)
    {
        if (!Guid.TryParse(blockId, out var id)) return new { error = "invalid block id" };
        var ok = await _blockService.DeleteBlockAsync(docId, id);
        if (!ok) return new { error = "block not found" };
        doc.Blocks?.RemoveAll(b => b.Id == id);
        changed.Add(id.ToString());
        await _hub.Clients.Group($"doc-{docId}").SendAsync("AiBlockChanged", new { op = "remove", id });
        return new { ok = true };
    }

    private async Task<object> ReorderBlocksAsync(Lilia.Core.DTOs.DocumentDto doc, Guid docId, string[] blockIds, List<string> changed)
    {
        var ids = (blockIds ?? Array.Empty<string>())
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToList();
        if (ids.Count == 0) return new { error = "no valid block ids" };
        await _blockService.ReorderBlocksAsync(docId, ids);
        changed.AddRange(ids.Select(g => g.ToString()));
        await _hub.Clients.Group($"doc-{docId}").SendAsync("AiBlockChanged",
            new { op = "reorder", ids = ids.Select(g => g.ToString()).ToList() });
        return new { ok = true };
    }

    // Set the document title via the Title block (upsert at the top), keeping
    // the document name in sync (BlockService syncs documents.title on both
    // create and update). Deterministic so "change the title" always lands.
    private async Task<object> SetTitleAsync(Lilia.Core.DTOs.DocumentDto doc, Guid docId, string title, string? author, string? date, List<string> changed)
    {
        var content = System.Text.Json.JsonSerializer.SerializeToElement(
            new { title = title ?? "", author = author ?? "", date = date ?? "" });
        var existing = doc.Blocks?.FirstOrDefault(b => b.Type == Lilia.Core.Entities.BlockTypes.Title);
        if (existing is not null)
        {
            var updated = await _blockService.UpdateBlockAsync(docId, existing.Id,
                new Lilia.Core.DTOs.UpdateBlockDto(null, content, null, null, null));
            if (updated is null) return new { error = "title block not found" };
            var i = doc.Blocks!.FindIndex(b => b.Id == existing.Id);
            if (i >= 0) doc.Blocks[i] = updated;
            changed.Add(existing.Id.ToString());
            await _hub.Clients.Group($"doc-{docId}").SendAsync("AiBlockChanged",
                new { op = "edit", id = existing.Id, type = "title", content = updated.Content });
            return new { ok = true, id = existing.Id, title };
        }
        var created = await _blockService.CreateBlockAsync(docId,
            new Lilia.Core.DTOs.CreateBlockDto("title", content, 0, null, null));
        doc.Blocks?.Insert(0, created);
        changed.Add(created.Id.ToString());
        await _hub.Clients.Group($"doc-{docId}").SendAsync("AiBlockChanged",
            new { op = "add", id = created.Id, type = "title", content = created.Content, afterId = (string?)null, sortOrder = 0 });
        return new { ok = true, id = created.Id, title };
    }

    private static System.Text.Json.JsonElement ParseContent(string json)
    {
        try { return System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone(); }
        catch { return System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    private static object DocOutline(Lilia.Core.DTOs.DocumentDto document)
    {
        var blocks = (document.Blocks ?? new List<Lilia.Core.DTOs.BlockDto>()).OrderBy(b => b.SortOrder).ToList();
        return new
        {
            title = document.Title,
            blockCount = blocks.Count,
            outline = blocks
                .Where(b => b.Type.Contains("heading", StringComparison.OrdinalIgnoreCase))
                .Select(b => new { id = b.Id, level = HeadingLevel(b.Content), text = TextOf(b.Content) }),
            blocks = blocks.Select(b => new { id = b.Id, type = b.Type, snippet = Snippet(b.Content, 120) }),
        };
    }

    private static object DocBlock(Lilia.Core.DTOs.DocumentDto document, string blockId)
    {
        if (!Guid.TryParse(blockId, out var id)) return new { error = "invalid block id" };
        var b = (document.Blocks ?? new List<Lilia.Core.DTOs.BlockDto>()).FirstOrDefault(x => x.Id == id);
        return b is null
            ? new { error = "block not found" }
            : (object)new { id = b.Id, type = b.Type, content = b.Content };
    }

    private static object DocSearch(Lilia.Core.DTOs.DocumentDto document, string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return new { matches = Array.Empty<object>() };
        var matches = (document.Blocks ?? new List<Lilia.Core.DTOs.BlockDto>())
            .OrderBy(b => b.SortOrder)
            .Where(b => Snippet(b.Content, 4000).Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(b => new { id = b.Id, type = b.Type, snippet = Snippet(b.Content, 160) })
            .Take(12);
        return new { matches };
    }

    private static string TextOf(System.Text.Json.JsonElement c)
    {
        if (c.ValueKind != System.Text.Json.JsonValueKind.Object) return string.Empty;
        foreach (var key in new[] { "text", "title", "latex", "code", "caption" })
            if (c.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                return v.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static int HeadingLevel(System.Text.Json.JsonElement c) =>
        c.ValueKind == System.Text.Json.JsonValueKind.Object
        && c.TryGetProperty("level", out var v)
        && v.ValueKind == System.Text.Json.JsonValueKind.Number
            ? v.GetInt32() : 1;

    private static string Snippet(System.Text.Json.JsonElement c, int max)
    {
        var t = TextOf(c);
        if (string.IsNullOrEmpty(t)) t = c.GetRawText();
        t = System.Text.RegularExpressions.Regex.Replace(t, "\\s+", " ").Trim();
        return t.Length > max ? t[..max] + "…" : t;
    }

    private static async Task<object?> InvokeKbToolAsync(IList<AITool> tools, FunctionCallContent call, CancellationToken ct)
    {
        if (tools.OfType<AIFunction>().FirstOrDefault(f => f.Name == call.Name) is not { } fn)
            return new { error = $"Unknown tool '{call.Name}'." };
        try
        {
            return await fn.InvokeAsync(new AIFunctionArguments(call.Arguments), ct);
        }
        catch (Exception ex)
        {
            return new { error = "tool failed: " + ex.Message };
        }
    }

    private async Task<Guid> PersistPendingAsync(string userId, string purpose, string model, List<ChatMessage> messages, Guid? documentId, CancellationToken ct)
    {
        var row = new AiRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DocumentId = documentId,
            BlockId = null,
            Purpose = purpose,
            Provider = "anthropic",
            Model = model,
            PromptHash = HashPrompt(string.Join("\n", messages.Select(m => m.Text))),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        _context.AiRequests.Add(row);
        await _context.SaveChangesAsync(ct);
        return row.Id;
    }

    private async Task MarkAsync(Guid id, string status, string? errorMessage, int promptTokens, int completionTokens, int latencyMs, CancellationToken ct)
        => await _context.AiRequests.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.ErrorMessage, errorMessage)
                .SetProperty(r => r.PromptTokens, promptTokens)
                .SetProperty(r => r.CompletionTokens, completionTokens)
                .SetProperty(r => r.LatencyMs, latencyMs)
                .SetProperty(r => r.CompletedAt, DateTime.UtcNow), ct);

    private static string HashPrompt(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
