using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Services;

public class MathpixClientTests
{
    private static readonly MathpixOptions DefaultOptions = new()
    {
        AppId = "test-app-id",
        AppKey = "test-app-key",
        BaseUrl = "https://api.mathpix.com",
        PollIntervalMs = 100,
        TimeoutSeconds = 5,
        MaxFileSizeMb = 50
    };

    private static MathpixClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new MathpixClient(httpClient, Options.Create(DefaultOptions), NullLogger<MathpixClient>.Instance);
    }

    [Fact]
    public async Task SubmitPdfAsync_SetsAuthHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpHandler((request, _) =>
        {
            capturedRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new MathpixPdfResponse { PdfId = "test-123" }))
            };
            return Task.FromResult(response);
        });

        var client = CreateClient(handler);

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "fake pdf content");
        try
        {
            await client.SubmitPdfAsync(tempFile);

            capturedRequest.Should().NotBeNull();
            capturedRequest!.Headers.GetValues("app_id").Should().Contain("test-app-id");
            capturedRequest.Headers.GetValues("app_key").Should().Contain("test-app-key");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SubmitPdfAsync_Returns_PdfId()
    {
        var handler = new MockHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new MathpixPdfResponse { PdfId = "abc-456" }))
            };
            return Task.FromResult(response);
        });

        var client = CreateClient(handler);

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "fake pdf content");
        try
        {
            var pdfId = await client.SubmitPdfAsync(tempFile);
            pdfId.Should().Be("abc-456");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SubmitPdfAsync_Unauthorized_ThrowsWithMessage()
    {
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var client = CreateClient(handler);

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "fake pdf content");
        try
        {
            var act = () => client.SubmitPdfAsync(tempFile);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*authentication failed*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SubmitPdfAsync_RateLimit_ThrowsWithMessage()
    {
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var client = CreateClient(handler);

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "fake pdf content");
        try
        {
            var act = () => client.SubmitPdfAsync(tempFile);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*rate limit*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SubmitPdfAsync_FileNotFound_Throws()
    {
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var client = CreateClient(handler);

        var act = () => client.SubmitPdfAsync("/nonexistent/file.pdf");
        act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task SubmitPdfAsync_FileTooLarge_Throws()
    {
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var opts = new MathpixOptions
        {
            AppId = "test", AppKey = "test",
            BaseUrl = "https://api.mathpix.com",
            MaxFileSizeMb = 0 // 0MB max — any file is too large
        };

        var httpClient = new HttpClient(handler);
        var client = new MathpixClient(httpClient, Options.Create(opts), NullLogger<MathpixClient>.Instance);

        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "some content");
        try
        {
            var act = () => client.SubmitPdfAsync(tempFile);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*exceeds maximum*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsStatus()
    {
        var handler = new MockHttpHandler((_, _) =>
        {
            var status = new MathpixPdfStatus
            {
                Status = "completed",
                NumPages = 10,
                NumPagesCompleted = 10,
                PercentDone = 100,
                Markdown = "# Title\n\nSome content"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(status))
            });
        });

        var client = CreateClient(handler);
        var result = await client.GetStatusAsync("test-id");

        result.Status.Should().Be("completed");
        result.NumPages.Should().Be(10);
        result.Markdown.Should().Contain("Title");
    }

    [Fact]
    public async Task WaitForCompletionAsync_PollsUntilComplete()
    {
        var callCount = 0;
        var handler = new MockHttpHandler((request, _) =>
        {
            callCount++;
            var status = new MathpixPdfStatus
            {
                Status = callCount >= 3 ? "completed" : "processing",
                PercentDone = callCount >= 3 ? 100 : callCount * 33,
                Markdown = callCount >= 3 ? "# Result" : null
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(status))
            });
        });

        var client = CreateClient(handler);
        var result = await client.WaitForCompletionAsync("test-id");

        result.Should().Be("# Result");
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task WaitForCompletionAsync_ErrorStatus_Throws()
    {
        var handler = new MockHttpHandler((_, _) =>
        {
            var status = new MathpixPdfStatus
            {
                Status = "error",
                Error = "Encrypted PDF"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(status))
            });
        });

        var client = CreateClient(handler);

        var act = () => client.WaitForCompletionAsync("test-id");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Encrypted PDF*");
    }

    [Fact]
    public async Task WaitForCompletionAsync_Timeout_ThrowsTimeoutException()
    {
        var opts = new MathpixOptions
        {
            AppId = "test", AppKey = "test",
            BaseUrl = "https://api.mathpix.com",
            PollIntervalMs = 50,
            TimeoutSeconds = 1 // 1 second timeout
        };

        var handler = new MockHttpHandler((_, _) =>
        {
            var status = new MathpixPdfStatus { Status = "processing", PercentDone = 50 };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(status))
            });
        });

        var httpClient = new HttpClient(handler);
        var client = new MathpixClient(httpClient, Options.Create(opts), NullLogger<MathpixClient>.Instance);

        var act = () => client.WaitForCompletionAsync("test-id");
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task IsAvailableAsync_EmptyCredentials_ReturnsFalse()
    {
        var opts = new MathpixOptions { AppId = "", AppKey = "" };
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var httpClient = new HttpClient(handler);
        var client = new MathpixClient(httpClient, Options.Create(opts), NullLogger<MathpixClient>.Instance);

        var result = await client.IsAvailableAsync();
        result.Should().BeFalse();
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
