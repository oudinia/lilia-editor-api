namespace Lilia.Core.Entities;

public class User
{
    public string Id { get; set; } = string.Empty; // Clerk user ID
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Image { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Team> OwnedTeams { get; set; } = new List<Team>();
    public virtual ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
    public virtual ICollection<Document> OwnedDocuments { get; set; } = new List<Document>();
    public virtual ICollection<DocumentCollaborator> DocumentCollaborations { get; set; } = new List<DocumentCollaborator>();
    public virtual ICollection<Label> Labels { get; set; } = new List<Label>();
    public virtual ICollection<Template> Templates { get; set; } = new List<Template>();
    public virtual UserPreferences? Preferences { get; set; }
}
