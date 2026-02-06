using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Lorem Ipsum text generation API.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LoremController : ControllerBase
{
    private readonly ILoremIpsumService _loremService;

    public LoremController(ILoremIpsumService loremService)
    {
        _loremService = loremService;
    }

    /// <summary>
    /// Generate Lorem Ipsum paragraphs.
    /// </summary>
    /// <param name="count">Number of paragraphs (1-20, default 3)</param>
    /// <param name="startWithLorem">Start with classic "Lorem ipsum..." (default true)</param>
    [HttpGet("paragraphs")]
    [ProducesResponseType(typeof(LoremResponse), StatusCodes.Status200OK)]
    public IActionResult GetParagraphs([FromQuery] int count = 3, [FromQuery] bool startWithLorem = true)
    {
        count = Math.Clamp(count, 1, 20);
        var text = _loremService.GenerateParagraphs(count, startWithLorem);
        return Ok(new LoremResponse { Text = text, Type = "paragraphs", Count = count });
    }

    /// <summary>
    /// Generate Lorem Ipsum sentences.
    /// </summary>
    /// <param name="count">Number of sentences (1-50, default 5)</param>
    /// <param name="startWithLorem">Start with classic "Lorem ipsum..." (default true)</param>
    [HttpGet("sentences")]
    [ProducesResponseType(typeof(LoremResponse), StatusCodes.Status200OK)]
    public IActionResult GetSentences([FromQuery] int count = 5, [FromQuery] bool startWithLorem = true)
    {
        count = Math.Clamp(count, 1, 50);
        var text = _loremService.GenerateSentences(count, startWithLorem);
        return Ok(new LoremResponse { Text = text, Type = "sentences", Count = count });
    }

    /// <summary>
    /// Generate Lorem Ipsum words.
    /// </summary>
    /// <param name="count">Number of words (1-500, default 50)</param>
    /// <param name="startWithLorem">Start with classic "Lorem ipsum..." (default true)</param>
    [HttpGet("words")]
    [ProducesResponseType(typeof(LoremResponse), StatusCodes.Status200OK)]
    public IActionResult GetWords([FromQuery] int count = 50, [FromQuery] bool startWithLorem = true)
    {
        count = Math.Clamp(count, 1, 500);
        var text = _loremService.GenerateWords(count, startWithLorem);
        return Ok(new LoremResponse { Text = text, Type = "words", Count = count });
    }

    /// <summary>
    /// Generate Lorem Ipsum with flexible parameters.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(LoremResponse), StatusCodes.Status200OK)]
    public IActionResult Generate([FromBody] LoremRequest request)
    {
        var text = request.Type?.ToLowerInvariant() switch
        {
            "paragraphs" or "p" => _loremService.GenerateParagraphs(
                Math.Clamp(request.Count, 1, 20), request.StartWithLorem),
            "sentences" or "s" => _loremService.GenerateSentences(
                Math.Clamp(request.Count, 1, 50), request.StartWithLorem),
            "words" or "w" => _loremService.GenerateWords(
                Math.Clamp(request.Count, 1, 500), request.StartWithLorem),
            _ => _loremService.GenerateParagraphs(
                Math.Clamp(request.Count, 1, 20), request.StartWithLorem)
        };

        return Ok(new LoremResponse
        {
            Text = text,
            Type = request.Type ?? "paragraphs",
            Count = request.Count
        });
    }
}

public class LoremRequest
{
    /// <summary>
    /// Type of content: "paragraphs", "sentences", or "words"
    /// </summary>
    public string? Type { get; set; } = "paragraphs";

    /// <summary>
    /// Number of units to generate
    /// </summary>
    public int Count { get; set; } = 3;

    /// <summary>
    /// Start with classic "Lorem ipsum dolor sit amet..."
    /// </summary>
    public bool StartWithLorem { get; set; } = true;
}

public class LoremResponse
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
}
