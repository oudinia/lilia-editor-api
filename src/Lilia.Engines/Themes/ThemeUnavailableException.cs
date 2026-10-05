namespace Lilia.Engines.Themes;

/// <summary>
/// The document's theme cannot be compiled on this server (planned, or its fonts are not
/// installed). The compile stops with this message rather than printing in a substitute face.
/// </summary>
public sealed class ThemeUnavailableException(string message) : InvalidOperationException(message);
