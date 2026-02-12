namespace Lilia.Import.Detection;

/// <summary>
/// Shared static set of monospace font families used for code block detection.
/// </summary>
public static class MonospaceFontList
{
    /// <summary>
    /// Default set of monospace font names (case-insensitive).
    /// </summary>
    public static readonly HashSet<string> Default = new(StringComparer.OrdinalIgnoreCase)
    {
        "Consolas",
        "Courier New",
        "Courier",
        "Monaco",
        "Menlo",
        "Lucida Console",
        "Liberation Mono",
        "DejaVu Sans Mono",
        "Source Code Pro",
        "Fira Code",
        "JetBrains Mono",
        "Cascadia Code",
        "Cascadia Mono",
        "Inconsolata",
        "Ubuntu Mono",
        "Noto Mono",
        "Roboto Mono",
        "IBM Plex Mono",
        "Hack",
        "Anonymous Pro",
        "PT Mono",
        "Droid Sans Mono"
    };
}
