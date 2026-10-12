using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lilia.Api.Hubs;
using Lilia.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol.Server;

namespace Lilia.Api.Mcp;

/// <summary>
/// The Lilia MCP server, step 3: tools that change a document (the user, 12 Oct: "go ahead with
/// step 3", and "signalr should be used when we use the mcp").
///
/// <list type="bullet">
/// <item><b>Same writes as Ask Lilia.</b> Each tool runs Ask Lilia's own edit tool
///   (<see cref="AskLiliaService.BuildDocumentTools"/>): same validation, same saving.</item>
/// <item><b>Who may write.</b> The author must be the owner or an editor. With
///   <c>Mcp:RequireWriteScope</c> on, an app's token must also carry <see cref="WriteScope"/> (the
///   author allowed edits on the consent page); it is off for now.</item>
/// <item><b>Undo.</b> Before an app's first change to a document (again after
///   <see cref="UndoWindow"/> without changes), a version is saved; every reply gives its id.</item>
/// <item><b>Live.</b> The open editor hears <c>DocumentChangedExternally</c> on the document's
///   SignalR room, names the app, reloads, and offers Undo.</item>
/// </list>
/// </summary>
[McpServerToolType]
public sealed class LiliaMcpWriteTools(
    IHttpContextAccessor http,
    IDocumentService documents,
    IAskLiliaService ask,
    IVersionService versions,
    IHubContext<DocumentHub> hub,
    IMemoryCache cache,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ConnectorTransfers transfers,
    ILogger<LiliaMcpWriteTools> logger)
{
    public const string WriteScope = "documents:write";
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(10);

    private System.Security.Claims.ClaimsPrincipal? User => http.HttpContext?.User;

    private string UserId =>
        User?.FindFirst("sub")?.Value
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Not signed in.");

    /// <summary>The token's scopes (an app's token); null for Lilia's own sign-in.</summary>
    internal static string[]? Scopes(System.Security.Claims.ClaimsPrincipal? user)
    {
        if (!McpOAuth.IsConnectedAppToken(user)) return null;
        var raw = user!.FindFirst("scope")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/scope")?.Value ?? "";
        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Lilia's own sign-in may write. An app may write too, unless <c>Mcp:RequireWriteScope</c> is
    /// on: then only with <see cref="WriteScope"/> (the author allowed edits on the consent page).
    /// Off for now (the user, 12 Oct: "for now, let's give the mcp write capability also"): the
    /// Stytch RBAC scopes are not set up yet, and the consent page says the app can edit.
    /// </summary>
    internal static bool AppMayWrite(System.Security.Claims.ClaimsPrincipal? user, bool requireScope) =>
        !requireScope || Scopes(user) is not { } scopes || scopes.Contains(WriteScope);

    public static bool RequireWriteScope(IConfiguration config) => config.GetValue<bool>("Mcp:RequireWriteScope");

    private const string ReconnectMessage =
        "This app was connected to Lilia before it could edit, so it may only read. To let it edit, " +
        "disconnect Lilia in the app (for Claude: Settings → Connectors) and connect again: Lilia will ask " +
        "whether to allow edits.";

    // ── the tools ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "add_block", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Add a block to a document (small, targeted inserts). Returns the new block id. Types: paragraph, heading, equation, theorem, code, list, table, figure, abstract, blockquote; on a CV personalInfo, cvSection, cvEntry. The open editor shows the change at once; the reply gives the version to restore to undo it.")]
    public Task<object> AddBlock(
        [Description("The document id.")] string documentId,
        [Description("Block type, e.g. paragraph, heading, equation.")] string type,
        [Description("JSON content for the type, e.g. {\"text\":\"…\"}, {\"text\":\"…\",\"level\":2}, {\"latex\":\"…\"}.")] string content,
        [Description("Insert after this block id; omit to append at the end.")] string? afterId = null) =>
        WriteAsync(documentId, "add_block", new() { ["type"] = type, ["content"] = content, ["afterId"] = afterId });

    [McpServerTool(Name = "edit_block", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Replace one block's content (and optionally its type) by id. Read it first with get_block.")]
    public Task<object> EditBlock(
        [Description("The document id.")] string documentId,
        [Description("The block id (from get_outline).")] string blockId,
        [Description("The new JSON content for the block.")] string content,
        [Description("Optional new block type.")] string? type = null) =>
        WriteAsync(documentId, "edit_block", new() { ["blockId"] = blockId, ["content"] = content, ["type"] = type });

    [McpServerTool(Name = "remove_block", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete one block by id. The version saved before the change can restore it.")]
    public Task<object> RemoveBlock(
        [Description("The document id.")] string documentId,
        [Description("The block id to delete.")] string blockId) =>
        WriteAsync(documentId, "remove_block", new() { ["blockId"] = blockId });

    [McpServerTool(Name = "reorder_blocks", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Put the document's blocks in this exact order (every block id, from get_outline).")]
    public Task<object> ReorderBlocks(
        [Description("The document id.")] string documentId,
        [Description("All block ids in the new order.")] string[] blockIds) =>
        WriteAsync(documentId, "reorder_blocks", new() { ["blockIds"] = blockIds });

    [McpServerTool(Name = "set_title", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set the document's title (also its name and the LaTeX \\title), and optionally the author and date.")]
    public Task<object> SetTitle(
        [Description("The document id.")] string documentId,
        [Description("The title.")] string title,
        [Description("Author name(s).")] string? author = null,
        [Description("Date, e.g. 'October 2026' or '\\\\today'.")] string? date = null) =>
        WriteAsync(documentId, "set_title", new() { ["title"] = title, ["author"] = author, ["date"] = date });

    [McpServerTool(Name = "apply_lml", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Replace the WHOLE document body with LML, Lilia's block markup, in one step (for rewrites; prefer edit_block for small changes). Academic: @abstract, @heading[level=1], @paragraph, @equation[mode=display], @theorem[…], @bibliography; bodies indented 2 spaces. The version saved before can restore the old text.")]
    public Task<object> ApplyLml(
        [Description("The document id.")] string documentId,
        [Description("The full LML source for the body.")] string lml,
        [Description("Optional new title.")] string? title = null) =>
        WriteAsync(documentId, "apply_lml", new() { ["lml"] = lml, ["title"] = title });

    [McpServerTool(Name = "set_document_settings", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Change the page setup. Pass only the fields to change, as one JSON object: paperSize (a4|letter|legal|a5|executive|b5), orientation (portrait|landscape), marginTop/marginBottom/marginLeft/marginRight (e.g. 2.5cm, \"\" = class default), columns (1-3), columnGap (cm), columnSeparator (none|rule), balancedColumns, fontFamily (serif|sans-serif|monospace|charter|times|palatino|bookman), fontSize (10|11|12), lineSpacing (0.8-3), paragraphIndent (length or none), pageNumbering (arabic|roman|none), headerLeft/headerCenter/headerRight/footerLeft/footerCenter/footerRight (plain text), customPreamble (replaces the whole preamble: read it first with get_document_settings). Invalid values change nothing and list the valid ones.")]
    public Task<object> SetDocumentSettings(
        [Description("The document id.")] string documentId,
        [Description("The fields to change, e.g. {\"fontSize\":12,\"marginLeft\":\"2cm\"}.")] JsonElement settings)
    {
        var args = new Dictionary<string, object?>();
        if (settings.ValueKind == JsonValueKind.Object)
            foreach (var p in settings.EnumerateObject()) args[p.Name] = p.Value;
        return WriteAsync(documentId, "set_document_settings", args);
    }

    [McpServerTool(Name = "create_document", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a new document owned by the author, optionally with its body written in LML (as apply_lml). Kinds: article (default), report, book. Returns its id and address; then edit it with the other tools.")]
    public async Task<object> CreateDocument(
        [Description("The document's title.")] string title,
        [Description("Optional body in LML, e.g. \"@heading[level=1]\\n  Introduction\\n@paragraph\\n  Text.\".")] string? lml = null,
        [Description("article, report or book. Default article.")] string? kind = null)
    {
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        if (UnsafeIn(new object?[] { title, lml }) is { } refused) return new { error = refused, category = "refused" };
        var category = (kind ?? "article").Trim().ToLowerInvariant() switch
        {
            "report" => "report",
            "book" or "thesis" => "book",
            _ => "article",
        };
        var doc = await documents.CreateDocumentAsync(UserId, new Lilia.Api.Models.Documents.CreateDocumentDto
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim(),
            DocumentCategory = category,
            DocumentClass = category,
        });
        object? body = null;
        if (!string.IsNullOrWhiteSpace(lml))
            body = await WriteAsync(doc.Id.ToString(), "apply_lml", new() { ["lml"] = lml });
        return new { id = doc.Id, title = doc.Title, kind = category, body, url = $"{McpOAuth.PublicOrigin(config)}/d/{doc.Id}" };
    }

    [McpServerTool(Name = "start_import", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Import a file (.tex, .docx or .lml) as a new Lilia document, without the file passing through this conversation: returns a link to Lilia's drop page. Give the author the link; they drop the file there (signed in to Lilia). Then call import_status with the importId to get the new document. The link works for 30 minutes.")]
    public async Task<object> StartImport()
    {
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        var (token, ticket) = transfers.IssueImport(UserId, await AppNameAsync());
        return new
        {
            importId = token,
            url = $"{McpOAuth.PublicOrigin(config)}/connect/import/{token}",
            accepts = new[] { ".tex", ".docx", ".lml" },
            expiresAt = ticket.ExpiresAt,
            note = "Give the author this link to drop the file. When they say it's done, call import_status with the importId.",
        };
    }

    [McpServerTool(Name = "import_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Where an import started with start_import is: waiting (no file dropped yet), done (with the new document's id) or failed (with the reason).")]
    public object ImportStatus([Description("The importId from start_import.")] string importId)
    {
        if (transfers.Import(importId?.Trim() ?? "", UserId) is not { } t)
            return new { error = "No such import: it may have expired (30 minutes). Call start_import again." };
        return new
        {
            status = t.Status,
            documentId = t.DocumentId,
            title = t.Title,
            error = t.Error,
            url = t.DocumentId is { } id ? $"{McpOAuth.PublicOrigin(config)}/d/{id}" : null,
            next = t.Status switch
            {
                "done" => "Read it with get_outline, or check it compiles with check_document.",
                "failed" => "Tell the author why; they can try another file with a new start_import.",
                _ => "The author hasn't dropped the file yet.",
            },
        };
    }

    // ── one write ─────────────────────────────────────────────────────────

    /// <summary>
    /// MCP-45: what the compile would refuse (\write18, \input{/etc/…}, \directlua…) is refused when
    /// it is written, with the guard's own message, instead of being stored and failing every check.
    /// </summary>
    internal static string? UnsafeIn(IEnumerable<object?> values)
    {
        foreach (var v in values)
        {
            var text = v switch { string s => s, JsonElement e => e.ToString(), _ => null };
            if (text is not null && Lilia.Engines.TexSafety.TexSourceGuard.Violation(text) is { } why)
                return $"Lilia refuses this LaTeX: {why} Nothing was changed.";
        }
        return null;
    }

    private async Task<object> WriteAsync(string documentId, string tool, Dictionary<string, object?> args)
    {
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        if (UnsafeIn(args.Values) is { } refused) return new { error = refused, category = "refused" };
        var doc = Guid.TryParse(documentId?.Trim(), out var id) ? await documents.GetDocumentAsync(id, UserId) : null;
        if (doc is null) return new { error = $"No document '{documentId}' that you can open. Call list_documents for ids." };
        if (!AskLiliaService.MayEditWithAi(true, doc))
            return new { error = "You can only read this document (view-only access). Its owner can give you edit access." };

        var app = await AppNameAsync();
        var undo = await UndoPointAsync(doc.Id, app);
        var changed = new List<string>();
        var meta = false;
        var live = new AskLiliaService.LiveDocument(doc);
        var fn = ((AskLiliaService)ask)
            .BuildDocumentTools(live, doc.Id, UserId, allowWrite: true, changed, () => meta = true, CancellationToken.None)
            .OfType<AIFunction>().First(f => f.Name == tool);

        object? result;
        try
        {
            result = await fn.InvokeAsync(new AIFunctionArguments(args.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] {Tool} failed on {DocId}", tool, doc.Id);
            return new { error = $"Lilia could not {tool.Replace('_', ' ')}: {ex.Message}" };
        }

        if (changed.Count > 0 || meta)
        {
            // The open editor reloads, names the app and offers Undo.
            await hub.Clients.Group($"doc-{doc.Id}").SendAsync("DocumentChangedExternally", new
            {
                source = "mcp",
                app,
                tool,
                blockIds = changed,
                undoVersionId = undo,
                at = DateTime.UtcNow,
            });
        }
        return new
        {
            result,
            changedBlocks = changed,
            undo = undo is null ? null : new { versionId = undo, note = "Restore this version in History to undo every change made since it was saved." },
            url = $"{McpOAuth.PublicOrigin(config)}/d/{doc.Id}",
        };
    }

    /// <summary>
    /// A version before an app's first change, then one per <see cref="UndoWindow"/> of quiet:
    /// one undo point for a burst of edits rather than one per call.
    /// </summary>
    private async Task<Guid?> UndoPointAsync(Guid docId, string app)
    {
        var key = $"mcp-undo:{docId}:{User?.FindFirst("client_id")?.Value ?? "lilia"}";
        if (cache.TryGetValue(key, out Guid known))
        {
            cache.Set(key, known, new MemoryCacheEntryOptions { SlidingExpiration = UndoWindow });
            return known;
        }
        try
        {
            var v = await versions.CreateVersionAsync(docId, UserId, new Lilia.Core.DTOs.CreateVersionDto($"Before {app} edit"));
            cache.Set(key, v.Id, new MemoryCacheEntryOptions { SlidingExpiration = UndoWindow });
            return v.Id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] undo point failed for {DocId}", docId);
            return null;
        }
    }

    /// <summary>The app's name as it registered with Stytch ("Claude"), for the editor's message.</summary>
    private async Task<string> AppNameAsync()
    {
        var clientId = User?.FindFirst("client_id")?.Value;
        if (clientId is null) return "Lilia";
        if (cache.TryGetValue($"mcp-app:{clientId}", out string? known) && known is not null) return known;
        var name = "An app";
        try
        {
            var projectId = config["Stytch:ProjectId"];
            var secret = config["Stytch:Secret"];
            if (!string.IsNullOrEmpty(projectId) && !string.IsNullOrEmpty(secret))
            {
                var client = httpFactory.CreateClient();
                client.BaseAddress = new Uri(config["Stytch:ApiBase"] ?? "https://api.stytch.com");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{projectId}:{secret}")));
                using var res = await client.GetAsync($"/v1/connected_apps/clients/{Uri.EscapeDataString(clientId)}");
                if (res.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    if (json.RootElement.TryGetProperty("connected_app", out var app)
                        && app.TryGetProperty("client_name", out var n) && n.GetString() is { Length: > 0 } s)
                        name = s.Length > 60 ? s[..60] : s;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Mcp] app name lookup failed for {ClientId}", clientId);
        }
        cache.Set($"mcp-app:{clientId}", name, TimeSpan.FromHours(6));
        return name;
    }
}
