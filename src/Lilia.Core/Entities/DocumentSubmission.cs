namespace Lilia.Core.Entities;

/// <summary>
/// A submission package someone downloaded (document_submissions, publisher templates phase 3, 25 Oct
/// 2026): the main.tex and references.bib it held, for which venue, with which class version. Never
/// changed once written (no endpoint updates or deletes one), so "what changed since the submission"
/// (the LaTeX diff, the latexdiff marked-up PDF) is always against what was really sent. Deleted with
/// its document.
/// </summary>
public class DocumentSubmission
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    /// <summary>Who downloaded it (an owner or a collaborator).</summary>
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? Profile { get; set; }
    public string? ClassVersion { get; set; }
    public string MainTex { get; set; } = "";
    public string? ReferencesBib { get; set; }
    /// <summary>SHA-256 of MainTex, as the manifest lists it.</summary>
    public string MainTexSha256 { get; set; } = "";
}
