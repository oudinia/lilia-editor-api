using System.Security.Claims;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Controllers;

public class PreviewControllerTests
{
    private readonly Mock<IRenderService> _renderServiceMock;
    private readonly Mock<IPreviewCacheService> _cacheServiceMock;
    private readonly Mock<IDocumentService> _documentServiceMock;
    private readonly Mock<ILogger<PreviewController>> _loggerMock;
    private readonly PreviewController _sut;

    public PreviewControllerTests()
    {
        _renderServiceMock = new Mock<IRenderService>();
        _cacheServiceMock = new Mock<IPreviewCacheService>();
        _documentServiceMock = new Mock<IDocumentService>();
        _loggerMock = new Mock<ILogger<PreviewController>>();

        _sut = new PreviewController(
            _renderServiceMock.Object,
            _cacheServiceMock.Object,
            _documentServiceMock.Object,
            _loggerMock.Object);

        // Setup default authenticated user
        SetupAuthenticatedUser("user123");
    }

    private void SetupAuthenticatedUser(string userId)
    {
        var claims = new List<Claim> { new Claim("sub", userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    private void SetupUnauthenticatedUser()
    {
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
        };
    }

    private static DocumentDto CreateTestDocument(Guid docId)
    {
        return new DocumentDto(
            docId,
            "Test Document",
            "user123",
            null,
            "en",
            "a4",
            "Arial",
            12,
            1,
            "none",
            1.5,
            false,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null, null, null, null, // margins
            null, null, // header/footer
            null, null, null, // lineSpacing, paragraphIndent, pageNumbering
            new List<BlockDto>(),
            new List<BibliographyEntryDto>(),
            new List<LabelDto>()
        );
    }

    #region GetPageCount Tests

    [Fact]
    public async Task GetPageCount_WhenAuthenticated_ReturnsPageCount()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _renderServiceMock.Setup(s => s.GetPageCountAsync(docId))
            .ReturnsAsync(5);

        // Act
        var result = await _sut.GetPageCount(docId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PageCountResponse>().Subject;
        response.Count.Should().Be(5);
    }

    [Fact]
    public async Task GetPageCount_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        SetupUnauthenticatedUser();
        var docId = Guid.NewGuid();

        // Act
        var result = await _sut.GetPageCount(docId);

        // Assert
        result.Result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetPageCount_WhenDocumentNotFound_ReturnsNotFound()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync((DocumentDto?)null);

        // Act
        var result = await _sut.GetPageCount(docId);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region GetSections Tests

    [Fact]
    public async Task GetSections_WhenAuthenticated_ReturnsSections()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var sections = new List<SectionDto>
        {
            new("1", "Introduction", 1, 1, 2),
            new("2", "Methods", 1, 2, 5)
        };

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _renderServiceMock.Setup(s => s.GetSectionsAsync(docId))
            .ReturnsAsync(sections);

        // Act
        var result = await _sut.GetSections(docId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<SectionsResponse>().Subject;
        response.Sections.Should().HaveCount(2);
        response.Sections[0].Title.Should().Be("Introduction");
    }

    [Fact]
    public async Task GetSections_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        SetupUnauthenticatedUser();
        var docId = Guid.NewGuid();

        // Act
        var result = await _sut.GetSections(docId);

        // Assert
        result.Result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(401);
    }

    #endregion

    #region GetHtmlPreview Tests

    [Fact]
    public async Task GetHtmlPreview_WhenCacheHit_ReturnsCachedContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var cachedContent = "<div>Cached content</div>";

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _cacheServiceMock.Setup(s => s.GetCachedPreviewAsync(docId, "html", 1))
            .ReturnsAsync(cachedContent);
        _renderServiceMock.Setup(s => s.GetPageCountAsync(docId))
            .ReturnsAsync(5);

        // Act
        var result = await _sut.GetHtmlPreview(docId, 1);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PreviewResponse>().Subject;
        response.Content.Should().Be(cachedContent);
        response.Format.Should().Be("html");
        response.Page.Should().Be(1);
        response.TotalPages.Should().Be(5);

        // Verify render was not called
        _renderServiceMock.Verify(s => s.RenderPageAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetHtmlPreview_WhenCacheMiss_RendersAndCaches()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var renderedContent = "<div>Rendered content</div>";

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _cacheServiceMock.Setup(s => s.GetCachedPreviewAsync(docId, "html", 1))
            .ReturnsAsync((string?)null);
        _renderServiceMock.Setup(s => s.RenderPageAsync(docId, 1))
            .ReturnsAsync(renderedContent);
        _renderServiceMock.Setup(s => s.GetPageCountAsync(docId))
            .ReturnsAsync(3);

        // Act
        var result = await _sut.GetHtmlPreview(docId, 1);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PreviewResponse>().Subject;
        response.Content.Should().Be(renderedContent);
        response.TotalPages.Should().Be(3);

        // Verify cache was updated
        _cacheServiceMock.Verify(s => s.SetCachedPreviewAsync(docId, "html", renderedContent, 1), Times.Once);
    }

    [Fact]
    public async Task GetHtmlPreview_WithDefaultPage_UsesPageOne()
    {
        // Arrange
        var docId = Guid.NewGuid();

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _cacheServiceMock.Setup(s => s.GetCachedPreviewAsync(docId, "html", 1))
            .ReturnsAsync((string?)null);
        _renderServiceMock.Setup(s => s.RenderPageAsync(docId, 1))
            .ReturnsAsync("<div>Page 1</div>");
        _renderServiceMock.Setup(s => s.GetPageCountAsync(docId))
            .ReturnsAsync(1);

        // Act - calling without page parameter
        var result = await _sut.GetHtmlPreview(docId);

        // Assert
        _renderServiceMock.Verify(s => s.RenderPageAsync(docId, 1), Times.Once);
    }

    [Fact]
    public async Task GetHtmlPreview_WhenDocumentNotFound_ReturnsNotFound()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync((DocumentDto?)null);

        // Act
        var result = await _sut.GetHtmlPreview(docId);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    #endregion

    #region GetLatexPreview Tests

    [Fact]
    public async Task GetLatexPreview_WhenCacheHit_ReturnsCachedContent()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var cachedLatex = "\\documentclass{article}";

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _cacheServiceMock.Setup(s => s.GetCachedPreviewAsync(docId, "latex", null))
            .ReturnsAsync(cachedLatex);

        // Act
        var result = await _sut.GetLatexPreview(docId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PreviewResponse>().Subject;
        response.Content.Should().Be(cachedLatex);
        response.Format.Should().Be("latex");
        response.Page.Should().BeNull();
        response.TotalPages.Should().BeNull();
    }

    [Fact]
    public async Task GetLatexPreview_WhenCacheMiss_RendersAndCaches()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var renderedLatex = "\\documentclass{article}\n\\begin{document}...";

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _cacheServiceMock.Setup(s => s.GetCachedPreviewAsync(docId, "latex", null))
            .ReturnsAsync((string?)null);
        _renderServiceMock.Setup(s => s.RenderToLatexAsync(docId))
            .ReturnsAsync(renderedLatex);

        // Act
        var result = await _sut.GetLatexPreview(docId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PreviewResponse>().Subject;
        response.Content.Should().Be(renderedLatex);

        // Verify cache was updated
        _cacheServiceMock.Verify(s => s.SetCachedPreviewAsync(docId, "latex", renderedLatex, null), Times.Once);
    }

    [Fact]
    public async Task GetLatexPreview_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        SetupUnauthenticatedUser();
        var docId = Guid.NewGuid();

        // Act
        var result = await _sut.GetLatexPreview(docId);

        // Assert
        result.Result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(401);
    }

