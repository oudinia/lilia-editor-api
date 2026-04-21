using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Integration.LatexValidation;

/// <summary>
/// Integration tests for the LaTeX validation endpoints. These run against
/// the full API + a real pdflatex binary via the test fixture's Postgres
/// container, so they exercise the DB cache, the in-process LaTeXRenderService
/// cache, and the pdflatex subprocess path end-to-end.
///
/// The point is not correctness of the LaTeX compiler — it's performance +
/// cache behaviour under the workloads that production actually sees:
///   • Editor-on-blur validating one block at a time (hot cache path).
///   • Document-level rollup (aggregate SQL).
///   • Parallel validation bursts (the scenario that produced the
///     "Connection is busy" Sentry issue LILIA-API-Q).
///   • Two-tier Typst verdicts recorded alongside pdflatex results.
///
/// All timings are logged via ITestOutputHelper and then asserted against
/// generous upper bounds — the assertions catch true regressions; the logs
/// are the artefact for tuning.
/// </summary>
[Collection("Integration")]
public class LatexValidationPerfTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _out;
    private readonly string _userId = $"ltx-perf-{Guid.NewGuid():N}"[..28];

    public LatexValidationPerfTests(TestDatabaseFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _out = output;
    }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
    }

    // Deterministic set of small valid LaTeX snippets so timings are
    // comparable run-to-run. Equations are short on purpose — the pdflatex
    // compile time dominates regardless, and we want the test suite to
    // finish in seconds, not minutes.
    private static readonly string[] ValidEquations =
    [
        @"E = mc^2",
        @"\sum_{i=1}^{n} i = \frac{n(n+1)}{2}",
        @"\int_0^1 x^2 \, dx = \frac{1}{3}",
        @"\lim_{x \to 0} \frac{\sin x}{x} = 1",
        @"f(x) = \frac{1}{\sqrt{2\pi}\sigma} e^{-\frac{(x-\mu)^2}{2\sigma^2}}",
        @"\nabla \cdot \vec{E} = \frac{\rho}{\varepsilon_0}",
        @"\binom{n}{k} = \frac{n!}{k!(n-k)!}",
        @"A = \begin{pmatrix} 1 & 2 \\ 3 & 4 \end{pmatrix}",
    ];

    // ─── Scenarios ──────────────────────────────────────────────────────

    /// <summary>
    /// Cold-vs-warm cache sweep. Seeds N equation blocks, validates each
    /// via the per-block endpoint on a cold cache, then again on a warm
    /// cache. Logs per-call durations and p50/p95 for each pass.
    /// </summary>
    [Fact]
    public async Task BlockValidate_ColdVsWarm_LogsDurations()
    {
        const int blockCount = 15;

        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Perf Doc");
        var blockIds = new List<Guid>();
        for (var i = 0; i < blockCount; i++)
        {
            var latex = ValidEquations[i % ValidEquations.Length];
            var block = await SeedEquationBlockAsync(doc.Id, latex, sortOrder: i);
            blockIds.Add(block.Id);
        }

        _out.WriteLine($"Seeded {blockCount} equation blocks on document {doc.Id}");
        _out.WriteLine("");

        var coldTimings = await ValidateAllAsync(client, blockIds, label: "COLD");
        var warmTimings = await ValidateAllAsync(client, blockIds, label: "WARM");

        PrintSummary("COLD (cache-miss, real pdflatex)", coldTimings);
        PrintSummary("WARM (cache-hit, DB lookup only)", warmTimings);
        _out.WriteLine($"Speedup (cold p50 / warm p50): {coldTimings.P50 / Math.Max(1, warmTimings.P50):F1}x");

        // Sanity asserts.
        warmTimings.P95.Should().BeLessThan(200, "warm cache hits should be a single indexed DB read");
        warmTimings.P50.Should().BeLessThan(coldTimings.P50,
            "warm p50 must beat cold p50 — otherwise the cache is not engaging");
        (coldTimings.Results.All(r => r.Cached == false)).Should().BeTrue("every cold call was a cache miss");
        (warmTimings.Results.All(r => r.Cached == true)).Should().BeTrue("every warm call must report cached=true");
    }

    /// <summary>
    /// Regression guard for LILIA-API-Q (Connection is busy). Fires N
    /// block validations in parallel on a cold cache — if the bulk
    /// insert helper races on a shared Npgsql connection, this is where
    /// it breaks.
    /// </summary>
    [Fact]
    public async Task BlockValidate_Parallel_NoConnectionBusyUnderLoad()
    {
        const int blockCount = 20;

        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Parallel Perf Doc");

        var blockIds = new List<Guid>();
        for (var i = 0; i < blockCount; i++)
        {
            var block = await SeedEquationBlockAsync(doc.Id, ValidEquations[i % ValidEquations.Length], sortOrder: i);
            blockIds.Add(block.Id);
        }

        var sw = Stopwatch.StartNew();
        var results = await Task.WhenAll(blockIds.Select(async id =>
        {
            var r = await client.PostAsync($"/api/latex/block/{id}/validate", null);
            var body = await r.Content.ReadFromJsonAsync<ValidateResponse>();
            return (Status: r.StatusCode, Body: body);
        }));
        sw.Stop();

        _out.WriteLine($"Parallel validate: {blockCount} blocks in {sw.ElapsedMilliseconds} ms wall-clock");
        _out.WriteLine($"  Avg per-request wall-clock: {sw.ElapsedMilliseconds / (double)blockCount:F1} ms");

        results.Should().OnlyContain(r => r.Status == HttpStatusCode.OK,
            "concurrent validation bursts must not surface Connection-is-busy 500s (ref: Sentry LILIA-API-Q)");
        results.Should().OnlyContain(r => r.Body != null && r.Body.Valid,
            "all seeded equations are valid LaTeX");
    }

    /// <summary>
    /// Whole-document validate + rollup endpoint. Times both and checks
    /// that the rollup counts agree with the per-block validation results.
    /// </summary>
    [Fact]
    public async Task DocumentValidate_AndRollup_AreConsistent()
    {
        const int validCount = 6;
        const int invalidCount = 2;

        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Rollup Doc");

        var allBlockIds = new List<Guid>();
        for (var i = 0; i < validCount; i++)
            allBlockIds.Add((await SeedEquationBlockAsync(doc.Id, ValidEquations[i % ValidEquations.Length], i)).Id);

        // Deliberately broken equations — unknown control sequences.
        for (var i = 0; i < invalidCount; i++)
            allBlockIds.Add((await SeedEquationBlockAsync(doc.Id, @"\nosuchcommand{" + i + "}", validCount + i)).Id);

        // Warm the per-block cache so rollup can aggregate cached statuses.
        for (var i = 0; i < allBlockIds.Count; i++)
        {
            var id = allBlockIds[i];
            var kind = i < validCount ? "VALID" : "INVALID";
            var r = await client.PostAsync($"/api/latex/block/{id}/validate", null);
            if (!r.IsSuccessStatusCode)
                throw new Xunit.Sdk.XunitException(
                    $"[{kind} #{i}] block {id}/validate → {(int)r.StatusCode} {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            _out.WriteLine($"[{kind} #{i}] block {id.ToString()[..8]}… validated");
        }

        var sw = Stopwatch.StartNew();
        var rollupResp = await client.GetAsync($"/api/latex/{doc.Id}/validation-rollup");
        sw.Stop();
        if (!rollupResp.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(
                $"rollup → {(int)rollupResp.StatusCode}: {await rollupResp.Content.ReadAsStringAsync()}");

        rollupResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rollup = await rollupResp.Content.ReadFromJsonAsync<RollupResponse>();

        _out.WriteLine($"Rollup endpoint: {sw.ElapsedMilliseconds} ms");
        _out.WriteLine($"  totalBlocks={rollup!.TotalBlocks}  cachedBlocks={rollup.CachedBlocks}");
        _out.WriteLine($"  valid={rollup.ValidBlocks}  warning={rollup.WarningBlocks}  error={rollup.ErrorBlocks}");

        rollup.TotalBlocks.Should().Be(validCount + invalidCount);
        rollup.CachedBlocks.Should().Be(validCount + invalidCount);
        rollup.ErrorBlocks.Should().Be(invalidCount,
            "invalid equations must land in the error bucket");
        // Syntactically valid equations may still produce pdflatex warnings
        // (e.g. missing \title, Overfull hbox) — they land in either the
        // valid or warning bucket, never in error.
        (rollup.ValidBlocks + rollup.WarningBlocks).Should().Be(validCount,
            "every syntactically valid equation must end up in valid or warning, not error");

        // Also time the whole-doc validate endpoint.
        sw.Restart();
        var docValidate = await client.PostAsync($"/api/latex/{doc.Id}/validate", null);
        sw.Stop();
        if (!docValidate.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(
                $"doc validate → {(int)docValidate.StatusCode}: {await docValidate.Content.ReadAsStringAsync()}");
        _out.WriteLine($"Whole-doc validate: {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Two-tier validation: a typst verdict recorded via the typst endpoint
    /// must coexist with the pdflatex result for the same block + content
    /// hash, and the errors listing must surface both.
    /// </summary>
    [Fact]
    public async Task Typst_ResultRecorded_AppearsAlongsidePdflatex()
    {
        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Typst Doc");
        var block = await SeedEquationBlockAsync(doc.Id, @"\frac{a}{b}", 0);

        // pdflatex pass (valid).
        var pdflatex = await client.PostAsync($"/api/latex/block/{block.Id}/validate", null);
        pdflatex.EnsureSuccessStatusCode();

        // Simulate a client-side Typst compile that flagged a warning.
        var typstResp = await client.PostAsJsonAsync(
            $"/api/latex/block/{block.Id}/validate-typst",
            new { valid = true, error = (string?)null, warnings = new[] { "typst: uncommon symbol detected" } });
        typstResp.EnsureSuccessStatusCode();

        // Flip the block to an invalid state and record a typst failure
        // to prove both rows live alongside the pdflatex row.
        var invalidBlock = await SeedEquationBlockAsync(doc.Id, @"\unknownmacro", 1);
        var typstFail = await client.PostAsJsonAsync(
            $"/api/latex/block/{invalidBlock.Id}/validate-typst",
            new { valid = false, error = "typst: unknown command", warnings = Array.Empty<string>() });
        typstFail.EnsureSuccessStatusCode();

        var errors = await client.GetFromJsonAsync<List<ValidationErrorRow>>(
            $"/api/latex/{doc.Id}/validation-errors");
        errors.Should().NotBeNull();

        var validators = errors!.Select(e => e.Validator).Distinct().ToList();
        _out.WriteLine($"Non-valid rows for doc: {errors.Count}, validators: [{string.Join(", ", validators)}]");

        validators.Should().Contain("typst", "the typst failure row must be listed");
        errors.Should().Contain(e => e.Validator == "typst" && e.Status == "error");
    }

    /// <summary>
    /// Cache invalidation: editing the block's LaTeX must cause the next
    /// validate call to miss the DB cache (different content hash), so
    /// users never see a stale verdict after an edit.
    /// </summary>
    [Fact]
    public async Task EditingBlock_InvalidatesValidationCache()
    {
        var client = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Invalidation Doc");
        var block = await SeedEquationBlockAsync(doc.Id, @"x^2", 0);

        var first = await ValidateOnceAsync(client, block.Id);
        first.Cached.Should().BeFalse("first call on a never-validated block is a cache miss");

        var second = await ValidateOnceAsync(client, block.Id);
        second.Cached.Should().BeTrue("unchanged block should hit the cache on the second call");

        // Mutate the block's latex.
        await using (var db = CreateDbContext())
        {
            var entity = await db.Blocks.FindAsync(block.Id);
            entity!.Content = System.Text.Json.JsonDocument.Parse(
                """{"latex":"x^3 + y^3","equationMode":"display","numbered":true}""");
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var afterEdit = await ValidateOnceAsync(client, block.Id);
        afterEdit.Cached.Should().BeFalse("content hash changed → old cache row must not match");

        var thirdAfterEdit = await ValidateOnceAsync(client, block.Id);
        thirdAfterEdit.Cached.Should().BeTrue("the new result was persisted, so the next call must hit the cache");

        _out.WriteLine("Invalidation flow: miss → hit → edit → miss → hit ✓");
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private async Task<Lilia.Core.Entities.Block> SeedEquationBlockAsync(Guid documentId, string latex, int sortOrder)
    {
        var contentJson = JsonSerializer.Serialize(new
        {
            latex,
            equationMode = "display",
            numbered = true,
        });
        return await SeedBlockAsync(documentId, type: "equation", contentJson: contentJson, sortOrder: sortOrder);
    }

    private async Task<ValidationTimings> ValidateAllAsync(HttpClient client, List<Guid> blockIds, string label)
    {
        var results = new List<ValidationSample>();
        foreach (var id in blockIds)
        {
            var sw = Stopwatch.StartNew();
            var resp = await client.PostAsync($"/api/latex/block/{id}/validate", null);
            sw.Stop();
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<ValidateResponse>();
            results.Add(new ValidationSample(id, sw.ElapsedMilliseconds, body!.Cached, body.Valid));
        }

        var sorted = results.Select(r => r.DurationMs).OrderBy(x => x).ToArray();
        return new ValidationTimings(
            Label: label,
            Results: results,
            TotalMs: sorted.Sum(),
            P50: Percentile(sorted, 50),
            P95: Percentile(sorted, 95),
            Max: sorted.Length == 0 ? 0 : sorted[^1]);
    }

    private async Task<ValidateResponse> ValidateOnceAsync(HttpClient client, Guid blockId)
    {
        var resp = await client.PostAsync($"/api/latex/block/{blockId}/validate", null);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ValidateResponse>())!;
    }

    private void PrintSummary(string title, ValidationTimings t)
    {
        _out.WriteLine($"── {title} ──");
        _out.WriteLine($"   n={t.Results.Count}  total={t.TotalMs} ms  p50={t.P50} ms  p95={t.P95} ms  max={t.Max} ms");
        foreach (var r in t.Results)
            _out.WriteLine($"     block {r.BlockId.ToString()[..8]}…  {r.DurationMs,5} ms  cached={r.Cached}  valid={r.Valid}");
        _out.WriteLine("");
    }

    private static long Percentile(long[] sortedAsc, int p)
    {
        if (sortedAsc.Length == 0) return 0;
        var rank = (int)Math.Ceiling(p / 100.0 * sortedAsc.Length) - 1;
        return sortedAsc[Math.Clamp(rank, 0, sortedAsc.Length - 1)];
    }

    // ─── DTOs for response deserialisation ──────────────────────────────

    private sealed record ValidateResponse(
        bool Valid,
        string? Error,
        string[]? Warnings,
        Guid BlockId,
        bool Cached,
        DateTime? ValidatedAt);

    private sealed record RollupResponse(
        Guid DocumentId,
        int TotalBlocks,
        int CachedBlocks,
        int ValidBlocks,
        int WarningBlocks,
        int ErrorBlocks);

    private sealed record ValidationErrorRow(
        Guid BlockId,
        string Status,
        string Validator,
        string? ErrorMessage,
        DateTime ValidatedAt,
        string ContentHash);

    private sealed record ValidationSample(Guid BlockId, long DurationMs, bool Cached, bool Valid);

    private sealed record ValidationTimings(
        string Label,
        List<ValidationSample> Results,
        long TotalMs,
        long P50,
        long P95,
        long Max);
}
