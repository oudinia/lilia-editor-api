using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Something the operator did to an account from the admin console (admin_actions, 17 Oct 2026):
/// who, what, to whom, why. Written in the same transaction as the change itself, and shown on
/// the account's page; nothing the console changes goes unrecorded.
/// </summary>
public class AdminAction
{
    public Guid Id { get; set; }
    public string AdminEmail { get; set; } = "";
    /// <summary>grant_credits, set_plan, suspend, unsuspend, resend_verification, feedback_status.</summary>
    public string Action { get; set; } = "";
    public string? TargetUserId { get; set; }
    public string Reason { get; set; } = "";
    public JsonDocument? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
