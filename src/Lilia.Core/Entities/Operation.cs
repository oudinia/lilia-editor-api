namespace Lilia.Core.Entities;

/// <summary>
/// One operation an author took away from Lilia (10 Nov 2026, the owner's "start 2B immediately": paying per
/// operation). Today an export: a file downloaded from the export screens or through the connector's link. Kept
/// whatever the plan, so a per-operation price can bill what was really taken and Settings can say how many this
/// month. Append-only. The editor's live PDF preview is not an export: only a request that says it is a download
/// (<c>download=true</c>) or a connector download counts.
/// </summary>
public class Operation
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? DocumentId { get; set; }

    /// <summary>export (CHECK-constrained; more kinds when the owner decides what counts).</summary>
    public string Kind { get; set; } = "export";

    /// <summary>pdf | tex | zip | docx | markdown | html | lml.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>screens | connector.</summary>
    public string Source { get; set; } = "screens";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
