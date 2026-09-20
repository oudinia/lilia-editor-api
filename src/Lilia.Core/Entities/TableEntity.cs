using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// An author's table, owned independently of any document.
/// </summary>
/// <remarks>
/// Named <c>TableEntity</c> rather than <c>Table</c> because <c>Table</c> collides
/// with EF's own vocabulary and with <c>System.Data</c> in enough places to be a
/// nuisance; it maps to the <c>tables</c> table.
///
/// A document uses a table by <b>reference</b>, never by copy — see
/// <see cref="DocumentTable"/>. One row, however many papers it appears in.
/// </remarks>
public class TableEntity
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The table's name. Empty is normal — the listing shows a preview.</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary><c>\label{tab:…}</c>, stored with its prefix.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The same shape a <c>table</c> block's content has, so the editor's
    /// <c>blockToTableData</c> reads it unchanged.
    /// </summary>
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tables have their own recency. Blocks never did.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The table this one was copied from, when it was made by detaching rather
    /// than from scratch.
    /// </summary>
    /// <remarks>
    /// Olivia's rule: the choice happens at insert, not at divergence — link by
    /// default, because copying is the recoverable mistake of the two. When
    /// someone does take a copy, this records where it came from, because a
    /// reference model dies of forking invisibly.
    /// </remarks>
    public Guid? CopiedFrom { get; set; }

    public ICollection<DocumentTable> Documents { get; set; } = new List<DocumentTable>();
    public ICollection<TableCollaborator> Collaborators { get; set; } = new List<TableCollaborator>();
}

/// <summary>Which documents reference which tables. A reference, never a copy.</summary>
public class DocumentTable
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid TableId { get; set; }

    /// <summary>The block rendering this table in that document, when there is one.</summary>
    public Guid? BlockId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Document? Document { get; set; }
    public TableEntity? Table { get; set; }
}

/// <summary>Sharing, mirroring <see cref="DocumentCollaborator"/>.</summary>
public class TableCollaborator
{
    public Guid Id { get; set; }
    public Guid TableId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string? InvitedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TableEntity? Table { get; set; }
}
