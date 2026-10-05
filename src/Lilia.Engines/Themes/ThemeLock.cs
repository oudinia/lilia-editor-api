namespace Lilia.Engines.Themes;

/// <summary>
/// Classes that set their own look, where a document theme is switched off with a reason rather
/// than silently ignored: the publishers' submission classes (the journal decides how a paper
/// looks), the CV classes (their whole point is a fixed design), and beamer, whose own theme
/// system styles every frame (Exposition, the planned beamer theme, will revisit this).
/// </summary>
public static class ThemeLock
{
    private static readonly HashSet<string> Locked = new(StringComparer.OrdinalIgnoreCase)
    {
        // Publishers
        "IEEEtran", "acmart", "llncs", "elsarticle", "revtex4", "revtex4-1", "revtex4-2",
        "aastex", "aastex62", "aastex63", "aastex631", "aastex7", "aastex701", "mnras", "aa",
        "svjour3", "svjour", "sn-jnl", "jfm", "achemso", "pnas-new", "pnas", "frontiersSCNS", "frontiersHLTH",
        "frontiersFPHY", "wlscirep", "sigplanconf", "sig-alternate", "copernicus", "elsart", "iopart",
        "imsart", "jss", "ametsoc", "agujournal2019", "plos2015",
        // CVs
        "moderncv", "altacv", "awesome-cv", "europecv",
        // Slides
        "beamer", "beamerposter",
    };

    /// <summary>
    /// The sentence the editor shows when the class locks the look, or null when themes apply:
    /// <c>"IEEEtran sets its own look, so themes are off for this document."</c>
    /// </summary>
    public static string? Reason(string? documentClass)
    {
        var cls = documentClass?.Trim();
        if (string.IsNullOrEmpty(cls)) return null;
        var locked = Locked.Contains(cls)
                     || cls.StartsWith("revtex", StringComparison.OrdinalIgnoreCase)
                     || cls.StartsWith("aastex", StringComparison.OrdinalIgnoreCase);
        return locked ? $"{cls} sets its own look, so themes are off for this document." : null;
    }
}
