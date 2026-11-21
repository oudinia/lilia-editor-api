using System.Diagnostics;

namespace Lilia.Api.Services;

/// <summary>
/// How long the parts of one bulk import chunk's placement took (21 Nov 2026, the bulk import timings): set by
/// <see cref="PdfImportPipeline.PlaceLatexProjectAsync"/> for the call it makes, added to by <see cref="JobService"/>
/// (the LaTeX staged, the finalize) and <see cref="ImportReviewService"/> (the figures stored). Ambient
/// (<see cref="AsyncLocal{T}"/>), so no signature in between changes; null, and nothing measured, everywhere else.
/// </summary>
public sealed class ImportPlaceTimer
{
    private static readonly AsyncLocal<ImportPlaceTimer?> Ambient = new();

    /// <summary>The placement being measured on this call path, or null.</summary>
    public static ImportPlaceTimer? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    /// <summary>The LaTeX read and staged for review: parse and cleanup.</summary>
    public long ParseMs { get; set; }

    /// <summary>The whole finalize into the document, figures included.</summary>
    public long FinalizeMs { get; set; }

    /// <summary>The figures stored as the document's assets (inside the finalize).</summary>
    public long FiguresMs { get; set; }

    public static long Since(long startTimestamp) => (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}
