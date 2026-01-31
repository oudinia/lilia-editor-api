namespace Lilia.Core.Entities;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // owner, editor, viewer
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();

    // Navigation properties
    public virtual ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
    public virtual ICollection<DocumentCollaborator> DocumentCollaborators { get; set; } = new List<DocumentCollaborator>();
    public virtual ICollection<DocumentGroup> DocumentGroups { get; set; } = new List<DocumentGroup>();
}

public static class RoleNames
{
    public const string Owner = "owner";
    public const string Editor = "editor";
    public const string Viewer = "viewer";
}

public static class Permissions
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Delete = "delete";
    public const string Manage = "manage";
    public const string Transfer = "transfer";
}
