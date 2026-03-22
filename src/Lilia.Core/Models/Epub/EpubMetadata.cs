namespace Lilia.Core.Models.Epub;

public record EpubMetadata(
    string Title,
    string? Author = null,
    string? Language = null,
    string? Publisher = null,
    string? Isbn = null,
    string? Description = null,
    string? CoverImagePath = null
);
