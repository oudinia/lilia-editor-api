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
///   author allowed edits on the consent page); on since 16 Oct.</item>
/// <item><b>Undo.</b> Before an app's first change to a document (again after
///   <see cref="UndoWindow"/> without changes), a version is saved; every reply gives its id, which
///   <c>restore_version</c> takes (17 Nov 2026: before, only the author could restore it, in History).</item>
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
    Lilia.Engines.IBibliographyService bibliography,
    IAssetService assets,
    ILogger<LiliaMcpWriteTools> logger,
    Lilia.Infrastructure.Data.LiliaDbContext? db = null)
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
    /// On since 16 Oct 2026 (it was off from 12 Oct, before Stytch had the scopes): documents:read and
    /// documents:write are Stytch RBAC scopes in the Test and Live projects, granted to stytch_user.
    /// </summary>
    internal static bool AppMayWrite(System.Security.Claims.ClaimsPrincipal? user, bool requireScope) =>
        !requireScope || Scopes(user) is not { } scopes || scopes.Contains(WriteScope);

    public static bool RequireWriteScope(IConfiguration config) => config.GetValue<bool>("Mcp:RequireWriteScope");

    internal const string ReconnectMessage =
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
    private const string FieldGuide = "Fields by type (others are ignored): paragraph {text}; heading {text, level 1-6, label?}; equation {latex, equationMode: display|inline, label?, numbered?}; theorem {text, theoremType: theorem|lemma|definition|proposition|corollary|remark|example|proof, title?, label?, numbered?}; list {items:[{text}], kind: bullet|numbered|description — for description items:[{text: term, description}]}; table {headers:[...], rows:[[...]], caption?, label?, columnAlign?, rowColors?: {odd: hex like #F2F2F2, even: hex} (odd = first body row)}; figure {src, caption?, label?, width 0-1, alt?} or TikZ {kind:\"tikz\", source, caption?, label?, alt?} (alt: a description for readers who can't see it; ACM prints it as \\Description); code {code, language?, caption?, label?}; blockquote {text, attribution?, variant?: verse}; abstract {text}; algorithm {caption?, label?, lines:[{kind, text}]} with kind statement|require|ensure|if|elsif|else|endif|for|forall|endfor|while|endwhile|repeat|until|return|comment, or {code} with \\STATE / \\IF{…} lines; callout {text, title?, variant?}; footnote {text}; embed {code} — raw LaTeX printed as written; bibliography {text} — BibTeX entries (@article{key, …}) or lines \"@cite[key] Author — Title\"; frontMatter / backMatter {subType: preface|foreword|acknowledgements|dedication|afterword|appendix, text, title?}; CV: personalInfo {name, headline, email, phones:[{number}], homepage, location}, cvSection {title}, cvEntry {period, role, org, location, highlight?, description, tech:[]} (firstName/lastName, organization, startDate/endDate are mapped). Text fields take Lilia marks: **bold**, *italic*, $maths$, \\(…\\), Theorem~(thm:x) for a reference, @cite{key} for a citation. LaTeX typed in text stays LaTeX (commands, {…} groups, \\ control spaces): to print a brace write \\{ \\}, a backslash \\textbackslash{} (MCP-32); %, &, #, _ print as typed.";

    [McpServerTool(Name = "add_block", Title = "Add a block", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Add a block to a document (small, targeted inserts). Returns the new block id. The open editor shows the change at once; to undo it, give the reply's undo.versionId to restore_version. An unknown type is refused. " + FieldGuide)]
    public Task<object> AddBlock(
        [Description("The document id.")] string documentId,
        [Description("Block type, e.g. paragraph, heading, equation.")] string type,
        [Description("JSON content for the type, e.g. {\"text\":\"…\"}, {\"text\":\"…\",\"level\":2}, {\"latex\":\"…\"}.")] string content,
        [Description("Insert after this block id; omit to append at the end.")] string? afterId = null) =>
        WriteAsync(documentId, "add_block", new() { ["type"] = type, ["content"] = content, ["afterId"] = afterId });

    [McpServerTool(Name = "edit_block", Title = "Edit a block", Destructive = false, Idempotent = true, OpenWorld = false)]
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

    [McpServerTool(Name = "remove_block", Title = "Remove a block", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete one block by id. To put it back, give the reply's undo.versionId to restore_version.")]
    public Task<object> RemoveBlock(
        [Description("The document id.")] string documentId,
        [Description("The block id to delete.")] string blockId) =>
        WriteAsync(documentId, "remove_block", new() { ["blockId"] = blockId });

    [McpServerTool(Name = "reorder_blocks", Title = "Reorder blocks", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Put the document's blocks in this exact order (every block id, from get_outline).")]
    public Task<object> ReorderBlocks(
        [Description("The document id.")] string documentId,
        [Description("All block ids in the new order.")] string[] blockIds) =>
        WriteAsync(documentId, "reorder_blocks", new() { ["blockIds"] = blockIds });

    /// <summary>
    /// A reference from its DOI, arXiv id or ISBN (the connector test, 17 Nov 2026): the editor looks references up, but
    /// through the connector an assistant had to type the BibTeX itself, and could get the details wrong. The entry goes
    /// in the document's bibliography block (made at the end when there is none), under a key no other entry uses.
    /// </summary>
    [McpServerTool(Name = "add_reference", Title = "Add a reference", Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Add a reference to the document's bibliography from its DOI, arXiv id or ISBN (give one): Lilia looks it up (Crossref, arXiv, Open Library) and writes the entry, so its authors, title, venue and year are the real ones. Returns the cite key and what to write in the text to cite it (@cite{key}). A DOI the document already holds is not added twice: its key is returned. The bibliography block is made at the end when there is none.")]
    public async Task<object> AddReference(
        [Description("The document id.")] string documentId,
        [Description("A DOI, e.g. 10.1145/321879.321884 (a https://doi.org/ link is fine).")] string? doi = null,
        [Description("An arXiv id, e.g. 2106.09685 or arXiv:2106.09685.")] string? arxiv = null,
        [Description("An ISBN, for a book.")] string? isbn = null,
        [Description("Optional cite key to use instead of Lilia's (author and year), e.g. tarjan1975.")] string? citeKey = null)
    {
        var given = new[] { doi, arxiv, isbn }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (given != 1) return new { error = "Give exactly one of doi, arxiv or isbn.", category = "invalid" };
        var doc = Guid.TryParse(documentId?.Trim(), out var id) ? await documents.GetDocumentAsync(id, UserId) : null;
        if (doc is null) return new { error = $"No document '{documentId}' that you can open. Call list_documents for ids." };

        var cleanDoi = string.IsNullOrWhiteSpace(doi) ? null
            : System.Text.RegularExpressions.Regex.Replace(doi.Trim(), @"^(https?://(dx\.)?doi\.org/|doi:)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var cleanArxiv = string.IsNullOrWhiteSpace(arxiv) ? null
            : System.Text.RegularExpressions.Regex.Replace(arxiv.Trim(), @"^(https?://arxiv\.org/(abs|pdf)/|arxiv:)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var biblio = doc.Blocks?.FirstOrDefault(b => string.Equals(b.Type, "bibliography", StringComparison.OrdinalIgnoreCase));
        var text = biblio is not null && biblio.Content.ValueKind == JsonValueKind.Object && biblio.Content.TryGetProperty("text", out var t)
            ? t.GetString() ?? "" : "";

        // Already there: the same DOI or arXiv id in the block.
        var known = cleanDoi ?? cleanArxiv;
        if (known is not null && text.Contains(known, StringComparison.OrdinalIgnoreCase)
            && ExistingKeyFor(text, known) is { } existing)
            return new { citeKey = existing, cite = $"@cite{{{existing}}}", alreadyThere = true, note = "The bibliography already holds this reference." };

        Lilia.Core.DTOs.DoiLookupResultDto? found;
        try
        {
            found = cleanDoi is not null ? await bibliography.LookupDoiAsync(cleanDoi)
                : cleanArxiv is not null ? await bibliography.LookupArxivAsync(cleanArxiv)
                : await bibliography.LookupIsbnAsync(isbn!.Trim());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] add_reference lookup failed");
            found = null;
        }
        if (found is null)
            return new { error = $"Lilia found no work for that {(cleanDoi is not null ? "DOI" : cleanArxiv is not null ? "arXiv id" : "ISBN")}. Check it, or add the entry yourself (BibTeX in the bibliography block).", category = "not-found" };

        var taken = TakenKeys(text, doc);
        var key = UniqueKey(string.IsNullOrWhiteSpace(citeKey) ? found.CiteKey : citeKey.Trim(), taken);
        var data = found.Data;
        if (cleanArxiv is not null && !(data.ValueKind == JsonValueKind.Object && data.TryGetProperty("eprint", out _)))
        {
            var withId = System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!.AsObject();
            withId["eprint"] = cleanArxiv;
            withId["archivePrefix"] = "arXiv";
            data = JsonSerializer.SerializeToElement(withId);
        }
        var bibtex = Lilia.Engines.BibliographyService.ToBibTex(found.EntryType, key, data);

        var newText = string.IsNullOrWhiteSpace(text) ? bibtex : text.TrimEnd() + "\n" + bibtex;
        var content = JsonSerializer.Serialize(new { text = newText });
        var written = biblio is not null
            ? await WriteAsync(documentId!, "edit_block", new() { ["blockId"] = biblio.Id.ToString(), ["content"] = content })
            : await WriteAsync(documentId!, "add_block", new() { ["type"] = "bibliography", ["content"] = content });

        string? F(string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var v) ? v.ToString() : null;
        return new
        {
            citeKey = key,
            cite = $"@cite{{{key}}}",
            reference = new { type = found.EntryType, title = F("title"), authors = F("author"), year = F("year"), venue = F("journal") ?? F("booktitle") ?? F("publisher") },
            bibtex,
            written,
        };
    }

    private static readonly System.Text.RegularExpressions.Regex BibKey = new(@"@\w+\s*\{\s*([^,\s]+)\s*,", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The key of the BibTeX entry in <paramref name="text"/> that holds <paramref name="id"/> (a DOI or arXiv id).</summary>
    internal static string? ExistingKeyFor(string text, string id)
    {
        string? key = null;
        foreach (System.Text.RegularExpressions.Match m in BibKey.Matches(text))
        {
            var end = BibKey.Match(text, m.Index + m.Length) is { Success: true } next ? next.Index : text.Length;
            if (text.AsSpan(m.Index, end - m.Index).Contains(id.AsSpan(), StringComparison.OrdinalIgnoreCase)) key = m.Groups[1].Value;
        }
        return key;
    }

    private static HashSet<string> TakenKeys(string text, Lilia.Core.DTOs.DocumentDto doc)
    {
        var keys = BibKey.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    /// <summary><paramref name="key"/>, or with a, b, c… after it when the document already uses it.</summary>
    internal static string UniqueKey(string key, IReadOnlySet<string> taken)
    {
        key = System.Text.RegularExpressions.Regex.Replace(key, @"[^A-Za-z0-9:_\-]", "");
        if (key.Length == 0) key = "ref";
        if (!taken.Contains(key)) return key;
        for (var c = 'a'; c <= 'z'; c++) if (!taken.Contains(key + c)) return key + c;
        return key + Guid.NewGuid().ToString("N")[..6];
    }

    [McpServerTool(Name = "set_title", Title = "Set the title", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set the document's title (also its name and the LaTeX \\title), and optionally the authors, date, keywords and, for ACM, the rights and CCS lines. What you leave out stays as it is. Each author's affiliation, country and email go in authors (ACM, IEEE, Elsevier and the other venues print them in their own way); author is the plain alternative, one line each (name first).")]
    public Task<object> SetTitle(
        [Description("The document id.")] string documentId,
        [Description("The title.")] string title,
        [Description("Author name(s), plain. A new line starts the next line under the name (affiliation, then country, then email). Use authors instead for several authors with affiliations.")] string? author = null,
        [Description("Date, e.g. 'October 2026' or '\\\\today'.")] string? date = null,
        [Description("Keywords, comma-separated, e.g. 'union-find, amortised analysis' (IEEE, ACM and other venues ask for them).")] string? keywords = null,
        [Description("The authors in order, each with name and optionally affiliation, city, country and email. Replaces author.")] TitleAuthorInput[]? authors = null,
        [Description("ACM only: the rights lines from ACM's eRights email after acceptance (\\setcopyright{…}, \\acmDOI{…}, \\acmConference[…]{…}{…}{…}, \\acmISBN{…}, \\acmYear{…}…), one a line. Without them the PDF keeps acmart's placeholders. Other lines are left out.")] string? acmRights = null,
        [Description("ACM only: the CCS concepts from ACM's tool (dl.acm.org/ccs): the CCSXML block and the \\ccsdesc[500]{…} lines.")] string? acmCcs = null) =>
        WriteAsync(documentId, "set_title", new()
        {
            ["title"] = title,
            ["author"] = authors is { Length: > 0 } ? AuthorText(authors) : author is null ? null : AuthorLines(author),
            ["date"] = date,
            ["keywords"] = keywords,
            ["acmRights"] = acmRights,
            ["acmCcs"] = acmCcs,
        });

    /// <summary>One author for set_title (launch-bot #129, 17 Nov 2026: the connector had no way to give an affiliation).</summary>
    public sealed record TitleAuthorInput(
        [property: Description("The author's name.")] string Name,
        [property: Description("Institution or company, e.g. 'Lilia Editor' or 'Dept. of CS, University of X'.")] string? Affiliation = null,
        [property: Description("City, optional.")] string? City = null,
        [property: Description("Country (ACM asks for one).")] string? Country = null,
        [property: Description("Email (ACM asks for one).")] string? Email = null);

    /// <summary>
    /// The title block's author string the title editor writes: each author's name, then a line each for
    /// affiliation, city, country and email (<c>\\</c>), authors joined by <c>\and</c>. The venues read it back
    /// (PublisherClasses.FrontMatter: the last line before the email is the country).
    /// </summary>
    internal static string AuthorText(IEnumerable<TitleAuthorInput> authors) =>
        string.Join(" \\and ", authors.Where(a => !string.IsNullOrWhiteSpace(a.Name)).Select(a =>
            string.Join(@"\\", new[] { a.Name, a.Affiliation, a.City, a.Country, a.Email }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => OneLine(x!)))));

    /// <summary>A plain author with new lines: each line a line under the name (Grok's "name\naffiliation\n…" was joined into one name).</summary>
    internal static string AuthorLines(string author) =>
        author.Contains('\n')
            ? string.Join(@"\\", author.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : author;

    private static string OneLine(string s) => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s*\r?\n\s*", ", ");

    // Paste → LaTeX, phase 3 (11 Nov 2026): move a paper over section by section, each one checked.
    [McpServerTool(Name = "paste_content", Title = "Paste text into a document", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Paste any text into a document where asked: LaTeX (a fragment or a whole paper), Markdown or plain text, or Lilia text. Lilia reads it as its Import → Paste does, adds the blocks after afterId (or at the end), checks that the document still compiles, and returns the new block ids, the verdict (problems on the pasted blocks) and the LaTeX of what was pasted. The way to move a paper over section by section: paste a section, read the verdict, fix with edit_block, paste the next after the last block added. The rest of the document and its settings are left as they are.")]
    public Task<object> PasteContent(
        [Description("The document id.")] string documentId,
        [Description("The text to paste.")] string content,
        [Description("AUTO (default), LATEX, MARKDOWN or LML.")] string? format = null,
        [Description("Insert after this block id (from get_outline or a previous paste); omit to add at the end.")] string? afterId = null) =>
        WriteAsync(documentId, "paste_content", new() { ["content"] = content, ["format"] = format, ["afterId"] = afterId });

    [McpServerTool(Name = "apply_lml", Title = "Rewrite the whole document", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Replace the WHOLE document body with LML, Lilia's block markup, in one step (for rewrites; prefer edit_block for small changes). Academic: @abstract, @heading[level=1], @paragraph, @equation[mode=display], @theorem[…], @bibliography; bodies indented 2 spaces. To put the old text back, give the reply's undo.versionId to restore_version.")]
    public Task<object> ApplyLml(
        [Description("The document id.")] string documentId,
        [Description("The full LML source for the body.")] string lml,
        [Description("Optional title, used only when the document has no title yet; to rename a document, use set_title.")] string? title = null) =>
        WriteAsync(documentId, "apply_lml", new() { ["lml"] = lml, ["title"] = title });

    [McpServerTool(Name = "set_venue", Title = "Format for a venue", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Format the document for a venue in one step: the publisher's class, options, front matter and reference style (the editor's Venue choice). Only the document's owner can choose it. Profile ids: acm-sigconf-review, acm-sigconf-final, acm-journal-review, acm-journal-final, ieee-conference, ieee-journal, elsevier-preprint, elsevier-review, elsevier-final, lncs-submission, lncs-camera-ready, aps-review, aps-final, aip-review, aip-final, ams-journal, iop-review, iop-final, siam-review, siam-final; \"\" removes the venue. Run check_document afterwards: a venue's class can need content it expects (an abstract, keywords).")]
    public Task<object> SetVenue(
        [Description("The document id.")] string documentId,
        [Description("The publisher profile id, e.g. ieee-conference; \"\" removes the venue.")] string profile) =>
        WriteAsync(documentId, "set_venue", new() { ["profile"] = profile });

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
        "language",
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
        ["language"] = JsonSerializer.SerializeToElement("auto"),
    };

    [McpServerTool(Name = "set_document_settings", Title = "Change the page setup", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Change the page setup. Pass only the fields to change, as one JSON object: paperSize (a4|letter|legal|a5|executive|b5), orientation (portrait|landscape), marginTop/marginBottom/marginLeft/marginRight (e.g. 2.5cm, \"\" = class default), columns (1-3), columnGap (cm), columnSeparator (none|rule), balancedColumns, fontFamily (serif|sans-serif|monospace|charter|times|palatino|bookman), fontSize (10|11|12), lineSpacing (0.8-3), paragraphIndent (length or none), pageNumbering (arabic|roman|none), headingNumbering (none|1-5: the heading levels numbered, which the table of contents lists too; default), headerLeft/headerCenter/headerRight/footerLeft/footerCenter/footerRight (plain text), language (auto|en|en-GB|fr|de|es|it|pt|nl|pl|sv|tr|ru|el|ar|he|zh|ja|ko: the PDF's language, auto reads it from the text), customPreamble (your own LaTeX added after Lilia's preamble, whose packages stay; it replaces the previous customPreamble, so read that first with get_document_settings). Invalid values change nothing and list the valid ones.")]
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

    [McpServerTool(Name = "create_document", Title = "Create a document", Destructive = false, Idempotent = false, OpenWorld = false)]
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
        Lilia.Core.DTOs.DocumentDto doc;
        try
        {
            doc = await documents.CreateDocumentAsync(UserId, new Lilia.Api.Models.Documents.CreateDocumentDto
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim(),
                DocumentCategory = category,
                DocumentClass = category,
            });
        }
        catch (QuotaExceededException limit)
        {
            // The author's plan, not the request: say so, so the app doesn't retry or rephrase.
            return new { error = limit.Message, category = "plan-limit" };
        }
        object? body = null;
        if (!string.IsNullOrWhiteSpace(lml))
            // MCP-02: the title given here stays; apply_lml would otherwise take the first heading.
            body = await WriteAsync(doc.Id.ToString(), "apply_lml", new() { ["lml"] = lml, ["title"] = doc.Title });
        return new { id = doc.Id, title = doc.Title, kind = category, body, url = $"{McpOAuth.PublicOrigin(config)}/d/{doc.Id}" };
    }

    /// <summary>Base64 for 10 MB, the largest file import_document takes.</summary>
    private const int ImportMaxChars = 14_000_000;

    /// <summary>What a project zip held, for import_document's reply: the main file used, the other candidates, and the
    /// files kept with the document but not used by Lilia's PDF.</summary>
    private static object? ProjectReport(string base64)
    {
        try
        {
            var extracted = new Lilia.Import.Services.LatexProjectExtractor().Extract(Convert.FromBase64String(base64));
            var unused = extracted.Files
                .Where(f => f.Kind is Lilia.Import.Services.LatexProjectFileKinds.Style
                    || f.Path.EndsWith(".eps", StringComparison.OrdinalIgnoreCase) || f.Path.EndsWith(".ps", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Path).ToList();
            return new
            {
                mainFile = extracted.MainFileName,
                otherMainFiles = extracted.MainCandidates.Skip(1).ToList(),
                notUsedByThePdf = unused,
                note = (extracted.MainCandidates.Count > 1
                        ? $"The zip has {extracted.MainCandidates.Count} main .tex files; Lilia used {extracted.MainFileName} (a main.tex is used first: rename the one you want to main.tex and import again). "
                        : "")
                    + (unused.Count > 0
                        ? "Kept with the document but not used by Lilia's PDF: the project's own class and style files (Lilia compiles with TeX Live's) and EPS images (convert them to PDF or PNG)."
                        : ""),
            };
        }
        catch (Exception) { return null; }
    }

    [McpServerTool(Name = "import_document", Title = "Import a file", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Import a file you have as a new Lilia document, in one call: .tex, .md or .lml as text, .docx or an Overleaf project .zip as base64 (up to 10 MB; a zip's \\input files are read as one document, its images and .bib kept with it). Lilia's importer runs as for a dropped file (no review step), and the reply gives the new document's id and address; then get_outline, check_document. When the file is with the author rather than with you, use start_import.")]
    public async Task<object> ImportDocument(
        [Description("The file name, e.g. paper.tex, thesis.docx, notes.md, notes.lml. Its extension gives the format unless format is set.")] string filename,
        [Description("The file: its text for .tex, .md and .lml, base64 for .docx (a data: prefix is fine).")] string content,
        [Description("Optional: tex, docx, md or lml.")] string? format = null)
    {
        // MCP-114 (run 3b): an app holding the file could only hand the author a drop page, and an
        // unattended agent couldn't import at all.
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        if (string.IsNullOrWhiteSpace(content)) return new { error = "content is empty: give the file's text (.tex, .md, .lml) or base64 (.docx).", category = "invalid" };
        if (content.Length > ImportMaxChars) return new { error = "The file is over 10 MB. Use start_import: the author drops it on Lilia's page.", category = "invalid" };
        var name = string.IsNullOrWhiteSpace(filename) ? "import" : Path.GetFileName(filename.Trim());
        var kind = ((format ?? Path.GetExtension(name)).Trim().TrimStart('.').ToLowerInvariant()) switch
        {
            "tex" or "latex" => "LATEX",
            // An Overleaf project as it downloads: the zip, base64. Its .tex files are read as one, its images and .bib
            // stored with the document (the launch bot's Overleaf round trip, 18 Nov 2026).
            "zip" => "LATEX",
            "docx" or "word" => "DOCX",
            "lml" => "LML",
            "md" or "markdown" or "txt" => "MARKDOWN",
            _ => null,
        };
        if (kind is null) return new { error = "Lilia imports .tex, .zip (an Overleaf project, base64), .docx, .md and .lml files: give one of these extensions, or format.", category = "invalid" };
        // A text file (.tex, .md, .lml) sent as a base64 data URI, as some apps send every file: imported as its literal
        // "data:…;base64,…" text (the launch bot's #226 retest, 19 Nov 2026). Its text, decoded.
        if (kind is "LATEX" or "MARKDOWN" or "LML" && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && content.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            && content.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) is var b64 and > 0 and < 200)
        {
            try { content = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(content[(b64 + 8)..].Trim())); }
            catch (FormatException) { return new { error = "This data URI's base64 isn't valid: send the file's text, or valid base64.", category = "invalid" }; }
        }
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var comma = content.IndexOf(',');
            if (content.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) content = content[(comma + 1)..];
            content = content.Trim();
        }
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
            // MCP-123: a .tex with \write18 or \input{|…} was imported without a word (the preamble's
            // dropped, the body's kept as text), though every other write tool refuses it (MCP-45) and
            // the compile does. The file is still imported, so the author can fix it in the editor; the
            // reply says what the compile will refuse, in the guard's own words.
            var project = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ProjectReport(content) : null;
            var unsafeSource = kind == "DOCX" || project is not null ? null : Lilia.Engines.TexSafety.TexSourceGuard.Violation(content);
            return new
            {
                documentId = result.Document.Id,
                title = result.Document.Title,
                url = $"{McpOAuth.PublicOrigin(config)}/d/{result.Document.Id}",
                // A project zip: which .tex was the main one, the others that could have been, and the files the PDF won't
                // use (a project's own .cls/.sty/.bst, EPS images). Both were silent (launch-bot #241, #242, 18 Nov 2026).
                project,
                warning = unsafeSource is null ? null
                    : $"{unsafeSource} The document was imported, but it will not compile until that is removed; anything the importer dropped is gone.",
                next = unsafeSource is null
                    ? "Read it with get_outline, or check it compiles with check_document."
                    : "Find and remove it (get_outline, then edit the block), then check_document.",
            };
        }
        catch (QuotaExceededException limit)
        {
            return new { error = limit.Message, category = "plan-limit" };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] import_document of {File} failed", name);
            return new { error = $"Lilia could not import this file: {(ex.Message.Length > 300 ? ex.Message[..300] : ex.Message)}" };
        }
    }

    [McpServerTool(Name = "start_import", Title = "Start a file import", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Import a file (.tex, an Overleaf project .zip, .docx, .md or .lml) the author has, without it passing through this conversation: returns a link to Lilia's drop page. Give the author the link; they drop the file there (signed in to Lilia). Then call import_status with the importId to get the new document. The link works for 30 minutes. When you have the file yourself, import_document does it in one call.")]
    public async Task<object> StartImport()
    {
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        var (token, ticket) = transfers.IssueImport(UserId, await AppNameAsync());
        return new
        {
            importId = token,
            url = $"{McpOAuth.PublicOrigin(config)}/connect/import/{token}",
            accepts = new[] { ".tex", ".zip", ".docx", ".md", ".lml" },
            expiresAt = ticket.ExpiresAt,
            note = "Give the author this link to drop the file. When they say it's done, call import_status with the importId.",
        };
    }

    [McpServerTool(Name = "import_status", Title = "Check an import", ReadOnly = true, Idempotent = true, OpenWorld = false)]
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

    /// <summary>The editor's image upload limit (AssetsController): 10 MB.</summary>
    internal const int FigureImageMaxBytes = 10 * 1024 * 1024;

    // The connector test (17 Nov 2026, item C): a figure could only be TikZ. An image link printed "Missing figure" and
    // there was no upload, so a plot the assistant made had no way in. The file comes in the call; nothing is fetched.
    [McpServerTool(Name = "add_figure_image", Title = "Put an image in a figure", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Put an image you have in a figure, from the file itself (a plot or diagram you made, a picture the author gave you): PNG, JPEG or PDF (its first page prints), up to 10 MB, sent as base64. Lilia stores it as the editor's upload does and adds a figure block after afterId (or at the end); with blockId it puts the image in that figure instead, keeping its caption, label and width unless you give new ones. Lilia never fetches a link: send the file. SVG, GIF, WebP and other formats are refused: convert to PNG, or to PDF for a vector drawing. Then check_document.")]
    public async Task<object> AddFigureImage(
        [Description("The document id.")] string documentId,
        [Description("The file name, e.g. results-plot.png. The file's own bytes decide its type.")] string filename,
        [Description("The file as base64 (a data: prefix is fine).")] string contentBase64,
        [Description("Optional caption. Lilia marks work in it, e.g. @cite{key}.")] string? caption = null,
        [Description("Optional label to refer to it, e.g. fig:results (in text: Figure~(fig:results)).")] string? label = null,
        [Description("Optional width as a fraction of the line, 0.05 to 1 (default 0.8).")] double? width = null,
        [Description("Optional short description for readers who can't see the image (ACM asks for one). Default: the file name.")] string? alt = null,
        [Description("Add the new figure after this block id; omit to add it at the end.")] string? afterId = null,
        [Description("Put the image in this existing figure block (from get_outline) instead of adding one.")] string? blockId = null)
    {
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        if (UnsafeIn(new object?[] { caption, label, alt }) is { } refused) return new { error = refused, category = "refused" };
        var raw = (contentBase64 ?? "").Trim();
        var comma = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) raw = raw[(comma + 1)..].Trim();
        if (raw.Length == 0)
            return new { error = "contentBase64 is empty: send the image file itself, as base64. Lilia does not fetch links.", category = "invalid" };
        if (raw.Length > ImportMaxChars) return new { error = TooLarge(raw.Length / 4L * 3), category = "invalid" };
        byte[] bytes;
        try { bytes = Convert.FromBase64String(raw); }
        catch (FormatException) { return new { error = "contentBase64 isn't valid base64: send the image file's bytes as base64. Lilia does not fetch links.", category = "invalid" }; }
        if (bytes.Length > FigureImageMaxBytes) return new { error = TooLarge(bytes.Length), category = "invalid" };
        var (mime, extension, other) = FigureImageKind(bytes);
        if (mime is null)
            return new
            {
                error = $"Lilia puts PNG, JPEG or PDF images in figures{(other is null ? "" : $", and this file is {other}")}. Convert it to PNG (or PDF for a vector drawing) and send it again.",
                category = "invalid",
            };
        if (width is { } w && (double.IsNaN(w) || w < 0.05 || w > 1))
            return new { error = "width is a fraction of the line, from 0.05 to 1 (e.g. 0.6).", category = "invalid" };

        var (doc, refusal) = await WritableAsync(documentId);
        if (doc is null) return refusal!;

        System.Text.Json.Nodes.JsonObject content;
        Lilia.Core.DTOs.BlockDto? figure = null;
        if (!string.IsNullOrWhiteSpace(blockId))
        {
            figure = Guid.TryParse(blockId.Trim(), out var bid) ? doc.Blocks?.FirstOrDefault(b => b.Id == bid) : null;
            if (figure is null) return new { error = $"No block '{blockId}' in this document. Call get_outline for ids.", category = "invalid" };
            if (figure.Type is not ("figure" or "image"))
                return new { error = $"Block {blockId} is a {figure.Type}, not a figure. Omit blockId to add a figure (after it: afterId).", category = "invalid" };
            content = System.Text.Json.Nodes.JsonNode.Parse(figure.Content.GetRawText()) as System.Text.Json.Nodes.JsonObject ?? new();
            if (content["subfigures"] is System.Text.Json.Nodes.JsonArray { Count: > 0 })
                return new { error = "This figure holds subfigures, each with its own image, and prints those, not one image. Add a new figure instead (afterId).", category = "invalid" };
            // A TikZ figure becomes an image figure: the drawing would print instead of the image.
            content.Remove("kind");
            content.Remove("source");
        }
        else content = new();

        var name = FigureImageFileName(filename, extension!);
        Lilia.Core.DTOs.AssetDto asset;
        try
        {
            await using var stream = new MemoryStream(bytes);
            asset = await assets.UploadAssetAsync(doc.Id, UserId, name, mime, bytes.Length, stream);
        }
        catch (AssetTooLargeException tooLarge)
        {
            return new { error = tooLarge.Message, category = "invalid" };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] add_figure_image could not store {File} for {DocId}", name, doc.Id);
            return new { error = $"Lilia could not store the image: {ex.Message}" };
        }

        // The fields the editor's upload sets (src, assetId, alt), and what the call gives.
        content["src"] = asset.Url;
        content["assetId"] = asset.Id.ToString();
        if (!string.IsNullOrWhiteSpace(alt)) content["alt"] = alt.Trim();
        else if (!(content["alt"] is System.Text.Json.Nodes.JsonValue kept && kept.TryGetValue<string>(out var keptAlt) && !string.IsNullOrWhiteSpace(keptAlt)))
            content["alt"] = name;
        if (caption is not null) content["caption"] = caption;
        if (label is not null) content["label"] = label;
        if (width is not null) content["width"] = width;

        var args = figure is null
            ? new Dictionary<string, object?> { ["type"] = "figure", ["content"] = content.ToJsonString(), ["afterId"] = afterId }
            : new Dictionary<string, object?> { ["blockId"] = figure.Id.ToString(), ["content"] = content.ToJsonString() };
        var image = new
        {
            assetId = asset.Id,
            url = asset.Url,
            file = Lilia.Engines.FigureFile.Name(asset.Url),
            contentType = asset.FileType,
            bytes = asset.FileSize,
            widthPx = asset.Width,
            heightPx = asset.Height,
        };
        var (reply, changedAny) = await RunAsync(doc, figure is null ? "add_block" : "edit_block", args, image);
        if (!changedAny)
        {
            // The block wasn't written: the image is nobody's.
            try { await assets.DeleteAssetAsync(doc.Id, asset.Id); }
            catch (Exception ex) { logger.LogWarning(ex, "[Mcp] add_figure_image could not remove unused asset {AssetId}", asset.Id); }
        }
        return reply;
    }

    private static string TooLarge(long bytes) =>
        $"The image is {bytes / 1048576.0:0.#} MB; Lilia takes images up to {FigureImageMaxBytes / 1048576} MB, as the editor does. "
        + "Make it smaller (fewer pixels, or JPEG for a photo) and send it again.";

    /// <summary>
    /// What the file is, from its first bytes: the MIME type and extension of a PNG, JPEG or PDF, which LaTeX includes;
    /// otherwise null and, when known, what it is instead ("an SVG drawing"), for the refusal.
    /// </summary>
    internal static (string? Mime, string? Extension, string? Other) FigureImageKind(byte[] bytes)
    {
        bool StartsWith(int at, params byte[] sig) => bytes.Length >= at + sig.Length && bytes.AsSpan(at, sig.Length).SequenceEqual(sig);
        if (StartsWith(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return ("image/png", ".png", null);
        if (StartsWith(0, 0xFF, 0xD8, 0xFF)) return ("image/jpeg", ".jpg", null);
        if (StartsWith(0, "%PDF-"u8.ToArray())) return ("application/pdf", ".pdf", null);
        if (StartsWith(0, "GIF8"u8.ToArray())) return (null, null, "a GIF");
        if (StartsWith(0, "RIFF"u8.ToArray()) && StartsWith(8, "WEBP"u8.ToArray())) return (null, null, "a WebP image");
        if (StartsWith(0, "BM"u8.ToArray())) return (null, null, "a BMP image");
        if (StartsWith(0, 0x49, 0x49, 0x2A, 0x00) || StartsWith(0, 0x4D, 0x4D, 0x00, 0x2A)) return (null, null, "a TIFF image");
        if (StartsWith(4, "ftyp"u8.ToArray())) return (null, null, "a HEIC or AVIF photo");
        if (StartsWith(0, "%!PS"u8.ToArray())) return (null, null, "an EPS file");
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024)).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (head.Contains("<svg", StringComparison.OrdinalIgnoreCase)) return (null, null, "an SVG drawing");
        return (null, null, null);
    }

    /// <summary>The name the asset keeps: the one given, safe, with the extension of what the file really is.</summary>
    internal static string FigureImageFileName(string? filename, string extension)
    {
        var name = Path.GetFileName((filename ?? "").Trim().Replace('\\', '/'));
        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(stem)) stem = "figure";
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext == extension || (extension == ".jpg" && ext == ".jpeg") ? stem + ext : stem + extension;
    }

    /// <summary>
    /// Undo from the assistant (the connector test, 17 Nov 2026, item D): every write's reply gave an undo point, but
    /// only the author could restore it, in History. This restores through the same <see cref="IVersionService"/> as
    /// History's Restore, for someone who may edit. The document as it is is saved first, always (not the burst's undo
    /// point: restoring that very version would leave nothing to go back to), so the restore can be undone the same way.
    /// The next edit starts a new undo point, at the restored text.
    /// </summary>
    [McpServerTool(Name = "restore_version", Title = "Restore a saved version", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Put a document back to one of its saved versions, as History's Restore does: its blocks, bibliography and page setup become the version's. The version id comes from list_versions, or is the undo.versionId an edit's reply gave (restoring it undoes every change made since). The document as it is now is saved first and the reply gives that version's id: restore it the same way to undo the restore. Only someone who may edit the document can restore it.")]
    public async Task<object> RestoreVersion(
        [Description("The document id.")] string documentId,
        [Description("The version id, from list_versions or an edit's undo.versionId.")] string versionId)
    {
        if (!AppMayWrite(User, RequireWriteScope(config))) return new { error = ReconnectMessage };
        var (doc, refusal) = await WritableAsync(documentId);
        if (doc is null) return refusal!;
        if (!Guid.TryParse(versionId?.Trim(), out var vid) || await versions.GetVersionAsync(doc.Id, vid) is not { } target)
            return new { error = $"No version '{versionId}' in this document. Call list_versions for ids." };

        var app = await AppNameAsync();
        Guid before;
        try
        {
            before = (await versions.CreateVersionAsync(doc.Id, UserId,
                new Lilia.Core.DTOs.CreateVersionDto($"Before {app} restored v{target.VersionNumber}"))).Id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] restore_version could not save {DocId} first", doc.Id);
            return new { error = "Lilia could not save the document as it is before restoring, so nothing was changed. Try again." };
        }
        Lilia.Core.DTOs.DocumentDto? restored;
        try
        {
            restored = await versions.RestoreVersionAsync(doc.Id, vid, UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] restore_version of {VersionId} failed on {DocId}", vid, doc.Id);
            return new { error = $"Lilia could not restore this version: {ex.Message}" };
        }
        if (restored is null) return new { error = $"No version '{versionId}' in this document. Call list_versions for ids." };
        // The burst's undo point was the text before the restore; the next edit saves a new one.
        cache.Remove(UndoKey(doc.Id));
        logger.LogInformation("[Mcp] restore_version {DocId} to v{Number} by {App}", doc.Id, target.VersionNumber, app);

        // The open editor reloads, names the app and offers Undo (to the version saved just before).
        await hub.Clients.Group($"doc-{doc.Id}").SendAsync("DocumentChangedExternally", new
        {
            source = "mcp",
            app,
            tool = "restore_version",
            blockIds = Array.Empty<string>(),
            undoVersionId = before,
            at = DateTime.UtcNow,
        });
        return new
        {
            restored = new { versionId = target.Id, number = target.VersionNumber, name = target.Name, savedAt = target.CreatedAt },
            blockCount = restored.Blocks?.Count ?? 0,
            undo = new { versionId = before, note = "The document as it was before this restore. To undo the restore, call restore_version with this versionId." },
            url = $"{McpOAuth.PublicOrigin(config)}/d/{doc.Id}",
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
        var (doc, refusal) = await WritableAsync(documentId);
        if (doc is null) return refusal!;
        return (await RunAsync(doc, tool, args)).Reply;
    }

    /// <summary>The document, when the author may change it through an app; else why not.</summary>
    private async Task<(Lilia.Core.DTOs.DocumentDto? Doc, object? Refusal)> WritableAsync(string? documentId)
    {
        var doc = Guid.TryParse(documentId?.Trim(), out var id) ? await documents.GetDocumentAsync(id, UserId) : null;
        if (doc is null) return (null, new { error = $"No document '{documentId}' that you can open. Call list_documents for ids." });
        if (doc.PlanLocked)
            return (null, new { error = "This document is read-only on its owner's plan: the plan holds fewer documents than they own. The owner can choose it as one to keep editing, move another to the Trash, or upgrade.", category = "refused" });
        if (!AskLiliaService.MayEditWithAi(true, doc))
            return (null, new { error = "You can only read this document (view-only access). Its owner can give you edit access." });
        return (doc, null);
    }

    /// <summary>Run Ask Lilia's own tool on a document the author may change; the reply, and whether anything changed.</summary>
    private async Task<(object Reply, bool Changed)> RunAsync(Lilia.Core.DTOs.DocumentDto doc, string tool, Dictionary<string, object?> args, object? image = null)
    {
        var app = await AppNameAsync();
        // A rewrite of the whole body or a paste saves the text as it is just before it, not the burst's first version
        // (#124, 17 Nov 2026: undoing an apply_lml went back past every earlier edit too). And the burst starts again
        // after it (below): undoing an edit made after ten pastes went back to the empty document, 610 blocks gone
        // (launch-bot #149).
        var bulk = tool is "apply_lml" or "paste_content";
        var undo = bulk ? await FreshUndoPointAsync(doc.Id, app, tool == "apply_lml" ? "rewrite" : "paste") : await UndoPointAsync(doc.Id, app);
        var changed = new List<string>();
        var meta = false;
        object? result = null;
        // Two calls on one document at once (an app sends set_title and set_venue together) can meet in the
        // database: the second found the document changed and failed with EF's own text (launch-bot #126,
        // 17 Nov 2026). It runs once more on the document as it is now, then says so plainly.
        for (var attempt = 0; ; attempt++)
        {
            var live = new AskLiliaService.LiveDocument(doc);
            var fn = ((AskLiliaService)ask)
                .BuildDocumentTools(live, doc.Id, UserId, allowWrite: true, changed, () => meta = true, CancellationToken.None)
                .OfType<AIFunction>().First(f => f.Name == tool);
            try
            {
                result = await fn.InvokeAsync(new AIFunctionArguments(args.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)));
                break;
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                logger.LogInformation("[Mcp] {Tool} on {DocId} met another change (attempt {Attempt})", tool, doc.Id, attempt + 1);
                if (attempt >= 2)
                    return (new { error = $"Lilia could not {tool.Replace('_', ' ')}: the document was being changed at the same time (another call, or the author). Nothing was changed by this call; send it again.", category = "busy" }, false);
                db?.ChangeTracker.Clear();
                await Task.Delay(150 * (attempt + 1));
                if (await documents.GetDocumentAsync(doc.Id, UserId) is not { } now)
                    return (new { error = "This document is no longer available." }, false);
                doc = now;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Mcp] {Tool} failed on {DocId}", tool, doc.Id);
                return (new { error = $"Lilia could not {tool.Replace('_', ' ')}: {ex.Message}" }, false);
            }
        }

        // After a paste or a rewrite, the next edit saves its own undo point: the document as the paste left it.
        if (bulk && (changed.Count > 0 || meta)) cache.Remove(UndoKey(doc.Id));

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
        return (new
        {
            result,
            changedBlocks = changed,
            undo = undo is null ? null : new { versionId = undo, note = bulk
                ? "To undo this paste or rewrite, call restore_version with this versionId (the author can also restore it in History)."
                : "To undo, call restore_version with this versionId: it undoes this change and the app's other small edits made since its last paste or rewrite (within 10 minutes of each other). The author can also restore it in History." },
            url = $"{McpOAuth.PublicOrigin(config)}/d/{doc.Id}",
            image,
        }, changed.Count > 0 || meta);
    }

    internal static bool IsConcurrency(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) return true;
        return false;
    }

    private string UndoKey(Guid docId) => $"mcp-undo:{docId}:{User?.FindFirst("client_id")?.Value ?? "lilia"}";

    /// <summary>
    /// A version before an app's first change, then one per <see cref="UndoWindow"/> of quiet:
    /// one undo point for a burst of edits rather than one per call.
    /// </summary>
    private async Task<Guid?> UndoPointAsync(Guid docId, string app)
    {
        var key = UndoKey(docId);
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

    /// <summary>A new version now, made the burst's undo point; null when it can't be saved.</summary>
    private async Task<Guid?> FreshUndoPointAsync(Guid docId, string app, string what)
    {
        try
        {
            var v = await versions.CreateVersionAsync(docId, UserId, new Lilia.Core.DTOs.CreateVersionDto($"Before {app} {what}"));
            cache.Set(UndoKey(docId), v.Id, new MemoryCacheEntryOptions { SlidingExpiration = UndoWindow });
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
