using System.Text.RegularExpressions;

namespace Lilia.Api.Security;

/// <summary>
/// What the development upload route accepts as a storage key: path segments of letters, digits,
/// dots, dashes and underscores (no "..", no absolute path), ending in an image or document type —
/// never .html or .svg, which the /uploads static files would serve as pages on the editor's domain.
/// </summary>
public static partial class LocalUploadKey
{
    public const long MaxBytes = 20 * 1024 * 1024;

    [GeneratedRegex(@"^(?:[A-Za-z0-9_-][A-Za-z0-9._-]{0,127}/){0,8}[A-Za-z0-9_-][A-Za-z0-9._-]{0,127}\.(?:png|jpe?g|gif|webp|pdf|eps|tiff?|bmp|bib|tex|txt|csv|docx|zip)$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();

    public static bool IsSafe(string? key) =>
        !string.IsNullOrEmpty(key) && !key.Contains("..", StringComparison.Ordinal) && Shape().IsMatch(key);
}
