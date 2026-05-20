namespace Lilia.Core.Entities;

/// <summary>
/// Append-only ledger of Stripe webhook events received. The UNIQUE
/// index on <see cref="StripeEventId"/> is the webhook dedup mechanism:
/// Stripe delivers events at-least-once, so the billing service inserts
/// a row and treats an already-processed row (or a unique violation) as
/// "skip". Also the payment audit trail — every event we acted on, and
/// any handler error, is recorded here.
/// </summary>
public class StripeEvent
{
    public Guid Id { get; set; }

    /// <summary>Stripe's event id, e.g. <c>evt_1A2b3C</c>. Unique.</summary>
    public string StripeEventId { get; set; } = string.Empty;

    /// <summary>Stripe event type, e.g. <c>customer.subscription.updated</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set once the handler completes successfully. Null = pending / failed.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Handler error message if processing failed; null when OK.</summary>
    public string? Error { get; set; }
}
