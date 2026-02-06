using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/[controller]")]
[Authorize]
public class BibliographyController : ControllerBase
{
    private readonly IBibliographyService _bibliographyService;
    private readonly IDocumentService _documentService;
    private readonly ICitationStyleService _citationStyleService;

    public BibliographyController(
        IBibliographyService bibliographyService,
        IDocumentService documentService,
        ICitationStyleService citationStyleService)
    {
        _bibliographyService = bibliographyService;
        _documentService = documentService;
        _citationStyleService = citationStyleService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<BibliographyEntryDto>>> GetEntries(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var entries = await _bibliographyService.GetEntriesAsync(docId);
        return Ok(entries);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BibliographyEntryDto>> GetEntry(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var entry = await _bibliographyService.GetEntryAsync(docId, id);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [HttpPost]
    public async Task<ActionResult<BibliographyEntryDto>> CreateEntry(Guid docId, [FromBody] CreateBibliographyEntryDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var entry = await _bibliographyService.CreateEntryAsync(docId, dto);
        return CreatedAtAction(nameof(GetEntry), new { docId, id = entry.Id }, entry);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BibliographyEntryDto>> UpdateEntry(Guid docId, Guid id, [FromBody] UpdateBibliographyEntryDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var entry = await _bibliographyService.UpdateEntryAsync(docId, id, dto);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteEntry(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _bibliographyService.DeleteEntryAsync(docId, id);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpPost("import")]
    public async Task<ActionResult<List<BibliographyEntryDto>>> ImportBibTex(Guid docId, [FromBody] ImportBibTexDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var entries = await _bibliographyService.ImportBibTexAsync(docId, dto.BibTexContent);
        return Ok(entries);
    }

    [HttpGet("export")]
    public async Task<ActionResult<string>> ExportBibTex(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var bibTex = await _bibliographyService.ExportBibTexAsync(docId);
        return Content(bibTex, "text/plain");
    }

    [HttpPost("doi")]
    public async Task<ActionResult<DoiLookupResultDto>> LookupDoi([FromBody] DoiLookupDto dto)
    {
        var result = await _bibliographyService.LookupDoiAsync(dto.Doi);
        if (result == null) return NotFound("DOI not found");
        return Ok(result);
    }

    [HttpPost("isbn")]
    public async Task<ActionResult<DoiLookupResultDto>> LookupIsbn([FromBody] IsbnLookupDto dto)
    {
        var result = await _bibliographyService.LookupIsbnAsync(dto.Isbn);
        if (result == null) return NotFound("ISBN not found");
        return Ok(result);
    }

    [HttpPost("arxiv")]
    public async Task<ActionResult<DoiLookupResultDto>> LookupArxiv([FromBody] ArxivLookupDto dto)
    {
        var result = await _bibliographyService.LookupArxivAsync(dto.ArxivId);
        if (result == null) return NotFound("arXiv paper not found");
        return Ok(result);
    }

    /// <summary>
    /// Get available citation styles.
    /// </summary>
    [HttpGet("styles")]
    [AllowAnonymous]
    public ActionResult<IEnumerable<CitationStyleInfo>> GetStyles()
    {
        return Ok(_citationStyleService.GetAvailableStyles());
    }

    /// <summary>
    /// Format a single entry in a specific citation style.
    /// </summary>
    [HttpGet("{id:guid}/formatted")]
    public async Task<ActionResult<FormattedCitationDto>> GetFormattedEntry(
        Guid docId,
        Guid id,
        [FromQuery] string style = "apa")
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var entry = await _bibliographyService.GetEntryAsync(docId, id);
        if (entry == null) return NotFound();

        if (!Enum.TryParse<CitationStyle>(style, ignoreCase: true, out var citationStyle))
        {
            return BadRequest($"Unknown citation style: {style}. Available: apa, mla, chicago, ieee, harvard, vancouver");
        }

        var formatted = _citationStyleService.FormatCitation(entry.EntryType, entry.Data, citationStyle);

        return Ok(new FormattedCitationDto(entry.Id, entry.CiteKey, formatted, style));
    }

    /// <summary>
    /// Format all entries as a complete bibliography in a specific style.
    /// </summary>
    [HttpGet("formatted")]
    public async Task<ActionResult<FormattedBibliographyDto>> GetFormattedBibliography(
        Guid docId,
        [FromQuery] string style = "apa")
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        if (!Enum.TryParse<CitationStyle>(style, ignoreCase: true, out var citationStyle))
        {
            return BadRequest($"Unknown citation style: {style}. Available: apa, mla, chicago, ieee, harvard, vancouver");
        }

        var entries = await _bibliographyService.GetEntriesAsync(docId);

        var formattedEntries = entries.Select(e => new FormattedCitationDto(
            e.Id,
            e.CiteKey,
            _citationStyleService.FormatCitation(e.EntryType, e.Data, citationStyle),
            style
        )).ToList();

        var fullBibliography = _citationStyleService.FormatBibliography(
            entries.Select(e => (e.EntryType, e.Data)),
            citationStyle
        );

        return Ok(new FormattedBibliographyDto(style, formattedEntries, fullBibliography));
    }
}

public record FormattedCitationDto(Guid Id, string CiteKey, string FormattedText, string Style);
public record FormattedBibliographyDto(string Style, List<FormattedCitationDto> Entries, string FullText);
