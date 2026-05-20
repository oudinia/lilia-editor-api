namespace Lilia.Api.Services;

/// <summary>
/// Stripe configuration, bound from the <c>Stripe</c> config section.
/// Secrets come from user-secrets (local) or DO env vars (prod):
/// <c>Stripe__SecretKey</c>, <c>Stripe__WebhookSecret</c>.
///
/// An empty <see cref="SecretKey"/> means billing is not configured —
/// the checkout/portal endpoints return a clear 503 rather than crash,
/// so the API runs fine before Stripe is wired up.
/// </summary>
public class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";

    /// <summary>Stripe Checkout redirect targets. Stripe appends the
    /// session id to the success URL when it contains {CHECKOUT_SESSION_ID}.</summary>
    public string CheckoutSuccessUrl { get; set; } = "https://editor.liliaeditor.com/billing/success";
    public string CheckoutCancelUrl { get; set; } = "https://editor.liliaeditor.com/billing/cancel";

    /// <summary>Where the Stripe billing portal returns the user.</summary>
    public string PortalReturnUrl { get; set; } = "https://editor.liliaeditor.com/settings/billing";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
