namespace Lilia.Core.Entities;

/// <summary>
/// Many-to-many join between users and teams. Every user has at
/// least one row in this table (their default team, auto-created
/// on user.created). Owner of the team is also a member (with the
/// "owner" role) — there's no implicit owner relationship through
/// <see cref="Team.OwnerId"/>; explicit membership is the source
/// of truth for "who can access this team".
///
/// Free-plan enforcement: <c>TeamService.AddMember</c> rejects when
/// adding a 4th member to a team with <see cref="Team.Plan"/> = "free".
/// </summary>
public class TeamMember
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string UserId { get; set; } = string.Empty;
    /// <summary>"owner" | "admin" | "member" | "viewer".</summary>
    public string Role { get; set; } = "member";
    public string? InvitedBy { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Team Team { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual User? Inviter { get; set; }
}
