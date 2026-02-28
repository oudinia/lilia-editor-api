using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Services;

[Trait("Category", "Integration")]
public class MathpixImageDiagnosticTest
{
    private readonly ITestOutputHelper _output;
    private readonly string _testPdfPath;
    private readonly string _appId;
    private readonly string _appKey;

    public MathpixImageDiagnosticTest(ITestOutputHelper output)
    {
        _output = output;

        var config = new ConfigurationBuilder()
            .AddUserSecrets<MathpixImageDiagnosticTest>()
            .Build();

        _appId = config["Mathpix:AppId"] ?? "";
        _appKey = config["Mathpix:AppKey"] ?? "";
        _testPdfPath = config["Mathpix:TestPdfPath"] ?? "";
    }

    [Fact]
    public async Task DiagnoseImageExtraction()
    {
        if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appKey))
        {
            _output.WriteLine("SKIP: Mathpix credentials not configured in user secrets");
            return;
        }
        if (!File.Exists(_testPdfPath))
        {
            _output.WriteLine($"SKIP: Test PDF not found at {_testPdfPath}");
            return;
        }

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

        var result = await service.ParseAsync(_testPdfPath);

        var images = result.Elements.OfType<ImportImage>().ToList();
        _output.WriteLine($"\n=== IMAGE DIAGNOSTIC ===");
        _output.WriteLine($"Total images found: {images.Count}");

        var withData = 0;
        var withoutData = 0;

        foreach (var img in images)
        {
            var hasData = img.Data.Length > 0;
            if (hasData) withData++;
            else withoutData++;

            _output.WriteLine($"  [{img.Order}] {(hasData ? "OK" : "EMPTY")} " +
                $"size={img.Data.Length} bytes, " +
                $"mime={img.MimeType}, " +
                $"file={img.Filename}, " +
                $"alt={img.AltText?[..Math.Min(50, img.AltText?.Length ?? 0)]}");
        }

        _output.WriteLine($"\nImages with data: {withData}");
        _output.WriteLine($"Images without data: {withoutData}");

        // Also check what the review pipeline would produce
        _output.WriteLine($"\n=== REVIEW BLOCK PREVIEW ===");
        foreach (var img in images.Take(3))
        {
            var src = img.Data.Length > 0
                ? $"data:{img.MimeType};base64,{Convert.ToBase64String(img.Data)[..Math.Min(80, Convert.ToBase64String(img.Data).Length)]}..."
                : "(empty)";
            _output.WriteLine($"  Figure block src: {src}");
            _output.WriteLine($"  Caption: {img.AltText}");
        }

        // Check all block types for export readiness
        _output.WriteLine($"\n=== EXPORT READINESS ===");
        var headings = result.Elements.OfType<ImportHeading>().ToList();
        var paragraphs = result.Elements.OfType<ImportParagraph>().ToList();
        var equations = result.Elements.OfType<ImportEquation>().ToList();
        var tables = result.Elements.OfType<ImportTable>().ToList();
        var codeBlocks = result.Elements.OfType<ImportCodeBlock>().ToList();
        var listItems = result.Elements.OfType<ImportListItem>().ToList();
        var abstracts = result.Elements.OfType<ImportAbstract>().ToList();
        var theorems = result.Elements.OfType<ImportTheorem>().ToList();
        var bibEntries = result.Elements.OfType<ImportBibliographyEntry>().ToList();

        _output.WriteLine($"  Headings: {headings.Count} (all have text: {headings.All(h => !string.IsNullOrEmpty(h.Text))})");
        _output.WriteLine($"  Paragraphs: {paragraphs.Count} (all have text: {paragraphs.All(p => !string.IsNullOrEmpty(p.Text))})");
        _output.WriteLine($"  Equations: {equations.Count} (all have LaTeX: {equations.All(e => !string.IsNullOrEmpty(e.LatexContent))})");
        _output.WriteLine($"  Tables: {tables.Count} (all have rows: {tables.All(t => t.Rows.Count > 0)})");
        _output.WriteLine($"  Code blocks: {codeBlocks.Count} (all have text: {codeBlocks.All(c => !string.IsNullOrEmpty(c.Text))})");
        _output.WriteLine($"  List items: {listItems.Count} (all have text: {listItems.All(l => !string.IsNullOrEmpty(l.Text))})");
        _output.WriteLine($"  Images: {images.Count} (with data: {withData}/{images.Count})");
        _output.WriteLine($"  Abstracts: {abstracts.Count}");
        _output.WriteLine($"  Theorems: {theorems.Count}");
        _output.WriteLine($"  Bibliography entries: {bibEntries.Count}");

        _output.WriteLine($"\n  VERDICT: {(withoutData == 0 ? "ALL IMAGES HAVE DATA - READY FOR EXPORT" : $"{withoutData} IMAGES MISSING DATA")}");
    }
}