    #endregion

    #region GetFullHtmlPreview Tests

    [Fact]
    public async Task GetFullHtmlPreview_WhenAuthenticated_ReturnsFullHtml()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var fullHtml = "<div class=\"lilia-preview\">...</div>";

        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync(CreateTestDocument(docId));
        _cacheServiceMock.Setup(s => s.GetCachedPreviewAsync(docId, "html-full", null))
            .ReturnsAsync((string?)null);
        _renderServiceMock.Setup(s => s.RenderToHtmlAsync(docId))
            .ReturnsAsync(fullHtml);

        // Act
        var result = await _sut.GetFullHtmlPreview(docId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<PreviewResponse>().Subject;
        response.Content.Should().Be(fullHtml);
        response.Format.Should().Be("html");
        response.Page.Should().BeNull();
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public async Task AllEndpoints_RequireAuthentication()
    {
        // Arrange
        SetupUnauthenticatedUser();
        var docId = Guid.NewGuid();

        // Act & Assert — controller returns Unauthorized(object) = UnauthorizedObjectResult
        (await _sut.GetPageCount(docId)).Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(401);
        (await _sut.GetSections(docId)).Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(401);
        (await _sut.GetHtmlPreview(docId)).Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(401);
        (await _sut.GetLatexPreview(docId)).Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(401);
        (await _sut.GetFullHtmlPreview(docId)).Result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task AllEndpoints_CheckDocumentAccess()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _documentServiceMock.Setup(s => s.GetDocumentAsync(docId, "user123"))
            .ReturnsAsync((DocumentDto?)null);

        // Act & Assert
        (await _sut.GetPageCount(docId)).Result.Should().BeOfType<NotFoundResult>();
        (await _sut.GetSections(docId)).Result.Should().BeOfType<NotFoundResult>();
        (await _sut.GetHtmlPreview(docId)).Result.Should().BeOfType<NotFoundResult>();
        (await _sut.GetLatexPreview(docId)).Result.Should().BeOfType<NotFoundResult>();
        (await _sut.GetFullHtmlPreview(docId)).Result.Should().BeOfType<NotFoundResult>();
    }

    #endregion
}
