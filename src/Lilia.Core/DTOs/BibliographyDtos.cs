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

/// <param name="Overwrite">An entry whose cite key already exists: replace it
/// (true), or leave it and skip the incoming one (false). Omitted means true,
/// the behaviour before the flag existed.</param>
public record ImportBibTexDto(
    string BibTexContent,
    bool? Overwrite = null
);

public record DoiLookupDto(
    string Doi
);

public record IsbnLookupDto(
    string Isbn
);

public record ArxivLookupDto(
    string ArxivId
);

public record DoiLookupResultDto(
    string CiteKey,
    string EntryType,
    JsonElement Data
);
