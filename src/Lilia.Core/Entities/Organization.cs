namespace Lilia.Core.Entities;

public class Organization
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Logo { get; set; }
    public string? Metadata { get; set; }
    public string? PaymentsCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
    public virtual ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public virtual ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public virtual ICollection<AiChat> AiChats { get; set; } = new List<AiChat>();
}
