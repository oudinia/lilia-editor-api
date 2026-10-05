using Lilia.Engines.TexSafety;

namespace Lilia.Engines.Themes;

/// <summary>
/// Whether this server can compile a theme: it is built (not planned) and every TeX file it
/// needs is installed. Measured once with <c>kpsewhich</c>, in the same scrubbed environment the
/// engines run in, and cached for the life of the process.
///
/// <para>This is the runtime safety net behind the fonts in the image. A theme that is not
/// available is refused on save and never emitted into a compile: it fails with a message
/// instead of printing in a substitute face.</para>
/// </summary>
public static class ThemeAvailability
{
    private static Lazy<Task<IReadOnlySet<string>>> _found = new(ProbeAsync);
    private static Func<string, bool>? _override;

    /// <summary>Start the probe early (at startup) so the first request does not pay for it.</summary>
    public static Task WarmUpAsync() => _found.Value;

    public static bool IsAvailable(string? themeId)
    {
        var theme = ThemeCatalog.Find(themeId);
        if (theme is null) return false;
        if (theme.Id == ThemeCatalog.Classic) return true;
        if (!theme.IsBuilt) return false;
        if (_override is { } o) return o(theme.Id);
        var found = _found.Value.GetAwaiter().GetResult();
        return theme.TexFiles.All(found.Contains);
    }

    /// <summary>Why a theme cannot be used here, or null when it can.</summary>
    public static string? WhyUnavailable(string? themeId)
    {
        var theme = ThemeCatalog.Find(themeId);
        if (theme is null) return $"'{themeId}' is not a theme. Valid values: {string.Join(", ", ThemeCatalog.Ids)}.";
        if (IsAvailable(theme.Id)) return null;
        var usable = string.Join(", ", ThemeCatalog.All.Where(t => IsAvailable(t.Id)).Select(t => t.Id));
        return theme.IsBuilt
            ? $"The {theme.Name} theme's fonts are not installed on this server, so it cannot be used yet. Available themes: {usable}."
            : $"The {theme.Name} theme is planned and not built yet. Available themes: {usable}.";
    }

    /// <summary>Tests: decide availability without kpsewhich. Null restores the real probe.</summary>
    public static void OverrideForTests(Func<string, bool>? available) => _override = available;

    private static async Task<IReadOnlySet<string>> ProbeAsync()
    {
        var files = ThemeCatalog.All.SelectMany(t => t.TexFiles).Distinct(StringComparer.Ordinal).ToList();
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count == 0) return found;
        var dir = Path.Combine(Path.GetTempPath(), $"lilia-kpse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // One process for every file: kpsewhich prints the path of each one it finds.
            var (_, stdout, _) = await TexProcessRunner.RunAsync("kpsewhich", string.Join(" ", files), dir, 30);
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                found.Add(Path.GetFileName(line));
        }
        catch
        {
            // No TeX on this host (or kpsewhich failed): nothing beyond Classic is available.
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
        return found;
    }
}
