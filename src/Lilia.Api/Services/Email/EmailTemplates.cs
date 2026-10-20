using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services.Email;

public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// The emails Olivia designed on 17 Oct (design-handoffs/2026-10-17-email-design): white, ink, one blue,
/// the L mark, in en/fr/es. The generated files are embedded as they are (<c>Emails/{lang}.{key}.html|txt</c>; language first, or MSBuild reads a culture suffix)
/// and <c>subjects.json</c>); this fills their <c>{{variables}}</c> and <c>{{#if x}}…{{/if}}</c> blocks.
/// HTML values are escaped; the plain-text part gets them as they are.
/// </summary>
public static partial class EmailTemplates
{
    public static readonly string[] Languages = ["en", "fr", "es"];

    /// <summary>Where the designs point; replaced by <c>Email:BaseUrl</c> when that differs (the test copy).</summary>
    public const string DesignBaseUrl = "https://editor.liliaeditor.com";

    private static readonly ConcurrentDictionary<string, string> Files = new();
    private static readonly Lazy<JsonDocument> Subjects = new(() => JsonDocument.Parse(Read("subjects.json")));

    /// <summary>en, fr or es from a locale such as "fr-FR"; anything else is English.</summary>
    public static string Language(string? locale)
    {
        var two = (locale ?? "").Trim().ToLowerInvariant();
        two = two.Length >= 2 ? two[..2] : two;
        return Languages.Contains(two) ? two : "en";
    }

    public static bool Exists(string key) =>
        typeof(EmailTemplates).Assembly.GetManifestResourceNames().Contains($"Emails.en.{key}.html");

    public static RenderedEmail Render(string key, string? locale, IReadOnlyDictionary<string, string?> values, string? baseUrl = null)
    {
        var lang = Language(locale);
        var subject = Subjects.Value.RootElement.GetProperty(key).GetProperty(lang).GetProperty("subject").GetString()!;
        var html = Fill(Read($"{lang}.{key}.html"), values, encode: true);
        var text = Fill(Read($"{lang}.{key}.txt"), values, encode: false);
        var origin = (baseUrl ?? DesignBaseUrl).TrimEnd('/');
        if (origin != DesignBaseUrl)
        {
            html = html.Replace(DesignBaseUrl, origin);
            text = text.Replace(DesignBaseUrl, origin);
        }
        return new RenderedEmail(subject, html, text);
    }

    public static string Fill(string template, IReadOnlyDictionary<string, string?> values, bool encode)
    {
        string? Value(string name) => values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var s = IfBlock().Replace(template, m => Value(m.Groups[1].Value) is null ? "" : m.Groups[2].Value);
        // "Welcome to Lilia, {{first_name}}" reads "Welcome to Lilia" without a name.
        if (Value("first_name") is null) s = s.Replace(", {{first_name}}", "");
        // The publisher line waits on the owner's details (LEGAL-6): no empty paragraph meanwhile.
        if (Value("publisher_line") is null)
        {
            s = EmptyPublisherParagraph().Replace(s, "");
            s = s.Replace("\n{{publisher_line}}", "");
        }
        return Variable().Replace(s, m =>
        {
            var v = Value(m.Groups[1].Value) ?? "";
            return encode ? WebUtility.HtmlEncode(v) : v;
        });
    }

    private static string Read(string name) => Files.GetOrAdd(name, n =>
    {
        using var stream = typeof(EmailTemplates).Assembly.GetManifestResourceStream($"Emails.{n}")
            ?? throw new InvalidOperationException($"Email template Emails.{n} is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    [GeneratedRegex(@"\{\{#if ([a-z_]+)\}\}(.*?)\{\{/if\}\}", RegexOptions.Singleline)]
    private static partial Regex IfBlock();

    [GeneratedRegex(@"\{\{([a-z_]+)\}\}")]
    private static partial Regex Variable();

    [GeneratedRegex(@"<p[^>]*>\{\{publisher_line\}\}</p>")]
    private static partial Regex EmptyPublisherParagraph();
}
