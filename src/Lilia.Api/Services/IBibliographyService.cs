using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IBibliographyService
{
    Task<List<BibliographyEntryDto>> GetEntriesAsync(Guid documentId);
    Task<BibliographyEntryDto?> GetEntryAsync(Guid documentId, Guid entryId);
    Task<BibliographyEntryDto> CreateEntryAsync(Guid documentId, CreateBibliographyEntryDto dto);
    Task<BibliographyEntryDto?> UpdateEntryAsync(Guid documentId, Guid entryId, UpdateBibliographyEntryDto dto);
    Task<bool> DeleteEntryAsync(Guid documentId, Guid entryId);
    Task<List<BibliographyEntryDto>> ImportBibTexAsync(Guid documentId, string bibTexContent);
    Task<string> ExportBibTexAsync(Guid documentId);
    Task<DoiLookupResultDto?> LookupDoiAsync(string doi);
    Task<DoiLookupResultDto?> LookupIsbnAsync(string isbn);
}
