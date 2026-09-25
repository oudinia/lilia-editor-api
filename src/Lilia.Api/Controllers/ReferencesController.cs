using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// The document's cross-references: every label it defines, every place it
/// points at one, and where the two disagree.
///
/// <para><b>Why this exists.</b> A label lived only as a string inside one
/// block's JSON, so nothing could enumerate them — which is why the editor has
/// an autocomplete for <c>\cite{</c> and none for <c>\ref{</c>. The picker was
/// not missing because nobody built it; it was missing because there was
/// nothing for it to search.</para>
///
/// <para><b>Derived, not stored.</b> See <see cref="ReferenceIndex"/>. The
/// blocks are already loaded to render the document, so this cannot be
/// stale.</para>
/// </summary>
[ApiController]
[Route("api/documents/{docId:guid}/references")]
[Authorize]
public class ReferencesController : ControllerBase
{
    private readonly IBlockService _blocks;
    private readonly IDocumentService _documents;
    private readonly IRenderService _render;
    private readonly ILaTeXRenderService _latex;
    private readonly LiliaDbContext _db;
    private readonly ILogger<ReferencesController> _logger;

    public ReferencesController(
        IBlockService blocks,
        IDocumentService documents,
        IRenderService render,
        ILaTeXRenderService latex,
        LiliaDbContext db,
        ILogger<ReferencesController> logger)
    {
        _blocks = blocks;
        _documents = documents;
        _render = render;
        _latex = latex;
        _db = db;
        _logger = logger;
    }

    private string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Targets, uses and problems for this document.
    /// </summary>
    /// <param name="numbers">
    /// Compile first, so every target carries the number <c>\ref</c> would
    /// print. Off by default because it costs a full LaTeX run: the index
    /// itself is free, and numbers are the only part that is not.
    ///
    /// Without it — or when the compile fails — numbers come back null. That is
    /// the honest answer for a document nobody has compiled, and it is the same
    /// rule the table tool's verdict follows: "unchecked" is not "invalid".
    /// </param>
    [HttpGet]
    [ProducesResponseType(typeof(ReferenceReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid docId, [FromQuery] bool numbers = false)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        if (!await _documents.HasAccessAsync(docId, userId, Permissions.Read)) return Forbid();

        var dtos = await _blocks.GetBlocksAsync(docId);
        var blocks = dtos.Select(b => new Block
        {
            Id = b.Id,
            DocumentId = b.DocumentId,
            Type = b.Type,
            SortOrder = b.SortOrder,
            Content = JsonDocument.Parse(b.Content.GetRawText()),
        }).ToList();

        ReferenceReport report;
        DateTime? numberedAt;
        if (numbers)
        {
            // Asked for fresh numbers: compile now.
            var (aux, compiled) = await TryCompileForNumbersAsync(docId);
            report = ReferenceIndex.Build(blocks, aux);
            numberedAt = compiled ? DateTime.UtcNow : null;
        }
        else
        {
            // The numbers the last PDF compile kept — no compile, and they are
            // the ones in the PDF the author is looking at. Dated, because they
            // can be older than the blocks: the caller says so rather than
            // presenting them as current.
            var kept = await _db.Documents.AsNoTracking()
                .Where(d => d.Id == docId)
                .Select(d => new { d.LabelNumbers, d.LabelNumbersAt })
                .FirstOrDefaultAsync();
            var stored = LabelNumbers.Parse(kept?.LabelNumbers);
            report = ReferenceIndex.Build(blocks, stored);
            numberedAt = stored.Count > 0 ? kept?.LabelNumbersAt : null;
        }

        return Ok(ReferenceReportDto.From(report, numberedAt));
    }

    /// <summary>
    /// Compile the document and hand back its .aux. A failure here is not a
    /// failure of the request: the index is still correct and worth returning,
    /// so the numbers are simply absent and <c>numbered</c> says so.
    /// </summary>
    private async Task<(string? Aux, bool Numbered)> TryCompileForNumbersAsync(Guid docId)
    {
        try
        {
            var latex = await _render.RenderToLatexAsync(docId);
            if (string.IsNullOrWhiteSpace(latex)) return (null, false);

            var aux = await _latex.RenderToAuxAsync(latex);
            return (aux, !string.IsNullOrEmpty(aux));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[References] could not compile {DocId} for numbers", docId);
            return (null, false);
        }
    }
}

/// <param name="Numbered">
/// True when the numbers below come from a compile — just now, or the last PDF
/// compile. False means every <c>number</c> is null, not that they are zero.
/// </param>
/// <param name="NumberedAt">When that compile happened. A target with no number
/// while this is set was added since — the panel's "1 target added since".</param>
public record ReferenceReportDto(
    bool Numbered,
    DateTime? NumberedAt,
    IReadOnlyList<ReferenceTargetDto> Targets,
    IReadOnlyList<ReferenceUseDto> Uses,
    IReadOnlyList<ReferenceProblemDto> Problems)
{
    public static ReferenceReportDto From(ReferenceReport report, DateTime? numberedAt) => new(
        numberedAt is not null,
        numberedAt,
        report.Targets.Select(t => new ReferenceTargetDto(
            t.Key,
            t.Kind.ToString().ToLowerInvariant(),
            t.BlockId,
            t.Caption,
            t.Number,
            t.Page)).ToList(),
        report.Uses.Select(u => new ReferenceUseDto(u.Key, u.BlockId, u.Form)).ToList(),
        report.Problems.Select(p => new ReferenceProblemDto(p.Kind, p.Key, p.BlockIds)).ToList());
}

public record ReferenceTargetDto(
    string Key, string Kind, Guid BlockId, string? Caption, string? Number, int? Page);

public record ReferenceUseDto(string Key, Guid BlockId, string Form);

public record ReferenceProblemDto(string Kind, string Key, IReadOnlyList<Guid> BlockIds);
