using System.Text.Json;

namespace Lilia.Core.Entities;

public class Purchase
{
    public string Id { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string? UserId { get; set; }
    public string Type { get; set; } = string.Empty; // SUBSCRIPTION, ONE_TIME
    public string CustomerId { get; set; } = string.Empty;
    public string? SubscriptionId { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Organization? Organization { get; set; }
    public virtual User? User { get; set; }
}
