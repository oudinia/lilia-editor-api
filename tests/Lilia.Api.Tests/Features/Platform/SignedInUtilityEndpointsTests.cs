using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Features.Platform.Contracts;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Features.Platform;

/// <summary>
/// The signed-in utilities no test called (3 Nov 2026): "Open in Lilia" from a free tool, a document's
/// capability report, font coverage, the LaTeX coverage report, diagnostic captures, sync telemetry and
/// the licence status. Each answers its caller about the caller's own things, and 401 without sign-in.
/// </summary>
public class SignedInUtilityEndpointsTests : IntegrationTestBase, IDisposable
{
    private readonly RecordingSink _telemetry = new();
    private readonly WebApplicationFactory<Program> _factory;

    public SignedInUtilityEndpointsTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<ISyncTelemetrySink>();
            s.AddSingleton<ISyncTelemetrySink>(_telemetry);
            // Its flusher drains the database sink only, and refuses to start beside another.
            foreach (var d in s.Where(d => d.ImplementationType?.Name == "SyncTelemetryFlusher").ToList()) s.Remove(d);
        }));
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient As(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private async Task<ToolArtifact> SeedArtifactAsync(string slug, string? inputJson, string? output)
    {
        var art = new ToolArtifact
        {
            Id = Guid.NewGuid(), ToolSlug = slug, AnonId = "anon-visitor-1",
            Input = inputJson is null ? null : JsonDocument.Parse(inputJson),
            Output = output, OutputFormat = "text", CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateDbContext();
        db.ToolArtifacts.Add(art);
        await db.SaveChangesAsync();
        return art;
    }

    [Fact]
    public async Task Open_in_Lilia_turns_a_tool_table_into_the_callers_document_and_records_the_crossing()
    {
        await SeedUserAsync("tool-ada");
        var art = await SeedArtifactAsync("latex-table", """{"headers":["A","B"],"rows":[["1","2"]]}""", "\\begin{tabular}...");

        var res = await As("tool-ada").PostAsync($"/api/from-tool/{art.Id}", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var docId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        await using var db = CreateDbContext();
        var doc = await db.Documents.SingleAsync(d => d.Id == docId);
        doc.Should().Match<Document>(d => d.OwnerId == "tool-ada" && d.Title == "Table");
        // The tool's block comes first, then the empty paragraph every new document starts with.
        var blocks = await db.Blocks.Where(b => b.DocumentId == docId).OrderBy(b => b.SortOrder).ToListAsync();
        blocks.Select(b => b.Type).Should().Equal("table", "paragraph");
        var block = blocks[0];
        block.Content.RootElement.GetProperty("headers")[1].GetString().Should().Be("B");
        (await db.ToolEvents.SingleAsync(e => e.UserId == "tool-ada"))
            .Should().Match<ToolEvent>(e => e.Event == "signup" && e.ToolSlug == "latex-table" && e.AnonId == "anon-visitor-1");
    }

    [Fact]
    public async Task Open_in_Lilia_puts_BibTeX_in_a_code_block_and_refuses_Word_and_unknown_artifacts()
    {
        await SeedUserAsync("tool-grace");
        var bib = await SeedArtifactAsync("doi-to-bibtex", null, "@article{hopper1952, title={Compilers}}");
        var word = await SeedArtifactAsync("word-to-latex", null, "\\section{x}");
        var me = As("tool-grace");

        var res = await me.PostAsync($"/api/from-tool/{bib.Id}", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var docId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        await using (var db = CreateDbContext())
        {
            var block = await db.Blocks.Where(b => b.DocumentId == docId).OrderBy(b => b.SortOrder).FirstAsync();
            block.Type.Should().Be("code");
            block.Content.RootElement.GetProperty("language").GetString().Should().Be("bibtex");
            block.Content.RootElement.GetProperty("code").GetString().Should().Contain("hopper1952");
        }

        (await me.PostAsync($"/api/from-tool/{word.Id}", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await me.PostAsync($"/api/from-tool/{Guid.NewGuid()}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CreateAnonymousClient().PostAsync($"/api/from-tool/{bib.Id}", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_capability_report_is_the_owners_and_names_an_unknown_engine()
    {
        await SeedUserAsync("cap-ada");
        await SeedUserAsync("cap-mallory");
        var doc = await SeedDocumentAsync("cap-ada");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Plain text"}""");

        var res = await As("cap-ada").GetAsync($"/api/documents/{doc.Id}/capabilities?engine=pdflatex");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("engine").GetString().Should().Be("pdflatex");

        var bad = await As("cap-ada").GetAsync($"/api/documents/{doc.Id}/capabilities?engine=wordperfect");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("supported").EnumerateArray().Should().NotBeEmpty();

        (await As("cap-mallory").GetAsync($"/api/documents/{doc.Id}/capabilities")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Font_coverage_says_whether_it_can_tell_and_skips_plain_ASCII()
    {
        var me = As("font-ada");

        var covering = await me.GetFromJsonAsync<JsonElement>("/api/fonts/covering?text=Hello");
        covering.TryGetProperty("available", out _).Should().BeTrue("\"cannot tell\" is its own answer");
        covering.GetProperty("codePoints").EnumerateArray().Should().BeEmpty("ASCII is never asked about");

        var uncovered = await me.GetFromJsonAsync<JsonElement>("/api/fonts/Latin%20Modern%20Roman/uncovered?text=Hello");
        uncovered.GetProperty("family").GetString().Should().Be("Latin Modern Roman");
        (await CreateAnonymousClient().GetAsync("/api/fonts/covering?text=x")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_LaTeX_coverage_report_answers_signed_in_callers_only()
    {
        var me = As("cov-ada");

        (await me.GetAsync("/api/lilia/latex-coverage/report?windowDays=9999")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.GetAsync("/api/lilia/latex-coverage/packages/no-such-package-xyz")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CreateAnonymousClient().GetAsync("/api/lilia/latex-coverage/report")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_diagnostic_capture_is_read_by_its_owner_only()
    {
        await SeedUserAsync("diag-ada");
        await SeedUserAsync("diag-mallory");
        var capture = new { source = "math-editor", note = "the cursor jumps", userAgent = "test", url = "/document/x", payload = new { steps = 3 } };

        var created = await As("diag-ada").PostAsJsonAsync("/api/diagnostics/captures", capture);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refToken").GetString();

        var mine = await As("diag-ada").GetFromJsonAsync<JsonElement>($"/api/diagnostics/captures/{token}");
        mine.GetProperty("note").GetString().Should().Be("the cursor jumps");
        (await As("diag-ada").GetFromJsonAsync<JsonElement>("/api/diagnostics/captures")).EnumerateArray().Should().ContainSingle();

        (await As("diag-mallory").GetAsync($"/api/diagnostics/captures/{token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As("diag-mallory").GetFromJsonAsync<JsonElement>("/api/diagnostics/captures")).EnumerateArray().Should().BeEmpty();
        (await CreateAnonymousClient().PostAsJsonAsync("/api/diagnostics/captures", capture)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sync_telemetry_keeps_known_kinds_caps_a_batch_and_names_the_caller()
    {
        var events = Enumerable.Range(0, 150).Select(i => new { eventKind = "sync_error", severity = i == 0 ? "loud" : "error", attemptCount = i })
            .Cast<object>()
            .Prepend(new { eventKind = "made_up_kind", severity = "error", attemptCount = -1 })
            .ToList();

        var res = await As("sync-ada").PostAsJsonAsync("/api/sync/telemetry", new { events });

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _telemetry.Records.Should().HaveCount(99, "a batch is cut at 100 events, and the unknown kind among them is dropped");
        _telemetry.Records.Should().OnlyContain(r => r.EventKind == "sync_error" && r.UserId == "sync-ada" && r.Source == "client");
        _telemetry.Records[0].Severity.Should().Be("warn", "an unknown severity becomes warn");
        (await As("sync-ada").PostAsJsonAsync("/api/sync/telemetry", new { events = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await CreateAnonymousClient().PostAsJsonAsync("/api/sync/telemetry", new { events })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_licence_is_express_by_default_and_pro_with_an_active_pro_subscription()
    {
        await SeedUserAsync("lic-ada");
        await SeedUserAsync("lic-grace");
        await using (var db = CreateDbContext())
        {
            db.Purchases.Add(new Purchase { Id = "pur-test-1", UserId = "lic-grace", Type = "SUBSCRIPTION", ProductId = "lilia-pro-monthly", Status = "active", CustomerId = "cus-test" });
            await db.SaveChangesAsync();
        }

        (await As("lic-ada").GetFromJsonAsync<JsonElement>("/api/license/status")).GetProperty("edition").GetString().Should().Be("express");
        (await As("lic-grace").GetFromJsonAsync<JsonElement>("/api/license/status")).GetProperty("edition").GetString().Should().Be("pro");
        (await CreateAnonymousClient().GetAsync("/api/license/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class RecordingSink : ISyncTelemetrySink
    {
        public List<SyncTelemetryRecord> Records { get; } = new();
        public void Record(SyncTelemetryRecord evt) { lock (Records) Records.Add(evt); }
    }
}
