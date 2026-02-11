namespace Lilia.Core.Entities;

public class Passkey
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public string CredentialId { get; set; } = string.Empty;
    public int Counter { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public bool BackedUp { get; set; }
    public string? Transports { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? Aaguid { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
}
