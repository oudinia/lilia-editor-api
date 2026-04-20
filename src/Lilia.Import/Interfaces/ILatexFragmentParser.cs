using System.Text.Json;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Parses a single-block LaTeX fragment back into a block content JSONB shape.
/// Used by the v2 "edit block as LaTeX" round-trip surface (#68) —
/// POST /api/blocks/{id}/from-latex feeds the user's edited fragment here and
/// writes the result back into the Blocks.Content column.
///
/// Wraps the fragment in a minimal \documentclass{article}\begin{document}...
/// \end{document} shell and delegates to the full LatexParser — no new parser.
/// </summary>
public interface ILatexFragmentParser
{
    /// <summary>
    /// Parse <paramref name="latex"/> and return the block content JSONB for
    /// <paramref name="targetBlockType"/>.
    /// </summary>
    /// <param name="latex">Raw LaTeX fragment as typed by the user.</param>
    /// <param name="targetBlockType">Expected block type ("paragraph", "heading", "list", ...). If the parser produces a different top-level element type, throws <see cref="LatexFragmentParseException"/> with code TYPE_MISMATCH.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<JsonDocument> ParseFragmentAsync(string latex, string targetBlockType, CancellationToken ct = default);
}

/// <summary>
/// Thrown when a fragment cannot be mapped to the target block type.
/// The controller converts this to an HTTP 422 with diagnostics.
/// </summary>
public sealed class LatexFragmentParseException : Exception
{
    public string Code { get; }
    public IReadOnlyList<FragmentDiagnostic> Diagnostics { get; }

    public LatexFragmentParseException(string code, string message, IReadOnlyList<FragmentDiagnostic> diagnostics)
        : base(message)
    {
        Code = code;
        Diagnostics = diagnostics;
    }
}

public sealed record FragmentDiagnostic(
    int? Line,
    int? Column,
    string Severity,
    string Code,
    string Message);
