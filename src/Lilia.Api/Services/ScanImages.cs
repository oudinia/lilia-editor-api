namespace Lilia.Api.Services;

/// <summary>
/// A scanned page to import (PDF and scan import, 20 Nov 2026): a JPEG or PNG, told by its own first bytes, never by its
/// name, so a renamed file is refused before it reaches Mathpix or the plan's pages. One image is one PDF page against
/// the plan's <c>pdfPagesPerMonth</c>, the same allowance a PDF uses.
/// </summary>
public static class ScanImages
{
    /// <summary>The largest image imported: 10 MB, the connector's file limit and the editor's image upload limit.</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>The pages one image counts for against the plan.</summary>
    public const int PagesPerImage = 1;

    /// <summary>The importer's format for a scanned page (<c>ImportRequestDto.Format</c>).</summary>
    public const string Format = "IMAGE";

    /// <summary>The file name extensions taken as a scanned page.</summary>
    public static readonly string[] Extensions = ["jpg", "jpeg", "png"];

    /// <summary><c>image/png</c> or <c>image/jpeg</c> from the file's signature, or null for anything else.</summary>
    public static string? ContentType(ReadOnlySpan<byte> bytes)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A. JPEG: FF D8 FF.
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        return null;
    }

    /// <summary>The extension Mathpix is sent, for the content type.</summary>
    public static string Extension(string contentType) => contentType == "image/png" ? ".png" : ".jpg";

    internal const string NotAnImageMessage =
        "This file isn't a JPEG or PNG image, whatever its name says. Send the photo or scan itself (.jpg or .png), or a PDF.";

    internal const string TooLargeMessage =
        "The image is over 10 MB. Send a smaller photo or scan (a phone photo of a page is usually 2 to 5 MB).";
}
