namespace Lilia.Core.Entities;

/// <summary>
/// Running ledger of AI credit grants and spends for a user. Balance =
/// sum of Delta rows. Credits are granted on plan period start (+N) and
/// debited on AiRequest completion (-tokens_used).
///
/// Append-only. Never update rows in place — gives us a full audit
/// trail. Rebalance is just `SELECT SUM(delta) FROM ai_credit_ledger
/// WHERE user_id = :u`.
/// </summary>
public class AiCreditLedger
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Positive for grants, negative for spends. Integer credits
    /// (1 credit ≈ 1000 tokens, pricing model TBD).
    /// </summary>
    public int Delta { get; set; }

    // grant | spend | adjustment | refund — CHECK-constrained.
    public string Reason { get; set; } = "spend";

    /// <summary>Link to the AiRequest row that caused a spend (null for grants).</summary>
    public Guid? AiRequestId { get; set; }

    /// <summary>Free-text note — "monthly grant from Pro plan", etc.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
    public virtual AiRequest? AiRequest { get; set; }
}
