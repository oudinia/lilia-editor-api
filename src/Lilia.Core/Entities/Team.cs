namespace Lilia.Core.Entities;

public class Team
{
    public Guid Id { get; set; }
    /// <summary>
    /// Human display name — editable by the team owner. Defaults to
    /// <see cref="TeamCode"/> for teams that haven't been renamed.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Auto-generated research-lab-style codename (e.g. "Cobalt Photon
    /// A7B") set once at team creation. Unique across the system; used
    /// as a stable handle in URLs, emails, and the team picker. Users
    /// can roll a new code via the team settings; <see cref="Name"/>
    /// stays editable independently.
    /// </summary>
    public string TeamCode { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Image { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    /// <summary>
    /// Plan tier — "free" (default, ≤3 members) | "pro" | "team".
    /// Enforced in <c>TeamService.AddMember</c>; payment integration
    /// lives in lilia-cloud and syncs the plan via webhook.
    /// </summary>
    public string Plan { get; set; } = "free";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User Owner { get; set; } = null!;
    // Members nav removed when team_members table was retired
    // (see RetireTeamMembersTable migration). Members are now
    // reached via Groups → Group.Members (GroupMember).
    public virtual ICollection<Group> Groups { get; set; } = new List<Group>();
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
