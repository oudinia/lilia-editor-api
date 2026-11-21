namespace Lilia.Api.Security;

/// <summary>
/// What the /uploads route will look up (21 Nov 2026, files move to a private R2 bucket and every read goes through
/// the API). The route was static files over a folder; now that it names objects in a store, a key is checked before
/// the store sees it: no "..", no absolute path, no backslash, no empty segment, no control character. Anything else
/// passes, so a figure whose kept name has an odd extension (a zip's "fig.v 2") still loads.
/// </summary>
public static class StorageKeys
{
    /// <summary>
    /// Kept files a browser never loads by URL: the originals of imports (<c>imports/{job}.zip|pdf|docx|epub</c>) and
    /// the bulk import's PDFs, results and staged figures (<c>bulk-staging/</c>, served by its own signed-in endpoint).
    /// They were reachable at /uploads/… by anyone holding the job's id while /uploads was static files; the API reads
    /// them through the store, so the route no longer serves them.
    /// </summary>
    private static readonly string[] NotServed = ["imports/", "bulk-imports/", "bulk-staging/"];

    public static bool IsSafe(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= 1024
        && !key.Contains("..", StringComparison.Ordinal) && !key.Contains("//", StringComparison.Ordinal)
        && key[0] != '/' && key[^1] != '/' && !key.Contains('\\')
        && !key.Any(char.IsControl);

    /// <summary>A key the /uploads route serves: safe, and not one of the kept originals.</summary>
    public static bool IsServed(string? key) =>
        IsSafe(key) && !NotServed.Any(p => key!.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
