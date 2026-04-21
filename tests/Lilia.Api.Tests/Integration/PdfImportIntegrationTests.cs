using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lilia.Api.Tests.Integration;

[Collection("Integration")]
public class PdfImportIntegrationTests : IntegrationTestBase
{
    private readonly string _userId = $"pdf-test-{Guid.NewGuid():N}"[..28];

    public PdfImportIntegrationTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
    }

    [Fact]
    public async Task ImportPdf_CreatesReviewSession_WithCorrectBlockTypes()
    {
        // Arrange: Register a mock PDF parser that returns known elements
        var mockPdfParser = new Mock<IPdfParser>();
        mockPdfParser.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<ImportOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportDocument
            {
                SourcePath = "/tmp/test.pdf",
                Title = "Test Paper",
                Elements =
                [
                    new ImportHeading { Text = "Introduction", Level = 1, Order = 0 },
                    new ImportParagraph { Text = "This is a paragraph.", Order = 1 },
                    new ImportEquation { LatexContent = "E = mc^2", ConversionSucceeded = true, Order = 2 },
                    new ImportTable
                    {
                        Rows =
                        [
                            [new ImportTableCell { Text = "A" }, new ImportTableCell { Text = "B" }],
                            [new ImportTableCell { Text = "1" }, new ImportTableCell { Text = "2" }]
                        ],
                        HasHeaderRow = true,
                        Order = 3
                    },
                    new ImportCodeBlock { Text = "print('hello')", Order = 4 }
                ]
            });

        // Create a client that uses the mocked PDF parser
        var factory = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing IPdfParser registration and replace with mock
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPdfParser));
                if (descriptor != null) services.Remove(descriptor);
                services.AddScoped(_ => mockPdfParser.Object);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", _userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{_userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", _userId);

        // Create a fake PDF content (base64)
        var fakeContent = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 fake"));

        // Act
        var response = await client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = fakeContent,
            format = "PDF",
            filename = "test-paper.pdf",
            title = "Test Paper",
            options = new
            {
                preserveFormatting = true,
                importImages = true,
                importBibliography = true,
                autoDetectEquations = true,
                splitByHeadings = true
            }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.TryGetProperty("reviewSessionId", out var sessionIdProp).Should().BeTrue();
        var sessionId = sessionIdProp.GetString();
        sessionId.Should().NotBeNullOrEmpty();

        // Verify we can fetch the review session
        var sessionResponse = await client.GetAsync($"/api/lilia/import-review/sessions/{sessionId}");
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessionData = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        sessionData.TryGetProperty("session", out var session).Should().BeTrue();
        session.GetProperty("documentTitle").GetString().Should().Be("Test Paper");

        // Verify the blocks in the review session (blocks is top-level, not nested in session)
        sessionData.TryGetProperty("blocks", out var blocks).Should().BeTrue();
        var blockArray = blocks.EnumerateArray().ToList();
        blockArray.Should().HaveCount(5);

        blockArray[0].GetProperty("originalType").GetString().Should().Be("heading");
        blockArray[1].GetProperty("originalType").GetString().Should().Be("paragraph");
        blockArray[2].GetProperty("originalType").GetString().Should().Be("equation");
        blockArray[3].GetProperty("originalType").GetString().Should().Be("table");
        blockArray[4].GetProperty("originalType").GetString().Should().Be("code");
    }

    [Fact]
    public async Task ImportPdf_UnsupportedFormat_WhenNoPdfParser_ReturnsError()
    {
        // Create a client WITHOUT a PDF parser registered
        var factory = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove IPdfParser so it's null in JobService
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPdfParser));
                if (descriptor != null) services.Remove(descriptor);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", _userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{_userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", _userId);

        var fakeContent = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 fake"));

        var response = await client.PostAsJsonAsync("/api/lilia/jobs/import", new
        {
            content = fakeContent,
            format = "PDF",
            filename = "test.pdf"
        });

        // Should fail because PDF parser is not available
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
