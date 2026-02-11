namespace Lilia.Core.Entities;

public class User
{
    public string Id { get; set; } = string.Empty; // Clerk user ID
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Image { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Username { get; set; }
    public string? DisplayUsername { get; set; }
    public string? Role { get; set; }
    public bool Banned { get; set; }
    public string? BanReason { get; set; }
    public DateTime? BanExpires { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool? OnboardingComplete { get; set; }
    public string? PaymentsCustomerId { get; set; }
    public string? Locale { get; set; }

    // Navigation properties
    public virtual ICollection<Team> OwnedTeams { get; set; } = new List<Team>();
    public virtual ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
    public virtual ICollection<Document> OwnedDocuments { get; set; } = new List<Document>();
    public virtual ICollection<DocumentCollaborator> DocumentCollaborations { get; set; } = new List<DocumentCollaborator>();
    public virtual ICollection<Label> Labels { get; set; } = new List<Label>();
    public virtual ICollection<Template> Templates { get; set; } = new List<Template>();
    public virtual ICollection<Formula> Formulas { get; set; } = new List<Formula>();
    public virtual ICollection<Snippet> Snippets { get; set; } = new List<Snippet>();
    public virtual UserPreferences? Preferences { get; set; }
    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();
    public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();
    public virtual ICollection<Passkey> Passkeys { get; set; } = new List<Passkey>();
    public virtual ICollection<TwoFactor> TwoFactors { get; set; } = new List<TwoFactor>();
    public virtual ICollection<OrganizationMember> OrganizationMemberships { get; set; } = new List<OrganizationMember>();
    public virtual ICollection<Invitation> SentInvitations { get; set; } = new List<Invitation>();
    public virtual ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public virtual ICollection<AiChat> AiChats { get; set; } = new List<AiChat>();
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public virtual ICollection<SyncHistory> SyncHistories { get; set; } = new List<SyncHistory>();
}
