using System.Text.Json;
using Lilia.Api.Models.Documents;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// The "Open in Lilia" landing point — turn a tool artifact into a real document
/// owned by the signed-in user.
///
/// <para>This deliberately lives in the editor rather than the tools host. The
/// tools host writes an artifact row and stops; the editor reads it. Pointing the
/// dependency this way keeps the public, anonymous tools surface from holding a
/// reference to the editor's document and block services — the tools host can be
/// deployed, scaled and (later) opened as a public API without dragging document
/// ownership along with it.</para>
///
/// <para>Word→LaTeX is excluded: it routes into import-review, not a flat doc.</para>
/// </summary>
[ApiController]
[Route("api/from-tool")]
public class FromToolController : ControllerBase
{
    private readonly LiliaDbContext _context;
    private readonly IDocumentService _documents;
    private readonly IBlockService _blocks;

    public FromToolController(LiliaDbContext context, IDocumentService documents, IBlockService blocks)
    {
        _context = context;
        _documents = documents;
        _blocks = blocks;
    }

    [HttpPost("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Adopt(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var art = await _context.ToolArtifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (art is null) return NotFound();
        if (art.ToolSlug == "word-to-latex")
            return UnprocessableEntity(new { message = "Word documents open via import-review, not this endpoint." });

        var title = art.ToolSlug switch { "latex-table" => "Table", "doi-to-bibtex" => "Reference", _ => "From tool" };
        var doc = await _documents.CreateDocumentAsync(userId, new CreateDocumentDto { Title = title });

        // One block from the artifact: a real editable table from the grid input; the
        // BibTeX as a code block; otherwise the output as a paragraph.
        var block = art.ToolSlug switch
        {
            "latex-table" when art.Input is not null =>
                new CreateBlockDto("table", art.Input.RootElement, 0, null, null),
            "doi-to-bibtex" =>
                new CreateBlockDto("code", JsonSerializer.SerializeToElement(new { code = art.Output ?? "", language = "bibtex" }), 0, null, null),
            _ =>
                new CreateBlockDto("paragraph", JsonSerializer.SerializeToElement(new { text = art.Output ?? "" }), 0, null, null),
        };
        await _blocks.CreateBlockAsync(doc.Id, block);

        // Funnel: the visitor crossed from a free tool into the product. Recorded
        // here because this is where it actually happens.
        _context.ToolEvents.Add(new ToolEvent
        {
            ToolSlug = art.ToolSlug,
            UserId = userId,
            AnonId = art.AnonId,
            Event = "signup",
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);

        return Ok(new { documentId = doc.Id });
    }
}
