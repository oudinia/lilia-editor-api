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
    Task<string> RenderToMarkdownAsync(Guid documentId);
    Task<string> RenderToLmlAsync(Guid documentId);
    string RenderBlockToHtml(Block block);
    string RenderBlockToLatex(Block block);
    string RenderBlockToMarkdown(Block block);
    string RenderBlockToLml(Block block);
}
