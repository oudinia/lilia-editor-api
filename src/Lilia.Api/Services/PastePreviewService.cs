using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Interfaces;

namespace Lilia.Api.Services;

/// <summary>A problem the compile found, placed on a block of the preview (null: Lilia's preamble or unknown).</summary>
public sealed record PastePreviewProblem(int? BlockIndex, string? BlockType, int? Line, string Severity, string Message);

/// <summary>A block of the preview, in the order the import would make it.</summary>
public sealed record PastePreviewBlock(string Type, object Content);

/// <summary>
/// What pasted text becomes, with nothing saved: its blocks, the LaTeX Lilia compiles for them, and the verdict.
/// <see cref="Compiles"/> is null when the compile could not run at all.
/// </summary>
public sealed record PastePreview(
    string Format, string? Title, IReadOnlyList<PastePreviewBlock> Blocks, string Latex, string Engine,
    bool? Compiles, bool PrintsCorrectly, IReadOnlyList<PastePreviewProblem> Problems, string? Fallback);

public interface IPastePreviewService
{
    Task<PastePreview> PreviewAsync(string content, string format, CancellationToken ct = default);
}

/// <summary>
/// Paste → LaTeX, phase 2 (the owner's call: before launch, 11 Nov 2026). Import → Paste shows, before anything is
/// made, the blocks the text becomes, the LaTeX Lilia will compile and whether it compiles, with the block at
/// fault. The text is read as the import reads it (<see cref="PastedText"/>), the LaTeX is Lilia's own emitter on a
/// document held in memory, and the verdict is check_document's (<see cref="IDocumentCheckService.CheckSourceAsync"/>),
/// with pdflatex unless the text needs another engine. Nothing is saved.
/// </summary>
public sealed class PastePreviewService(
    IRenderService render, IDocumentCheckService checker, ILatexParser latexParser, ILmlTextParser lmlParser,
    ILogger<PastePreviewService> logger) : IPastePreviewService
{
    public async Task<PastePreview> PreviewAsync(string content, string format, CancellationToken ct = default)
    {
        var pasted = await PastedText.ParseAsync(content, format, latexParser, lmlParser,
            warn: (ex, why) => logger.LogInformation(ex, "[PastePreview] {Why}", why));

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = "",
            Title = pasted.Title ?? "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        // A full LaTeX paper keeps its class, packages and macros, as the import keeps them.
        if (pasted.Format == "LATEX")
        {
            var pre = LatexPreambleExtractor.Extract(content);
            doc.CustomPreamble = pre.CustomPreamble;
            doc.LatexDocumentClass = pre.DocumentClass;
            doc.LatexPackages = pre.PackagesJson;
        }
        // What the screen import makes: a paper's title block first (#64).
        var shown = pasted.TitleBlock is { } title ? new[] { title }.Concat(pasted.Blocks).ToList() : pasted.Blocks.ToList();
        var blocks = shown.Select((b, i) => new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = b.Type,
            Content = PastedText.ContentJson(b),
            SortOrder = i,
            Depth = b.Depth,
        }).ToList();
        doc.Blocks = blocks;

        var latex = render.RenderToLatex(doc);
        var check = await checker.CheckSourceAsync(latex, doc.LatexEngine, ct);
        var index = blocks.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id.ToString(), x => x.i);
        var problems = check.Problems.Select(p =>
        {
            int? at = p.BlockId is { } id && index.TryGetValue(id, out var i) ? i : null;
            return new PastePreviewProblem(at, at is int n ? blocks[n].Type : null, p.Line, p.Severity, p.Message);
        }).ToList();

        return new PastePreview(
            pasted.Format, pasted.Title,
            shown.Select(b => new PastePreviewBlock(b.Type, b.Content)).ToList(),
            latex, Lilia.Api.Mcp.LiliaMcpTools.EngineFor(latex, doc.LatexEngine),
            check.Compiles, check.PrintsCorrectly, problems, pasted.Fallback);
    }
}
