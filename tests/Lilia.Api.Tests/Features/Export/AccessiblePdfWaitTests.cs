using System.Net;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// An accessible PDF that takes longer than the wait is still compiled and kept (10 Nov 2026: a 20-page paper took
/// 148 s; Cloudflare cuts at about 125 s, and the compile, tied to the request, stopped with it). The endpoint answers
/// 202 while it runs, asking again gets the same compile, and once done the PDF. Wait 50 ms here; the compile ends
/// when the test says.
/// </summary>
public class AccessiblePdfWaitTests : IntegrationTestBase, IDisposable
{
    private const string UserId = "test_user_001";
    private readonly SlowExport _export = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AccessiblePdfWaitTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Export:AccessibleWaitSeconds", "0.05");
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IDocumentExportService)).ToList()) s.Remove(d);
                s.AddSingleton<IDocumentExportService>(_export);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Caller()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", UserId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{UserId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", UserId);
        return c;
    }

    [Fact]
    public async Task A_slow_accessible_PDF_answers_202_then_the_PDF_from_one_compile()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Long paper");
        using var c = Caller();
        var url = $"/api/documents/{doc.Id}/export/pdf?accessible=true";

        var first = await c.GetAsync(url);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        first.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(15));
        (await c.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Accepted, "the same compile is still running");
        _export.Finish();

        HttpResponseMessage? done = null;
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(100);
            var r = await c.GetAsync(url);
            if (r.StatusCode == HttpStatusCode.OK) { done = r; break; }
        }

        done.Should().NotBeNull();
        done!.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await done.Content.ReadAsByteArrayAsync()).Should().StartWith("%PDF"u8.ToArray());
        _export.AccessibleCalls.Should().Be(1, "every ask waited on the one compile");
    }

    [Fact]
    public async Task The_usual_PDF_is_unchanged()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Paper");
        using var c = Caller();

        var res = await c.GetAsync($"/api/documents/{doc.Id}/export/pdf");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The accessible PDF compiles until the test says <see cref="Finish"/> (a fixed delay raced the second ask under
    /// CI load); the usual one is immediate. Later asks of a done PDF come from "cache".
    /// </summary>
    private sealed class SlowExport : IDocumentExportService
    {
        private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 200));
        private int _accessible;
        private volatile bool _done;
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Finish() => _finish.TrySetResult();
        public int AccessibleCalls => _accessible;

        public async Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(Guid documentId, string? engineHint, string? lookTheme, bool printSafe)
        {
            if (engineHint != "tagged") return (Pdf, "pdflatex");
            if (_done) return (Pdf, "tagged");
            Interlocked.Increment(ref _accessible);
            await _finish.Task.WaitAsync(TimeSpan.FromSeconds(30));
            _done = true;
            return (Pdf, "tagged");
        }

        public Task<(byte[] Pdf, string Engine)> ExportToPdfWithEngineAsync(Guid documentId, string? engineHint = null) =>
            ExportToPdfWithEngineAsync(documentId, engineHint, null, false);

        public Task<byte[]> ExportToPdfAsync(Guid documentId) => throw new NotSupportedException();
        public Task<Lilia.Import.Models.ExportDocument> BuildExportDocumentAsync(Guid documentId) => throw new NotSupportedException();
        public Task<byte[]> ExportToDocxAsync(Guid documentId) => throw new NotSupportedException();
    }
}
