using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Services;

public class PreviewCacheServiceTests : IDisposable
{
    private readonly IDistributedCache _cache;
    private readonly PreviewCacheService _sut;
    private readonly Mock<ILogger<PreviewCacheService>> _loggerMock;

    public PreviewCacheServiceTests()
    {
        var options = Options.Create(new MemoryDistributedCacheOptions());
        _cache = new MemoryDistributedCache(options);
        _loggerMock = new Mock<ILogger<PreviewCacheService>>();
        _sut = new PreviewCacheService(_cache, _loggerMock.Object);
    }

    public void Dispose()
    {
        // MemoryDistributedCache doesn't need explicit disposal
    }

    #region GetCachedPreviewAsync Tests

    [Fact]
    public async Task GetCachedPreviewAsync_WhenNotCached_ReturnsNull()
    {
        // Arrange
        var docId = Guid.NewGuid();

        // Act
        var result = await _sut.GetCachedPreviewAsync(docId, "html");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedPreviewAsync_WhenCached_ReturnsCachedContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var content = "<div>Cached HTML content</div>";
        await _sut.SetCachedPreviewAsync(docId, "html", content);

        // Act
        var result = await _sut.GetCachedPreviewAsync(docId, "html");

        // Assert
        result.Should().Be(content);
    }

    [Fact]
    public async Task GetCachedPreviewAsync_WithPage_ReturnsCachedPageContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var content = "<div>Page 1 content</div>";
        await _sut.SetCachedPreviewAsync(docId, "html", content, 1);

        // Act
        var result = await _sut.GetCachedPreviewAsync(docId, "html", 1);

        // Assert
        result.Should().Be(content);
    }

    [Fact]
    public async Task GetCachedPreviewAsync_DifferentPages_ReturnsDifferentContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var page1Content = "<div>Page 1</div>";
        var page2Content = "<div>Page 2</div>";
        await _sut.SetCachedPreviewAsync(docId, "html", page1Content, 1);
        await _sut.SetCachedPreviewAsync(docId, "html", page2Content, 2);

        // Act
        var result1 = await _sut.GetCachedPreviewAsync(docId, "html", 1);
        var result2 = await _sut.GetCachedPreviewAsync(docId, "html", 2);

        // Assert
        result1.Should().Be(page1Content);
        result2.Should().Be(page2Content);
    }

    [Fact]
    public async Task GetCachedPreviewAsync_DifferentFormats_ReturnsDifferentContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var htmlContent = "<div>HTML</div>";
        var latexContent = "\\documentclass{article}";
        await _sut.SetCachedPreviewAsync(docId, "html", htmlContent);
        await _sut.SetCachedPreviewAsync(docId, "latex", latexContent);

        // Act
        var htmlResult = await _sut.GetCachedPreviewAsync(docId, "html");
        var latexResult = await _sut.GetCachedPreviewAsync(docId, "latex");

        // Assert
        htmlResult.Should().Be(htmlContent);
        latexResult.Should().Be(latexContent);
    }

    #endregion

    #region SetCachedPreviewAsync Tests

    [Fact]
    public async Task SetCachedPreviewAsync_StoresContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var content = "Test content";

        // Act
        await _sut.SetCachedPreviewAsync(docId, "html", content);

        // Assert
        var result = await _sut.GetCachedPreviewAsync(docId, "html");
        result.Should().Be(content);
    }

    [Fact]
    public async Task SetCachedPreviewAsync_OverwritesExistingContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        await _sut.SetCachedPreviewAsync(docId, "html", "Old content");

        // Act
        await _sut.SetCachedPreviewAsync(docId, "html", "New content");

        // Assert
        var result = await _sut.GetCachedPreviewAsync(docId, "html");
        result.Should().Be("New content");
    }

    #endregion

    #region InvalidateCacheAsync Tests

    [Fact]
    public async Task InvalidateCacheAsync_RemovesHtmlCache()
    {
        // Arrange
        var docId = Guid.NewGuid();
        await _sut.SetCachedPreviewAsync(docId, "html", "HTML content");

        // Act
        await _sut.InvalidateCacheAsync(docId);

        // Assert
        var result = await _sut.GetCachedPreviewAsync(docId, "html");
        result.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCacheAsync_RemovesLatexCache()
    {
        // Arrange
        var docId = Guid.NewGuid();
        await _sut.SetCachedPreviewAsync(docId, "latex", "LaTeX content");

        // Act
        await _sut.InvalidateCacheAsync(docId);

        // Assert
        var result = await _sut.GetCachedPreviewAsync(docId, "latex");
        result.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCacheAsync_RemovesAllPageCaches()
    {
        // Arrange
        var docId = Guid.NewGuid();
        await _sut.SetCachedPreviewAsync(docId, "html", "Page 1", 1);
        await _sut.SetCachedPreviewAsync(docId, "html", "Page 2", 2);
        await _sut.SetCachedPreviewAsync(docId, "html", "Page 3", 3);

        // Act
        await _sut.InvalidateCacheAsync(docId);

        // Assert
        var result1 = await _sut.GetCachedPreviewAsync(docId, "html", 1);
        var result2 = await _sut.GetCachedPreviewAsync(docId, "html", 2);
        var result3 = await _sut.GetCachedPreviewAsync(docId, "html", 3);
        result1.Should().BeNull();
        result2.Should().BeNull();
        result3.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCacheAsync_DoesNotAffectOtherDocuments()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        await _sut.SetCachedPreviewAsync(docId1, "html", "Doc 1 content");
        await _sut.SetCachedPreviewAsync(docId2, "html", "Doc 2 content");

        // Act
        await _sut.InvalidateCacheAsync(docId1);

        // Assert
        var result1 = await _sut.GetCachedPreviewAsync(docId1, "html");
        var result2 = await _sut.GetCachedPreviewAsync(docId2, "html");
        result1.Should().BeNull();
        result2.Should().Be("Doc 2 content");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetCachedPreviewAsync_WithEmptyContent_ReturnsEmptyString()
    {
        // Arrange
        var docId = Guid.NewGuid();
        await _sut.SetCachedPreviewAsync(docId, "html", "");

        // Act
        var result = await _sut.GetCachedPreviewAsync(docId, "html");

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public async Task SetCachedPreviewAsync_WithLargeContent_StoresCorrectly()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var largeContent = new string('x', 100000); // 100KB of content

        // Act
        await _sut.SetCachedPreviewAsync(docId, "html", largeContent);
        var result = await _sut.GetCachedPreviewAsync(docId, "html");

        // Assert
        result.Should().Be(largeContent);
    }

    [Fact]
    public async Task Cache_WithSpecialCharacters_StoresCorrectly()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var content = "<div>特殊字符 &amp; UTF-8 🎉</div>";

        // Act
        await _sut.SetCachedPreviewAsync(docId, "html", content);
        var result = await _sut.GetCachedPreviewAsync(docId, "html");

        // Assert
        result.Should().Be(content);
    }

    #endregion
}
