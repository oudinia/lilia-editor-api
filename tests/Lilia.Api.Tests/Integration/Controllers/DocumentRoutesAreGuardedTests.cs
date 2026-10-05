using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Lilia.Api.Filters;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// A5 of the authz audit (3 Oct 2026): a guard so the hole cannot come back.
///
/// <para>Four controllers (Studio, Typst export, LaTeX render, hints) answered anyone who knew a
/// document id because each action had to remember to check, and these did not. This walks the
/// real route table: every route that names a document or a block (<c>{docId}</c>,
/// <c>{documentId}</c>, <c>{blockId}</c>) must carry <see cref="RequireDocumentAccessAttribute"/>,
/// or be listed in <see cref="Reviewed"/> with the reason it is safe. A new route with a document
/// id and no check fails here, with its name, instead of shipping.</para>
///
/// <para>To add a route to <see cref="Reviewed"/> is a statement that a person read the action and
/// saw it check access itself (<c>HasAccessAsync</c>, an owner filter in the query) or that it is
/// public on purpose. Prefer the attribute: it runs before binding and before any compile.</para>
/// </summary>
[Collection("Integration")]
public class DocumentRoutesAreGuardedTests : IntegrationTestBase
{
    public DocumentRoutesAreGuardedTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string Inline = "checks access in the action or its service; Reviewed_routes_refuse_a_stranger calls it as one";

