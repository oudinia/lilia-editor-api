using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Live integration tests against the real Mathpix API.
/// These tests require valid API credentials and a real PDF file.
/// Credentials are loaded from .NET user secrets (Mathpix:AppId, Mathpix:AppKey, Mathpix:TestPdfPath).
/// Skip in CI — run manually with: dotnet test --filter "MathpixLive"
/// </summary>
[Trait("Category", "Integration")]
public class MathpixLiveIntegrationTest
{
    private readonly ITestOutputHelper _output;
    private readonly string _testPdfPath;
    private readonly string _appId;
    private readonly string _appKey;

    public MathpixLiveIntegrationTest(ITestOutputHelper output)
    {
        _output = output;

        var config = new ConfigurationBuilder()
            .AddUserSecrets<MathpixLiveIntegrationTest>()
            .Build();

        _appId = config["Mathpix:AppId"] ?? "";
        _appKey = config["Mathpix:AppKey"] ?? "";
        _testPdfPath = config["Mathpix:TestPdfPath"] ?? "";
    }

    private bool CanRun()
    {
        if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appKey))
        {
            _output.WriteLine("SKIP: Mathpix credentials not configured in user secrets");
            return false;
        }
        if (!File.Exists(_testPdfPath))
        {
            _output.WriteLine($"SKIP: Test PDF not found at {_testPdfPath}");
            return false;
        }
        return true;
    }

    [Fact]
    public async Task MathpixClient_SubmitAndPoll_ReturnsMarkdown()
    {
        if (!CanRun()) return;

        var options = Options.Create(new MathpixOptions
        {
            AppId = _appId,
            AppKey = _appKey,
            BaseUrl = "https://api.mathpix.com",
            PollIntervalMs = 3000,
            TimeoutSeconds = 600, // 10 minutes for 76-page doc
            MaxFileSizeMb = 50
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var clientLogger = loggerFactory.CreateLogger<MathpixClient>();
        var httpClient = new HttpClient();
        var client = new MathpixClient(httpClient, options, clientLogger);

        // Step 1: Verify API is available
        _output.WriteLine("[Test] Checking Mathpix API availability...");
        var isAvailable = await client.IsAvailableAsync();
        _output.WriteLine($"[Test] API available: {isAvailable}");
        isAvailable.Should().BeTrue("Mathpix API should be reachable with valid credentials");

        // Step 2: Submit PDF
        _output.WriteLine($"[Test] Submitting PDF: {_testPdfPath}");
        var pdfId = await client.SubmitPdfAsync(_testPdfPath);
        _output.WriteLine($"[Test] Got pdf_id: {pdfId}");
        pdfId.Should().NotBeNullOrEmpty();

        // Step 3: Poll until complete
        _output.WriteLine("[Test] Waiting for completion (this may take a few minutes)...");
        var startTime = DateTime.UtcNow;
        var markdown = await client.WaitForCompletionAsync(pdfId);
        var elapsed = DateTime.UtcNow - startTime;
        _output.WriteLine($"[Test] Completed in {elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"[Test] Markdown length: {markdown.Length} chars");
        _output.WriteLine($"[Test] First 500 chars:\n{markdown[..Math.Min(500, markdown.Length)]}");

        markdown.Should().NotBeNullOrEmpty();
        markdown.Length.Should().BeGreaterThan(100, "76-page PDF should produce substantial markdown");
    }

    [Fact]
    public async Task MathpixPdfImportService_FullPipeline_ParsesPdf()
    {
        if (!CanRun()) return;

        var options = Options.Create(new MathpixOptions
        {
            AppId = _appId,
            AppKey = _appKey,
            BaseUrl = "https://api.mathpix.com",
            PollIntervalMs = 3000,
            TimeoutSeconds = 600,
            MaxFileSizeMb = 50
        });

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var httpClient = new HttpClient();
        var client = new MathpixClient(httpClient, options, loggerFactory.CreateLogger<MathpixClient>());
        var service = new MathpixPdfImportService(client, options, loggerFactory.CreateLogger<MathpixPdfImportService>());

        // Verify CanParse
        service.CanParse(_testPdfPath).Should().BeTrue();

        // Full pipeline: submit → poll → parse markdown → ImportDocument
        _output.WriteLine("[Test] Running full MathpixPdfImportService.ParseAsync pipeline...");
        var startTime = DateTime.UtcNow;
        var result = await service.ParseAsync(_testPdfPath);
        var elapsed = DateTime.UtcNow - startTime;

        _output.WriteLine($"[Test] Completed in {elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"[Test] Title: {result.Title}");
        _output.WriteLine($"[Test] Total elements: {result.Elements.Count}");
        _output.WriteLine($"[Test] Warnings: {result.Warnings.Count}");

        // Log element type breakdown
        var typeCounts = result.Elements
            .GroupBy(e => e.Type)
            .OrderByDescending(g => g.Count())
            .Select(g => $"  {g.Key}: {g.Count()}");
        _output.WriteLine("[Test] Element breakdown:");
        foreach (var line in typeCounts)
            _output.WriteLine(line);

        // Log first 10 elements
        _output.WriteLine("\n[Test] First 10 elements:");
        foreach (var el in result.Elements.Take(10))
        {
            var text = el switch
            {
                ImportHeading h => $"H{h.Level}: {h.Text[..Math.Min(80, h.Text.Length)]}",
                ImportParagraph p => $"P: {p.Text[..Math.Min(80, p.Text.Length)]}",
                ImportEquation eq => $"EQ: {eq.LatexContent?[..Math.Min(60, eq.LatexContent?.Length ?? 0)]}",
                ImportTable t => $"TABLE: {t.RowCount}x{t.ColumnCount}",
                ImportImage img => $"IMG: {img.MimeType}, {img.Data.Length} bytes, alt={img.AltText}",
                ImportCodeBlock cb => $"CODE: {cb.Text[..Math.Min(60, cb.Text.Length)]}",
                ImportListItem li => $"LI: {(li.IsNumbered ? "num" : "bul")} {li.Text[..Math.Min(60, li.Text.Length)]}",
                ImportAbstract a => $"ABSTRACT: {a.Text[..Math.Min(60, a.Text.Length)]}",
                ImportTheorem th => $"THM({th.EnvironmentType}): {th.Text[..Math.Min(60, th.Text.Length)]}",
                ImportBibliographyEntry b => $"BIB[{b.ReferenceLabel}]: {b.Text[..Math.Min(60, b.Text.Length)]}",
                _ => $"{el.Type}: ?"
            };
            _output.WriteLine($"  [{el.Order}] {text}");
        }

        // Assertions
        result.Elements.Should().NotBeEmpty("76-page PDF should produce elements");
        result.Elements.Count.Should().BeGreaterThan(20, "76-page PDF should have many elements");
        result.Title.Should().NotBe("Imported PDF Document", "should extract a real title");
        result.SourcePath.Should().Be(_testPdfPath);
        result.Elements.Select(e => e.Order).Should().BeInAscendingOrder();

        // Should contain headings
        result.Elements.OfType<ImportHeading>().Should().NotBeEmpty("academic PDF should have headings");

        // Should contain paragraphs
        result.Elements.OfType<ImportParagraph>().Should().NotBeEmpty("PDF should have paragraph text");
    }
}
