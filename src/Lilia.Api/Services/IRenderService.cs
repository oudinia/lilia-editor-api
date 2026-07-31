using Lilia.Core.DTOs;
using Lilia.Core.Entities;

namespace Lilia.Api.Services;

public interface IRenderService
{
    Task<int> GetPageCountAsync(Guid documentId);
    Task<List<SectionDto>> GetSectionsAsync(Guid documentId);
    Task<string> RenderPageAsync(Guid documentId, int page);
    Task<string> RenderToHtmlAsync(Guid documentId);
    Task<string> RenderToLatexAsync(Guid documentId);

    /// <summary>
    /// <paramref name="longtableBlocks"/> names table blocks to emit as
    /// <c>longtable</c> instead of a float-wrapped <c>tabular</c> — the second
    /// pass of the auto-fit loop, after a first compile reported which tables
    /// ran off the page. Ignored in multi-column documents, where longtable is
    /// a hard error.
    /// </summary>
    Task<string> RenderToLatexAsync(Guid documentId, IReadOnlySet<Guid>? longtableBlocks);
    Task<string> RenderToMarkdownAsync(Guid documentId);
    Task<string> RenderToLmlAsync(Guid documentId);
    string RenderBlockToHtml(Block block);
    string RenderBlockToLatex(Block block);
    string RenderBlockToMarkdown(Block block);
    string RenderBlockToLml(Block block);
}
