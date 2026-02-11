namespace Lilia.Core.Entities;

public class TwoFactor
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string BackupCodes { get; set; } = string.Empty;

    // Navigation properties
    public virtual User User { get; set; } = null!;
}
