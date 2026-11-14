namespace Lilia.Core.Entities;

/// <summary>
/// One movement of an author's connector operations (the owner, 14 Nov 2026: per-usage packs, $5 for 300 operations,
/// never expiring, bonus from 4 and 10 packs). Balance = the sum of <see cref="Delta"/>. A purchase adds, a check, an
/// export or an import through the connector past the free daily allowance spends one, and a spend whose call failed
/// is refunded. Append-only, like the AI credits ledger.
/// </summary>
public class OperationCredit
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    /// <summary>Positive for a purchase or a refund, negative for a spend.</summary>
    public int Delta { get; set; }

    /// <summary>purchase | spend | refund | adjustment.</summary>
    public string Reason { get; set; } = "spend";

    /// <summary>The connector tool a spend paid for (check_document, export_document, import_document).</summary>
    public string? Tool { get; set; }

    /// <summary>The Stripe Checkout session a purchase came from: unique, so a webhook delivered twice credits once.</summary>
    public string? StripeSessionId { get; set; }

    /// <summary>The spend a refund gives back.</summary>
    public Guid? RefundOf { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
}
