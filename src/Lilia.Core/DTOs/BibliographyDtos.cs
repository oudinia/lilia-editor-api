using System.Text.Json;

namespace Lilia.Core.DTOs;

public record BibliographyEntryDto(
    Guid Id,
    Guid DocumentId,
    string CiteKey,
    string EntryType,
    JsonElement Data,
    string? FormattedText,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateBibliographyEntryDto(
    string CiteKey,
    string EntryType,
    JsonElement Data
);

public record UpdateBibliographyEntryDto(
    string? CiteKey,
    string? EntryType,
    JsonElement? Data
);

public record ImportBibTexDto(
    string BibTexContent
);

public record DoiLookupDto(
    string Doi
);

public record IsbnLookupDto(
    string Isbn
);

public record DoiLookupResultDto(
    string CiteKey,
    string EntryType,
    JsonElement Data
);
