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
    IJobService jobs,
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

    /// <summary>The block types an agent writes (the invoice and CV types exist too, for those documents).</summary>
    internal static readonly string[] AgentBlockTypes =
    [
        "paragraph", "heading", "equation", "theorem", "list", "table", "figure", "code", "blockquote",
        "abstract", "algorithm", "callout", "footnote", "embed", "bibliography", "pagebreak", "columnbreak",
        "tableofcontents", "frontMatter", "backMatter", "personalInfo", "cvSection", "cvEntry",
    ];

    // ── the tools ─────────────────────────────────────────────────────────

    // One field guide for add_block and edit_block. Run 2 (MCP-84): it had no embed fields (a
    // {"latex":…} embed printed nothing), no algorithm line shape (a wrong one gave empty \STATEs)
    // and no bibliography.
    private const string FieldGuide = "Fields by type (others are ignored): paragraph {text}; heading {text, level 1-6, label?}; equation {latex, equationMode: display|inline, label?, numbered?}; theorem {text, theoremType: theorem|lemma|definition|proposition|corollary|remark|example|proof, title?, label?, numbered?}; list {items:[{text}], kind: bullet|numbered|description — for description items:[{text: term, description}]}; table {headers:[...], rows:[[...]], caption?, label?, columnAlign?}; figure {src, caption?, label?, width 0-1} or TikZ {kind:\"tikz\", source, caption?, label?}; code {code, language?, caption?, label?}; blockquote {text, attribution?, variant?: verse}; abstract {text}; algorithm {caption?, label?, lines:[{kind, text}]} with kind statement|require|ensure|if|elsif|else|endif|for|forall|endfor|while|endwhile|repeat|until|return|comment, or {code} with \\STATE / \\IF{…} lines; callout {text, title?, variant?}; footnote {text}; embed {code} — raw LaTeX printed as written; bibliography {text} — BibTeX entries (@article{key, …}) or lines \"@cite[key] Author — Title\"; frontMatter / backMatter {subType: preface|foreword|acknowledgements|dedication|afterword|appendix, text, title?}; CV: personalInfo {name, headline, email, phones:[{number}], homepage, location}, cvSection {title}, cvEntry {period, role, org, location, highlight?, description, tech:[]} (firstName/lastName, organization, startDate/endDate are mapped). Text fields take Lilia marks: **bold**, *italic*, $maths$, \\(…\\), Theorem~(thm:x) for a reference, @cite{key} for a citation.";

    [McpServerTool(Name = "add_block", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Add a block to a document (small, targeted inserts). Returns the new block id. The open editor shows the change at once; the reply gives the version to restore to undo it. An unknown type is refused. " + FieldGuide)]
    public Task<object> AddBlock(
        [Description("The document id.")] string documentId,
        [Description("Block type, e.g. paragraph, heading, equation.")] string type,
        [Description("JSON content for the type, e.g. {\"text\":\"…\"}, {\"text\":\"…\",\"level\":2}, {\"latex\":\"…\"}.")] string content,
        [Description("Insert after this block id; omit to append at the end.")] string? afterId = null) =>
        WriteAsync(documentId, "add_block", new() { ["type"] = type, ["content"] = content, ["afterId"] = afterId });

    [McpServerTool(Name = "edit_block", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Replace one block's content (and optionally its type) by id. Read it first with get_block. The content replaces the old one entirely: when changing the type, give the new type's fields. " + FieldGuide)]
    public Task<object> EditBlock(
        [Description("The document id.")] string documentId,
        [Description("The block id (from get_outline).")] string blockId,
        [Description("The new JSON content for the block.")] string content,
        [Description("Optional new block type.")] string? type = null) =>
        WriteAsync(documentId, "edit_block", new() { ["blockId"] = blockId, ["content"] = MapForType(content, type), ["type"] = type });

    /// <summary>
    /// MCP-50: a type change with content in the old shape — paragraph → equation sent
    /// {"text":"E=mc^2"} and printed "% Empty equation"; equation → paragraph sent {"latex":…} and
    /// printed nothing. The field moves across: text → latex for an equation, latex → $…$ text for a
    /// text block. Content already in the new shape is left alone.
    /// </summary>
    internal static string MapForType(string content, string? newType)
    {
        if (string.IsNullOrWhiteSpace(newType)) return content;
        System.Text.Json.Nodes.JsonObject? node;
        try { node = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject; }
        catch (JsonException) { return content; }
        if (node is null) return content;
        string? Str(string key) => node[key] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var type = newType.Trim().ToLowerInvariant();
        if (type == "equation" && string.IsNullOrWhiteSpace(Str("latex")) && Str("text") is { Length: > 0 } text)
        {
            node["latex"] = text.Trim().Trim('$').Trim();
            node.Remove("text");
        }
        else if (type is "paragraph" or "heading" or "abstract" or "blockquote" or "theorem" or "footnote"
                 && string.IsNullOrWhiteSpace(Str("text")) && Str("latex") is { Length: > 0 } latex)
        {
            node["text"] = $"${latex.Trim()}$";
            node.Remove("latex");
        }
        else return content;
        return node.ToJsonString();
    }

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

    private static readonly HashSet<string> TextSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "marginTop", "marginBottom", "marginLeft", "marginRight", "paragraphIndent", "customPreamble",
        "headerLeft", "headerCenter", "headerRight", "footerLeft", "footerCenter", "footerRight",
    };
    private static readonly HashSet<string> NumericSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "columns", "columnGap", "balancedColumns", "fontSize", "lineSpacing",
    };
    /** Every field set_document_settings takes, as Ask Lilia's tool names it. */
    private static readonly string[] SettingNames =
    [
        "paperSize", "orientation", "marginTop", "marginBottom", "marginLeft", "marginRight", "columns", "columnGap",
        "columnSeparator", "balancedColumns", "fontFamily", "fontSize", "lineSpacing", "paragraphIndent", "pageNumbering",
        "headingNumbering", "headerLeft", "headerCenter", "headerRight", "footerLeft", "footerCenter", "footerRight", "customPreamble",
    ];
    private static readonly JsonElement EmptyString = JsonDocument.Parse("\"\"").RootElement;

    /// <summary>What null puts back for a setting with a fixed set of values: the class default.</summary>
    private static readonly Dictionary<string, JsonElement> SettingDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["paperSize"] = JsonSerializer.SerializeToElement("a4"),
        ["orientation"] = JsonSerializer.SerializeToElement("portrait"),
        ["columns"] = JsonSerializer.SerializeToElement(1),
        ["columnGap"] = JsonSerializer.SerializeToElement(1.5),
        ["columnSeparator"] = JsonSerializer.SerializeToElement("none"),
        ["balancedColumns"] = JsonSerializer.SerializeToElement(false),
        ["fontFamily"] = JsonSerializer.SerializeToElement("serif"),
        ["fontSize"] = JsonSerializer.SerializeToElement(11),
        ["lineSpacing"] = JsonSerializer.SerializeToElement(1.0),
        ["pageNumbering"] = JsonSerializer.SerializeToElement("arabic"),
        ["headingNumbering"] = JsonSerializer.SerializeToElement("default"),
    };

    [McpServerTool(Name = "set_document_settings", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Change the page setup. Pass only the fields to change, as one JSON object: paperSize (a4|letter|legal|a5|executive|b5), orientation (portrait|landscape), marginTop/marginBottom/marginLeft/marginRight (e.g. 2.5cm, \"\" = class default), columns (1-3), columnGap (cm), columnSeparator (none|rule), balancedColumns, fontFamily (serif|sans-serif|monospace|charter|times|palatino|bookman), fontSize (10|11|12), lineSpacing (0.8-3), paragraphIndent (length or none), pageNumbering (arabic|roman|none), headingNumbering (none|1-5: the heading levels numbered, which the table of contents lists too; default), headerLeft/headerCenter/headerRight/footerLeft/footerCenter/footerRight (plain text), customPreamble (your own LaTeX added after Lilia's preamble, whose packages stay; it replaces the previous customPreamble, so read that first with get_document_settings). Invalid values change nothing and list the valid ones.")]
    public Task<object> SetDocumentSettings(
        [Description("The document id.")] string documentId,
        [Description("The fields to change, e.g. {\"fontSize\":12,\"marginLeft\":\"2cm\"}.")] JsonElement settings)
    {
        // Some clients send the object as a JSON string: it changed nothing ("Provide at least one setting").
        if (settings.ValueKind == JsonValueKind.String)
        {
            try { settings = JsonDocument.Parse(settings.GetString() ?? "").RootElement; }
            catch (JsonException) { return Task.FromResult<object>(new { error = "settings must be one JSON object, e.g. {\"fontSize\":12}.", category = "invalid" }); }
        }
        if (settings.ValueKind != JsonValueKind.Object)
            return Task.FromResult<object>(new { error = "settings must be one JSON object, e.g. {\"fontSize\":12}.", category = "invalid" });

        var args = new Dictionary<string, object?>();
        var unknown = new List<string>();
        foreach (var p in settings.EnumerateObject())
        {
            // A misspelt field was dropped without a word.
            var name = SettingNames.FirstOrDefault(n => n.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
            if (name is null) { unknown.Add(p.Name); continue; }
            args[name] = p.Value.ValueKind switch
            {
                // null on a text setting means "unset", as get_document_settings reports it; it was a
                // silent no-op (MCP-86). The empty string is what clears it.
                JsonValueKind.Null when TextSettings.Contains(name) => EmptyString,
                // A choice or a number set to null goes back to its default (MCP-113: it was ignored).
                JsonValueKind.Null when SettingDefaults.TryGetValue(name, out var fallback) => fallback,
                // headingNumbering: 2 failed with "could not be converted to System.String": the
                // choices that read as numbers are text fields.
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False when !NumericSettings.Contains(name) =>
                    JsonSerializer.SerializeToElement(p.Value.GetRawText()),
                _ => p.Value,
            };
        }
        if (unknown.Count > 0)
            return Task.FromResult<object>(new
            {
                error = $"Lilia has no setting {string.Join(", ", unknown.Select(u => $"'{u}'"))}. Settings: {string.Join(", ", SettingNames)}. Nothing was changed.",
                category = "invalid",
            });
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
            // MCP-02: the title given here stays; apply_lml would otherwise take the first heading.
            body = await WriteAsync(doc.Id.ToString(), "apply_lml", new() { ["lml"] = lml, ["title"] = doc.Title });
        return new { id = doc.Id, title = doc.Title, kind = category, body, url = $"{McpOAuth.PublicOrigin(config)}/d/{doc.Id}" };
    }

    /// <summary>Base64 for 10 MB, the largest file import_document takes.</summary>
    private const int ImportMaxChars = 14_000_000;

    [McpServerTool(Name = "import_document", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Import a file you have as a new Lilia document, in one call: .tex or .lml as text, .docx as base64 (up to 10 MB). Lilia's importer runs as for a dropped file (no review step), and the reply gives the new document's id and address; then get_outline, check_document. When the file is with the author rather than with you, use start_import.")]
    public async Task<object> ImportDocument(
        [Description("The file name, e.g. paper.tex, thesis.docx, notes.lml. Its extension gives the format unless format is set.")] string filename,
        [Description("The file: its text for .tex and .lml, base64 for .docx (a data: prefix is fine).")] string content,
        [Description("Optional: tex, docx or lml.")] string? format = null)
    {
        // MCP-114 (run 3b): an app holding the file could only hand the author a drop page, and an
        // unattended agent couldn't import at all.
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        if (string.IsNullOrWhiteSpace(content)) return new { error = "content is empty: give the file's text (.tex, .lml) or base64 (.docx).", category = "invalid" };
        if (content.Length > ImportMaxChars) return new { error = "The file is over 10 MB. Use start_import: the author drops it on Lilia's page.", category = "invalid" };
        var name = string.IsNullOrWhiteSpace(filename) ? "import" : Path.GetFileName(filename.Trim());
        var kind = ((format ?? Path.GetExtension(name)).Trim().TrimStart('.').ToLowerInvariant()) switch
        {
            "tex" or "latex" => "LATEX",
            "docx" or "word" => "DOCX",
            "lml" => "LML",
            _ => null,
        };
        if (kind is null) return new { error = "Lilia imports .tex, .docx and .lml files: give one of these extensions, or format.", category = "invalid" };
        if (kind == "DOCX")
        {
            var comma = content.IndexOf(',');
            if (content.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) content = content[(comma + 1)..];
            try { _ = Convert.FromBase64String(content.Trim()); }
            catch (FormatException) { return new { error = "A .docx goes as base64: this isn't valid base64.", category = "invalid" }; }
            content = content.Trim();
        }
        try
        {
            var result = await jobs.CreateImportJobFromBase64Async(UserId, new Lilia.Core.DTOs.ImportRequestDto(content, kind, name, SkipReview: true));
            if (result.Document is null)
                return new { error = $"Lilia could not import this file: {result.Job?.ErrorMessage ?? "the importer produced no document"}" };
            logger.LogInformation("[Mcp] import_document {File} ({Kind}) → {DocId} by {App}", name, kind, result.Document.Id, await AppNameAsync());
            return new
            {
                documentId = result.Document.Id,
                title = result.Document.Title,
                url = $"{McpOAuth.PublicOrigin(config)}/d/{result.Document.Id}",
                next = "Read it with get_outline, or check it compiles with check_document.",
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] import_document of {File} failed", name);
            return new { error = $"Lilia could not import this file: {(ex.Message.Length > 300 ? ex.Message[..300] : ex.Message)}" };
        }
    }

    [McpServerTool(Name = "start_import", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Import a file (.tex, .docx or .lml) the author has, without it passing through this conversation: returns a link to Lilia's drop page. Give the author the link; they drop the file there (signed in to Lilia). Then call import_status with the importId to get the new document. The link works for 30 minutes. When you have the file yourself, import_document does it in one call.")]
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
        // MCP-29 / 42: an unknown block type was stored and printed "% Unknown block type".
        if (args.TryGetValue("type", out var t) && t is string typeName && !string.IsNullOrWhiteSpace(typeName)
            && !Lilia.Core.Entities.BlockTypes.All.Contains(typeName.Trim(), StringComparer.OrdinalIgnoreCase))
            return new { error = $"Lilia has no block type '{typeName}'. Use one of: {string.Join(", ", AgentBlockTypes)}.", category = "invalid" };
        // A type Lilia has but the PDF can't print was accepted and printed nothing (MCP-42: cover).
        if (args.TryGetValue("type", out var t2) && t2 is string printed && string.Equals(printed.Trim(), "cover", StringComparison.OrdinalIgnoreCase))
            return new { error = "A cover block is for ePub books; the PDF has no place for it. Use set_title for the title page, and a figure for an image.", category = "invalid" };
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
