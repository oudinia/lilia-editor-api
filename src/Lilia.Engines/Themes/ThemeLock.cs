namespace Lilia.Engines.Themes;

/// <summary>
/// Which themes a document's class may use (Document settings → Look).
///
/// <para>Classes that set their own look switch themes off with a reason rather than silently
/// ignoring them: the publishers' submission classes (the journal decides how a paper looks) and
/// the CV classes (their whole point is a fixed design). Their only theme is Classic.</para>
///
/// <para>Beamer is not locked (Olivia, 6 Oct 2026): a beamer document takes Classic (beamer's own
/// default) or a beamer theme, today Exposition, loaded with <c>\usetheme</c>. The document
/// themes built in <c>lilia-theme.sty</c> are for every other class, and a beamer theme is for
/// beamer only.</para>
///
/// <para>A class change is never refused for the look: the stored look is kept and a theme the
/// new class cannot use falls back to Classic when the LaTeX is written
/// (<see cref="DocumentLook.ForClass"/>), so switching back restores it.</para>
/// </summary>
public static class ThemeLock
{
    public const string Beamer = "beamer";

    /// <summary>PUT refuses Exposition on a class that is not beamer with this sentence.</summary>
    public const string ExpositionNeedsBeamer = "Exposition is a beamer theme: switch the class to beamer to use it.";

    /// <summary>PUT refuses a document theme on a beamer document with this sentence.</summary>
    public const string BeamerTakesClassicOrExposition = "Beamer decks take Classic or Exposition.";

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
        // Posters: beamerposter's page is a poster, not a deck; Exposition is not drawn for it.
        "beamerposter",
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

    /// <summary>Whether the document is a beamer deck (the class is <c>beamer</c>).</summary>
    public static bool IsBeamer(string? documentClass) =>
        string.Equals(documentClass?.Trim(), Beamer, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The theme ids this class may use, in catalog order (<c>lookThemes</c> on the document):
    /// a locked class takes Classic only; beamer takes Classic and the beamer themes
    /// (<c>["classic","exposition"]</c>); every other class takes every theme but the beamer ones.
    /// Availability on this server is a separate question (<c>available</c> on GET /api/themes).
    /// </summary>
    public static IReadOnlyList<string> ThemesFor(string? documentClass)
    {
        if (Reason(documentClass) is not null) return new[] { ThemeCatalog.Classic };
        var beamer = IsBeamer(documentClass);
        return ThemeCatalog.All
            .Where(t => t.Id == ThemeCatalog.Classic || t.IsBeamerTheme == beamer)
            .Select(t => t.Id)
            .ToList();
    }

    /// <summary>Whether this class may use this theme.</summary>
    public static bool Allows(string? documentClass, string? themeId) =>
        ThemeCatalog.Find(themeId) is { } theme && ThemesFor(documentClass).Contains(theme.Id);

    /// <summary>
    /// Why this class may not use this theme, or null when it may: the lock's reason for a locked
    /// class, <see cref="ExpositionNeedsBeamer"/> for a beamer theme elsewhere,
    /// <see cref="BeamerTakesClassicOrExposition"/> for a document theme on beamer.
    /// </summary>
    public static string? WhyNot(string? documentClass, string? themeId)
    {
        var theme = ThemeCatalog.Find(themeId);
        if (theme is null || Allows(documentClass, theme.Id)) return null;
        if (Reason(documentClass) is { } locked) return locked;
        return theme.IsBeamerTheme ? ExpositionNeedsBeamer : BeamerTakesClassicOrExposition;
    }
}
