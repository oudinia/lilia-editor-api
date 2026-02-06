using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LmlController : ControllerBase
{
    private readonly ILmlConversionService _conversionService;

    public LmlController(ILmlConversionService conversionService)
    {
        _conversionService = conversionService;
    }

    /// <summary>
    /// Convert LML content to LaTeX.
    /// </summary>
    [HttpPost("to-latex")]
    [AllowAnonymous]
    public ActionResult<LmlConversionResultDto> ToLatex([FromBody] LmlConversionRequestDto request)
    {
        if (string.IsNullOrEmpty(request.Content))
            return BadRequest("Content is required");

        var options = new LmlConversionOptions
        {
            Title = request.Title,
            Author = request.Author,
            PaperSize = request.PaperSize ?? "a4paper",
            FontSize = request.FontSize ?? "11pt",
            FontFamily = request.FontFamily ?? "charter",
            IncludePreamble = request.IncludePreamble ?? true
        };

        var result = _conversionService.ConvertToLatex(request.Content, options);
        return Ok(new LmlConversionResultDto(result, "latex"));
    }

    /// <summary>
    /// Convert LML content to HTML.
    /// </summary>
    [HttpPost("to-html")]
    [AllowAnonymous]
    public ActionResult<LmlConversionResultDto> ToHtml([FromBody] LmlConversionRequestDto request)
    {
        if (string.IsNullOrEmpty(request.Content))
            return BadRequest("Content is required");

        var options = new LmlConversionOptions
        {
            Title = request.Title,
            Author = request.Author,
            IncludePreamble = request.IncludePreamble ?? true
        };

        var result = _conversionService.ConvertToHtml(request.Content, options);
        return Ok(new LmlConversionResultDto(result, "html"));
    }

    /// <summary>
    /// Convert LML content to Markdown.
    /// </summary>
    [HttpPost("to-markdown")]
    [AllowAnonymous]
    public ActionResult<LmlConversionResultDto> ToMarkdown([FromBody] LmlConversionRequestDto request)
    {
        if (string.IsNullOrEmpty(request.Content))
            return BadRequest("Content is required");

        var result = _conversionService.ConvertToMarkdown(request.Content);
        return Ok(new LmlConversionResultDto(result, "markdown"));
    }

    /// <summary>
    /// Convert LML content to multiple formats at once.
    /// </summary>
    [HttpPost("convert")]
    [AllowAnonymous]
    public ActionResult<LmlMultiConversionResultDto> ConvertAll([FromBody] LmlConversionRequestDto request)
    {
        if (string.IsNullOrEmpty(request.Content))
            return BadRequest("Content is required");

        var options = new LmlConversionOptions
        {
            Title = request.Title,
            Author = request.Author,
            PaperSize = request.PaperSize ?? "a4paper",
            FontSize = request.FontSize ?? "11pt",
            FontFamily = request.FontFamily ?? "charter",
            IncludePreamble = request.IncludePreamble ?? true
        };

        var latex = _conversionService.ConvertToLatex(request.Content, options);
        var html = _conversionService.ConvertToHtml(request.Content, options);
        var markdown = _conversionService.ConvertToMarkdown(request.Content);

        return Ok(new LmlMultiConversionResultDto(latex, html, markdown));
    }
}

public record LmlConversionRequestDto(
    string Content,
    string? Title = null,
    string? Author = null,
    string? PaperSize = null,
    string? FontSize = null,
    string? FontFamily = null,
    bool? IncludePreamble = null
);

public record LmlConversionResultDto(string Output, string Format);

public record LmlMultiConversionResultDto(string Latex, string Html, string Markdown);
