namespace Lilia.Core.Models.Epub;

public record EpubExportOptions(
    string Title,
    string? Author = null,
    string? Language = null,
    string? Publisher = null,
    string? Isbn = null,
    string? CssContent = null,
    bool IncludeAnnotations = false,
    bool CleanFormatting = true
);
