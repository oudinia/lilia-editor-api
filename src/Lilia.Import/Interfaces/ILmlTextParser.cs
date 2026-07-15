using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Parses human-readable LML text (spec: @block[attrs] + indented body)
/// into typed block payloads ready for document import.
/// </summary>
public interface ILmlTextParser
{
    /// <summary>
    /// True when content looks like text LML (not JSON document export).
    /// </summary>
    bool LooksLikeTextLml(string source);

    LmlTextParseResult Parse(string source);
}
