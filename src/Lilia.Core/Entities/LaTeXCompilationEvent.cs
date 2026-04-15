namespace Lilia.Core.Entities;

/// <summary>
/// Records every LaTeX compilation/validation attempt for error frequency analysis.
/// Populated by LaTeXRenderService on every validate/render call.
/// Used to surface frequent error patterns to users (guidance) and operators (triage).
/// </summary>
public class LaTeXCompilationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Document being compiled, if known.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Block being validated, if this is a block-level validation.</summary>
    public Guid? BlockId { get; set; }

    /// <summary>Block type (equation, theorem, code, …), if block-level.</summary>
    public string? BlockType { get; set; }

    /// <summary>validate | render_pdf | render_png | render_svg | render_block</summary>
    public string EventType { get; set; } = "validate";

    public bool Success { get; set; }

    /// <summary>Raw error lines from pdflatex .log (first 1 000 chars).</summary>
    public string? ErrorRaw { get; set; }

    /// <summary>
    /// Classified error category:
    ///   undefined_control_sequence | undefined_environment | missing_package |
    ///   math_mode_error | environment_mismatch | missing_file | syntax_error |
    ///   bibliography_error | timeout | unknown
    /// </summary>
    public string? ErrorCategory { get; set; }

    /// <summary>
    /// The specific offending token extracted from the error (e.g. "\missingcmd",
    /// "theorem", "subfigure"). Null when not extractable.
    /// </summary>
    public string? ErrorToken { get; set; }

    /// <summary>Line number in the compiled .tex file where the error occurred.</summary>
    public int? ErrorLine { get; set; }

    public int WarningCount { get; set; }
    public int DurationMs { get; set; }

    public string? UserId { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
