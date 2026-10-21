using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Lilia.Api.Services.Email;

/// <summary>
/// What an email is for, which decides how it travels (the user, 17 Oct: "a gateway to switch
/// provider easily"). Account emails are the ones a person asked for or needs to get in — sign-in,
/// verify, password reset, welcome, export ready, account closed — and must never carry an
/// Unsubscribe link (one click there would block the next reset). Notifications are everything
/// sent because of what someone else did (sharing, teams), where an Unsubscribe link is right.
/// </summary>
public enum EmailKind { Account, Notification }

/// <param name="Headers">Extra headers, e.g. List-Unsubscribe on a tips email.</param>
public sealed record OutgoingEmail(string To, string Subject, string Html, string? Text, string? ReplyTo, EmailKind Kind,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>One way to send an email. Adding a provider is one class and one name in the settings.</summary>
public interface IEmailProvider
{
    /// <summary>The name used in the routes (<c>Email__AccountRoute=resend,smtp</c>).</summary>
    string Name { get; }
    /// <summary>False when its settings are missing: the gateway skips it instead of failing.</summary>
    bool IsConfigured { get; }
    Task SendAsync(OutgoingEmail email, CancellationToken ct = default);
}

/// <summary>
/// Sends each email through the providers of its kind's route, in order, until one accepts it.
/// Routes come from the settings (<see cref="EmailSettings.AccountRoute"/>,
/// <see cref="EmailSettings.NotificationRoute"/>); unset, both use <see cref="EmailSettings.Transport"/>,
/// which is how the API sent before the gateway. Logs which provider sent every email, and a warning
/// whenever it had to fall back, so a provider going down shows up in the error tracker.
/// </summary>
public sealed class EmailGateway(IEnumerable<IEmailProvider> providers, EmailSettings settings, ILogger<EmailGateway> logger)
{
    private readonly Dictionary<string, IEmailProvider> _byName =
        providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Route(EmailKind kind)
    {
        var raw = kind == EmailKind.Account ? settings.AccountRoute : settings.NotificationRoute;
        if (string.IsNullOrWhiteSpace(raw)) raw = settings.Transport;
        if (string.IsNullOrWhiteSpace(raw)) raw = "resend";
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLowerInvariant()).Distinct().ToList();
    }

    /// <summary>Sends, and returns the name of the provider that took it.</summary>
    public async Task<string> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var route = Route(email.Kind);
        Exception? last = null;
        foreach (var name in route)
        {
            if (!_byName.TryGetValue(name, out var provider))
            {
                logger.LogError("Email route for {Kind} names an unknown provider {Provider}", email.Kind, name);
                continue;
            }
            if (!provider.IsConfigured)
            {
                logger.LogWarning("Email provider {Provider} is not configured; skipping it for {Kind} email {Subject}", name, email.Kind, email.Subject);
                continue;
            }
            try
            {
                await provider.SendAsync(email, ct);
                if (name != route[0])
                    logger.LogWarning("Email {Subject} ({Kind}) sent through fallback {Provider} after {First} failed", email.Subject, email.Kind, name, route[0]);
                else
                    logger.LogInformation("Email {Subject} ({Kind}) sent to {To} via {Provider}", email.Subject, email.Kind, email.To, name);
                return name;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                logger.LogWarning(ex, "Email provider {Provider} failed for {Kind} email {Subject}", name, email.Kind, email.Subject);
            }
        }
        logger.LogError(last, "No email provider could send {Kind} email {Subject} to {To} (route: {Route})", email.Kind, email.Subject, email.To, string.Join(",", route));
        throw new InvalidOperationException($"Email not sent: no provider in [{string.Join(",", route)}] accepted it.", last);
    }
}

/// <summary>Resend's HTTP API. Adds no List-Unsubscribe header (Resend leaves headers to the sender).</summary>
public sealed class ResendEmailProvider(EmailSettings settings, IHttpClientFactory http) : IEmailProvider
{
    public string Name => "resend";
    public bool IsConfigured => !string.IsNullOrEmpty(settings.ResendApiKey);

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from"] = $"{settings.FromName} <{settings.FromAddress}>",
            ["to"] = new[] { email.To },
            ["subject"] = email.Subject,
            ["html"] = email.Html,
            ["text"] = email.Text,
        };
        if (!string.IsNullOrEmpty(email.ReplyTo)) payload["reply_to"] = email.ReplyTo;
        if (email.Headers is { Count: > 0 }) payload["headers"] = email.Headers;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.ResendApiBase.TrimEnd('/')}/emails")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
        using var res = await http.CreateClient("email-resend").SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Resend refused the email: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
    }
}

/// <summary>Any SMTP server: Brevo in production, Mailpit in development and tests.</summary>
public sealed class SmtpEmailProvider(EmailSettings settings) : IEmailProvider
{
    public string Name => "smtp";
    public bool IsConfigured => !string.IsNullOrEmpty(settings.SmtpHost);

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        if (!string.IsNullOrEmpty(email.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        foreach (var (name, value) in email.Headers ?? new Dictionary<string, string>()) message.Headers.Add(name, value);
        message.Subject = email.Subject;
        var body = new BodyBuilder { HtmlBody = email.Html };
        if (!string.IsNullOrEmpty(email.Text)) body.TextBody = email.Text;
        message.Body = body.ToMessageBody();

        var security = (settings.SmtpSecurity ?? "none").Trim().ToLowerInvariant() switch
        {
            "starttls" => SecureSocketOptions.StartTls,
            "ssl" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.None,
        };
        using var client = new SmtpClient();
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, ct);
        if (!string.IsNullOrEmpty(settings.SmtpUsername))
            await client.AuthenticateAsync(settings.SmtpUsername, settings.SmtpPassword ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Drops the email after logging it: unit tests, and a server that must send nothing.</summary>
public sealed class NoopEmailProvider(ILogger<NoopEmailProvider> logger) : IEmailProvider
{
    public string Name => "noop";
    public bool IsConfigured => true;
    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        logger.LogInformation("[noop] would send {Kind} email to {To}: {Subject}", email.Kind, email.To, email.Subject);
        return Task.CompletedTask;
    }
}
