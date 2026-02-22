using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Lilia.Api.Tests.Services;

public class MineruClientTests
{
    private static MineruClient CreateClient(HttpMessageHandler handler, MineruOptions? options = null)
    {
        var opts = options ?? new MineruOptions { BaseUrl = "http://localhost:8000", TimeoutSeconds = 30, MaxFileSizeMb = 50 };
        var httpClient = new HttpClient(handler);
        return new MineruClient(httpClient, Options.Create(opts), NullLogger<MineruClient>.Instance);
    }

    [Fact]
    public async Task ParsePdfAsync_SendsMultipartRequest_ReturnsContentBlocks()
    {
        var contentList = new List<MineruContentBlock>
        {
            new() { Type = "text", Text = "Hello World", TextLevel = 1 },
            new() { Type = "equation", Text = "E = mc^2" },
        };
        var responsePayload = new MineruApiResponse
        {
            Backend = "pipeline",
            Version = "2.7.6",
            Results = new Dictionary<string, MineruFileResult>
            {
                ["test"] = new()
                {
                    ContentListJson = JsonSerializer.Serialize(contentList),
                    Images = new Dictionary<string, string>()
                }
            }
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(m => m.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            });

        var client = CreateClient(handlerMock.Object);

        // Create a temp PDF file
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllBytesAsync(tempFile, "%PDF-1.4 test"u8.ToArray());

        try
        {
            var result = await client.ParsePdfAsync(tempFile);

            result.ContentList.Should().HaveCount(2);
            result.ContentList[0].Type.Should().Be("text");
            result.ContentList[0].Text.Should().Be("Hello World");
            result.ContentList[0].TextLevel.Should().Be(1);
            result.ContentList[1].Type.Should().Be("equation");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParsePdfAsync_FileNotFound_Throws()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var client = CreateClient(handlerMock.Object);

        var act = () => client.ParsePdfAsync("/nonexistent/file.pdf");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ParsePdfAsync_FileTooLarge_Throws()
    {
        var options = new MineruOptions { BaseUrl = "http://localhost:8000", MaxFileSizeMb = 0 };
        var handlerMock = new Mock<HttpMessageHandler>();
        var client = CreateClient(handlerMock.Object, options);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllBytesAsync(tempFile, new byte[100]);

        try
        {
            var act = () => client.ParsePdfAsync(tempFile);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds maximum size*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParsePdfAsync_ServerError_ThrowsWithStatusCode()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service overloaded")
            });

        var client = CreateClient(handlerMock.Object);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllBytesAsync(tempFile, "%PDF"u8.ToArray());

        try
        {
            var act = () => client.ParsePdfAsync(tempFile);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*503*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParsePdfAsync_ConnectionRefused_ThrowsDescriptiveError()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = CreateClient(handlerMock.Object);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        await File.WriteAllBytesAsync(tempFile, "%PDF"u8.ToArray());

        try
        {
            var act = () => client.ParsePdfAsync(tempFile);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot connect*MinerU*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task IsAvailableAsync_ServerUp_ReturnsTrue()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var client = CreateClient(handlerMock.Object);

        var result = await client.IsAvailableAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_ServerDown_ReturnsFalse()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = CreateClient(handlerMock.Object);

        var result = await client.IsAvailableAsync();
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetImageAsync_Success_ReturnsBytes()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imageBytes)
            });

        var client = CreateClient(handlerMock.Object);

        var result = await client.GetImageAsync("figures/fig1.png");
        result.Should().BeEquivalentTo(imageBytes);
    }

    [Fact]
    public async Task GetImageAsync_NotFound_ReturnsEmptyArray()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Not Found"));

        var client = CreateClient(handlerMock.Object);

        var result = await client.GetImageAsync("missing.png");
        result.Should().BeEmpty();
    }
}
