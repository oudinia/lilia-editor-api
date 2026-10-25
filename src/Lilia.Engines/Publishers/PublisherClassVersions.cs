using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Lilia.Engines.Publishers;

/// <summary>
/// The version of each publisher class installed on this server (publisher templates phase 3, the
/// template update service, 25 Oct 2026; requirements NFR-5 and NFR-7): read from the class file's own
/// <c>\ProvidesClass{name}[date version …]</c> line (elsarticle builds it from <c>\RCSdate</c> and
/// <c>\RCSversion</c>), found with kpsewhich in the TeX sandbox.
///
/// <para>A document remembers the version it was given when its venue was chosen
/// (<c>Document.PublisherTemplateVersion</c>); when the installed class moves on, the check says so,
/// and nothing about the document changes by itself. Read once per process: a class changes with a
/// TeX Live update or an install in TEXMFLOCAL, both followed by a restart.</para>
/// </summary>
public static partial class PublisherClassVersions
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.Ordinal);

    /// <summary>Tests: decide versions without TeX. Null restores the real reading.</summary>
    public static void OverrideForTests(Func<string, string?>? versionOf)
    {
        _override = versionOf;
        Cache.Clear();
    }
    private static Func<string, string?>? _override;

    /// <summary>"2026/08/16 v2.20" for acmart, or null when the class isn't found.</summary>
    public static async Task<string?> InstalledAsync(string cls)
    {
        if (_override is not null) return _override(cls);
        if (Cache.TryGetValue(cls, out var kept)) return kept;
        string? version = null;
        var dir = Path.Combine(Path.GetTempPath(), $"lilia-clsver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (exit, stdout, _) = await TexSafety.TexProcessRunner.RunAsync("kpsewhich", $"{cls}.cls", dir, 20);
            var path = stdout.Trim();
            if (exit == 0 && path.EndsWith($"{cls}.cls", StringComparison.Ordinal) && File.Exists(path))
                version = Read(await File.ReadAllTextAsync(path));
        }
        catch { /* no TeX here: unknown */ }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
        return Cache[cls] = version;
    }

    /// <summary>Every publisher class's installed version (null when not found).</summary>
    public static async Task<IReadOnlyDictionary<string, string?>> AllAsync()
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var cls in PublisherClasses.All) result[cls] = await InstalledAsync(cls);
        return result;
    }

    /// <summary>The date and version from a class file's text: "2026/08/16 v2.20".</summary>
    public static string? Read(string classText)
    {
        // elsarticle: \ProvidesClass{\@shortjid}[\RCSdate, \RCSversion: …] with both defined above.
        if (RcsDate().Match(classText) is { Success: true } d && RcsVersion().Match(classText) is { Success: true } v)
            return $"{d.Groups[1].Value} v{v.Groups[1].Value}";
        if (Provides().Match(classText) is not { Success: true } m) return null;
        var info = m.Groups["info"].Value.Trim();
        // "2026/08/16 v2.20 Typesetting…" → date and the first version-looking token.
        var date = DateToken().Match(info);
        var ver = VersionToken().Match(info, date.Success ? date.Index + date.Length : 0);
        return (date.Success, ver.Success) switch
        {
            (true, true) => $"{date.Value} {(ver.Value.StartsWith('v') || ver.Value.StartsWith('V') ? "v" + ver.Value[1..] : "v" + ver.Value)}",
            (true, false) => date.Value,
            _ => null,
        };
    }

    /// <summary>
    /// The version part ("2.20") of a version string ("2026/08/16 v2.20", or a profile's
    /// "acmart 2.20"), to compare the two.
    /// </summary>
    public static string? Number(string? version) =>
        version is null ? null : NumberToken().Match(version) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// Whether two version strings name the same release: their version numbers when both have one
    /// ("v2.20" and "acmart 2.20"), else their dates (iopjournal has only a date).
    /// </summary>
    public static bool Same(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var (na, nb) = (Number(a), Number(b));
        if (na is not null && nb is not null) return na == nb;
        var (da, db) = (DateToken().Match(a), DateToken().Match(b));
        return da.Success && db.Success && da.Value == db.Value;
    }

    [GeneratedRegex(@"\\ProvidesClass\s*\{[^}]*\}\s*\[(?<info>[^\]]*)\]")]
    private static partial Regex Provides();
    [GeneratedRegex(@"\\def\\RCSdate\{([^}]*)\}")]
    private static partial Regex RcsDate();
    [GeneratedRegex(@"\\def\\RCSversion\{([^}]*)\}")]
    private static partial Regex RcsVersion();
    [GeneratedRegex(@"\d{4}/\d{2}/\d{2}")]
    private static partial Regex DateToken();
    [GeneratedRegex(@"[vV]?\d+(?:\.\d+)+[a-z]?")]
    private static partial Regex VersionToken();
    [GeneratedRegex(@"(?:^|\s)[vV]?(\d+(?:\.\d+)+[a-z]?)(?:\s|$)")]
    private static partial Regex NumberToken();
}
