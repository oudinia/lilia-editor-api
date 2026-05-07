namespace Lilia.Core.Entities;

/// <summary>
/// Join row for the M:N relationship between blocks and block_groups
/// (LILIA-136). Composite key is (BlockId, GroupId). Cascade-deletes
/// from either side.
///
/// We don't store the dimension here — it lives on the group. The
/// service-layer "one-group-per-dimension-per-block" constraint reads
/// the existing memberships, joins to groups, and rejects conflicts on
/// insert.
/// </summary>
public class BlockGroupMembership
{
    public Guid BlockId { get; set; }
    public Guid GroupId { get; set; }

    // Navigation properties
    public virtual Block Block { get; set; } = null!;
    public virtual BlockGroup Group { get; set; } = null!;
}