    // Controller.Action -> why it is safe without the attribute. These existed before the filter and were
    // read and probed on 5 Oct 2026 (the cloud session's IDOR sweep of 3 Oct found none open). New routes
    // use the attribute instead of being added here.
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["AiImport.FixFormatting"] = Inline,
        ["AiImport.SuggestImprovements"] = Inline,
        ["Assets.CreateAsset"] = Inline,
        ["Assets.DeleteAsset"] = Inline,
        ["Assets.GetAsset"] = Inline,
        ["Assets.GetAssets"] = Inline,
        ["Assets.GetSize"] = Inline,
        ["Assets.UploadAsset"] = Inline,
        ["Bibliography.CreateEntry"] = Inline,
        ["Bibliography.DeleteEntry"] = Inline,
        ["Bibliography.ExportBibTex"] = Inline,
        ["Bibliography.GetEntries"] = Inline,
        ["Bibliography.GetEntry"] = Inline,
        ["Bibliography.GetFormattedBibliography"] = Inline,
        ["Bibliography.GetFormattedEntry"] = Inline,
        ["Bibliography.GetStyles"] = "public: a static list of citation styles; nothing of the document's is in it",
        ["Bibliography.ImportBibTex"] = Inline,
        ["Bibliography.LookupArxiv"] = Inline,
        ["Bibliography.LookupDoi"] = Inline,
        ["Bibliography.LookupIsbn"] = Inline,
        ["Bibliography.SearchByTitle"] = Inline,
        ["Bibliography.UpdateEntry"] = Inline,
        ["BlockGroups.Create"] = Inline,
        ["BlockGroups.Delete"] = Inline,
        ["BlockGroups.Get"] = Inline,
        ["BlockGroups.List"] = Inline,
        ["BlockGroups.Update"] = Inline,
        ["Blocks.BatchConvert"] = Inline,
        ["Blocks.BatchUpdateBlocks"] = Inline,
        ["Blocks.ConvertBlock"] = Inline,
        ["Blocks.CreateBlock"] = Inline,
        ["Blocks.DeleteBlock"] = Inline,
        ["Blocks.GetBlock"] = Inline,
        ["Blocks.GetBlockLatex"] = Inline,
        ["Blocks.GetBlocks"] = Inline,
        ["Blocks.ReorderBlocks"] = Inline,
        ["Blocks.UpdateBlock"] = Inline,
        ["Blocks.UpdateBlockFromLatex"] = Inline,
        ["Capabilities.Get"] = Inline,
        ["Collaborators.AddGroupCollaborator"] = Inline,
        ["Collaborators.AddUserCollaborator"] = Inline,
        ["Collaborators.GetCollaborators"] = Inline,
        ["Collaborators.InviteByEmail"] = Inline,
        ["Collaborators.RemoveGroupCollaborator"] = Inline,
        ["Collaborators.RemoveUserCollaborator"] = Inline,
        ["Collaborators.UpdateGroupCollaboratorRole"] = Inline,
        ["Collaborators.UpdateUserCollaboratorRole"] = Inline,
        ["Comments.CreateComment"] = Inline,
        ["Comments.CreateReply"] = Inline,
        ["Comments.DeleteComment"] = Inline,
        ["Comments.DeleteReply"] = Inline,
        ["Comments.GetComment"] = Inline,
        ["Comments.GetCommentCounts"] = Inline,
        ["Comments.ListComments"] = Inline,
        ["Comments.UpdateComment"] = Inline,
        ["DocumentLabels.AddLabelToDocument"] = Inline,
        ["DocumentLabels.RemoveLabelFromDocument"] = Inline,
        ["Export.ExportDocx"] = Inline,
        ["Export.ExportHtml"] = Inline,
        ["Export.ExportLatex"] = Inline,
        ["Export.ExportLml"] = Inline,
        ["Export.ExportMarkdown"] = Inline,
        ["Export.ExportPdf"] = Inline,
        ["ImportReview.GetBlockSource"] = Inline,
        ["ImportReview.ResetBlock"] = Inline,
        ["ImportReview.UpdateBlock"] = Inline,
        ["Preview.GetFullHtmlPreview"] = Inline,
        ["Preview.GetHtmlPreview"] = Inline,
        ["Preview.GetLatexPreview"] = Inline,
        ["Preview.GetPageCount"] = Inline,
        ["Preview.GetSections"] = Inline,
        ["Preview.GetTypstPreview"] = Inline,
        ["References.Get"] = Inline,
        ["Tables.Attach"] = Inline,
        ["Tables.Detach"] = Inline,
        ["Versions.BranchVersion"] = Inline,
        ["Versions.CreateVersion"] = Inline,
        ["Versions.DeleteVersion"] = Inline,
        ["Versions.GetVersion"] = Inline,
        ["Versions.GetVersions"] = Inline,
        ["Versions.RestoreVersion"] = Inline,
    };

    private static string[] Names(RouteEndpoint e) => e.RoutePattern.Parameters.Select(p => p.Name).ToArray();

    private List<(string Key, string Route, bool Guarded)> DocumentRoutes()
    {
        var sources = Fixture.Factory.Services.GetServices<EndpointDataSource>();
        var found = new List<(string, string, bool)>();
        foreach (var e in sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            var names = Names(e);
            if (!names.Any(n => n is "docId" or "documentId" or "blockId")) continue;
            var action = e.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null) continue;                                    // not an MVC action
            var verb = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "?";
            found.Add(($"{action.ControllerName}.{action.ActionName}", $"{verb} /{e.RoutePattern.RawText?.TrimStart('/')}",
                       e.Metadata.OfType<RequireDocumentAccessAttribute>().Any()));
        }
        return found;
    }

    [Fact]
    public void The_route_table_has_document_routes_to_check()
    {
        DocumentRoutes().Count.Should().BeGreaterThan(50, "the walk would pass vacuously if it found nothing");
    }

    [Fact]
    public void Every_route_that_names_a_document_or_block_is_guarded_or_reviewed()
    {
        var unguarded = DocumentRoutes()
            .Where(r => !r.Guarded && !Reviewed.ContainsKey(r.Key))
            .Select(r => $"{r.Key}  ({r.Route})")
            .Distinct().OrderBy(x => x).ToList();

        if (Environment.GetEnvironmentVariable("PRINT_UNGUARDED") == "1")
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "unguarded-routes.txt"), unguarded);

        unguarded.Should().BeEmpty(
            "each of these takes a document or block id with no [RequireDocumentAccess]. Add the attribute " +
            "(preferred), or, if the action checks access itself, list it in Reviewed with the reason:\n  " + string.Join("\n  ", unguarded));
    }

    [Fact]
    public void The_reviewed_list_holds_no_stale_entries()
    {
        var routes = DocumentRoutes();
        var guarded = routes.Where(r => r.Guarded).Select(r => r.Key).ToHashSet();
        var existing = routes.Select(r => r.Key).ToHashSet();
        Reviewed.Keys.Where(k => !existing.Contains(k) || guarded.Contains(k)).Should().BeEmpty(
            "an entry that no longer exists, or that now carries the attribute, should be removed");
        Reviewed.Values.Should().OnlyContain(v => v.Length > 10, "every entry says why it is safe");
    }

    // ── the probe ────────────────────────────────────────────────────────

    private List<(string Key, string Verb, string Pattern)> ReviewedEndpoints()
    {
        var sources = Fixture.Factory.Services.GetServices<EndpointDataSource>();
        var list = new List<(string, string, string)>();
        foreach (var e in sources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>())
        {
            if (!Names(e).Any(n => n is "docId" or "documentId" or "blockId")) continue;
            var action = e.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null) continue;
            var key = $"{action.ControllerName}.{action.ActionName}";
            if (!Reviewed.TryGetValue(key, out var why) || why.StartsWith("public:")) continue;   // public on purpose: not probed
            foreach (var verb in e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                list.Add((key, verb, e.RoutePattern.RawText ?? ""));
        }
        return list;
    }

    private static string Fill(string pattern, Guid documentId, Guid blockId) =>
        System.Text.RegularExpressions.Regex.Replace(pattern.TrimStart('/'), @"\{(\*{0,2})(\w+)(?::[^}]*)?\??\}", m => m.Groups[2].Value switch
        {
            "docId" or "documentId" => documentId.ToString(),
            "blockId" => blockId.ToString(),
            var n when n.EndsWith("Id", StringComparison.OrdinalIgnoreCase) => Guid.NewGuid().ToString(),
            _ => "x",
        });

    [Fact]
    public async Task Reviewed_routes_refuse_a_stranger()
    {
        var owner = "authz_probe_owner";
        await SeedUserAsync(owner);
        await SeedUserAsync("authz_probe_stranger");
        var doc = await SeedDocumentAsync(owner, "Probe");
        var block = await SeedBlockAsync(doc.Id, "paragraph", "{\"text\":\"Private text\"}", 0);

        using var stranger = CreateClientAs("authz_probe_stranger");
        var open = new List<string>();
        foreach (var (key, verb, pattern) in ReviewedEndpoints())
        {
            var url = "/" + Fill(pattern, doc.Id, block.Id);
            using var req = new HttpRequestMessage(new HttpMethod(verb), url);
            if (verb is not ("GET" or "HEAD" or "DELETE"))
                req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var res = await stranger.SendAsync(req);
            var code = (int)res.StatusCode;
            if (code is >= 200 and < 300) open.Add($"{verb} {url}  ->  {code}  ({key})");
        }
        open.Should().BeEmpty("a stranger got a success answer from these routes:\n  " + string.Join("\n  ", open));
    }
}
