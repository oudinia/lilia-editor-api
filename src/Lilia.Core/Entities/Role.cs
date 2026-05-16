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

    // Single normalization point. The Roles table stores lowercase
    // canonical names; clients in the wild send "Editor", "Member",
    // "Admin" — anything not matching exactly bombs the role lookup
    // and the surrounding flow returns a generic "Failed to add"
    // error with no clue what went wrong. Aliases "admin"/"member"
    // come from the old team-roles dropdown and pre-launch share
    // dialog. Apply on every Roles.FirstOrDefaultAsync(r => r.Name ==
    // …) site (currently CollaboratorService + TeamService).
    public static string Normalize(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "member" or "admin" or "" => Editor,
            var x => x,
        };
}

public static class Permissions
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Delete = "delete";
    public const string Manage = "manage";
    public const string Transfer = "transfer";
}
