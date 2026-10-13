using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lilia.Import.Services;

/// <summary>
/// The field names an app or a model guesses for a CV block, mapped to the ones Lilia prints
/// (MCP-58): <c>firstName</c>/<c>lastName</c>, <c>organization</c>, <c>startDate</c>/<c>endDate</c>
/// were stored and printed nothing. personalInfo is <c>{name, headline, email, phones:[{number}],
/// homepage, location}</c>; cvEntry is <c>{period, role, org, location, highlight, description, tech:[]}</c>.
/// A field Lilia already has wins over its alias; a used alias is removed.
/// </summary>
public static class CvFields
{
    private static readonly string[] Org = ["organization", "organisation", "company", "employer", "institution", "school", "university"];
    private static readonly string[] Role = ["title", "position", "jobTitle", "degree"];
    private static readonly string[] Start = ["startDate", "start", "from", "dateStart"];
    private static readonly string[] End = ["endDate", "end", "to", "until", "dateEnd"];
    private static readonly string[] Period = ["date", "dates", "years"];
    private static readonly string[] Description = ["summary", "details"];
    private static readonly string[] Location = ["city", "place"];
    private static readonly string[] Headline = ["title", "jobTitle", "position", "tagline"];
    private static readonly string[] Homepage = ["website", "url", "web", "site"];
    private static readonly string[] Phone = ["phone", "phoneNumber", "telephone", "tel", "mobile"];

    /// <summary>The content with the aliases mapped, or the same element when nothing changes.</summary>
    public static JsonElement Normalize(string? type, JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object || type is null) return content;
        var cv = type.Equals("cvEntry", StringComparison.OrdinalIgnoreCase);
        var person = type.Equals("personalInfo", StringComparison.OrdinalIgnoreCase);
        if (!cv && !person) return content;
        if (JsonNode.Parse(content.GetRawText()) is not JsonObject o) return content;
        var changed = cv ? Entry(o) : Person(o);
        return changed ? JsonSerializer.SerializeToElement(o) : content;
    }

    /// <summary>"2020 – 2024", "2020 – " when only the start is known, "– 2024" for only the end.</summary>
    public static string PeriodOf(string? start, string? end)
    {
        start = start?.Trim() ?? "";
        end = end?.Trim() ?? "";
        if (start.Length == 0 && end.Length == 0) return "";
        if (end.Length == 0) return $"{start} –";
        if (start.Length == 0) return $"– {end}";
        return $"{start} – {end}";
    }

    private static bool Entry(JsonObject o)
    {
        var changed = Move(o, "org", Org) | Move(o, "role", Role) | Move(o, "description", Description) | Move(o, "location", Location);
        if (IsEmpty(o, "period"))
        {
            var start = Take(o, Start);
            var end = Take(o, End);
            var period = start is null && end is null ? Take(o, Period) : PeriodOf(start, end);
            if (!string.IsNullOrEmpty(period)) { o["period"] = period; changed = true; }
        }
        // "tech": "C#, SQL" → ["C#", "SQL"]
        if (o["tech"] is JsonValue tv && tv.GetValueKind() == JsonValueKind.String)
        {
            o["tech"] = new JsonArray(tv.GetValue<string>().Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
            changed = true;
        }
        return changed;
    }

    private static bool Person(JsonObject o)
    {
        var changed = false;
        if (IsEmpty(o, "name"))
        {
            var full = Take(o, ["fullName", "full_name"]);
            if (full is null)
            {
                var first = Take(o, ["firstName", "givenName", "first_name"]);
                var last = Take(o, ["lastName", "familyName", "surname", "last_name"]);
                full = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            }
            if (!string.IsNullOrEmpty(full)) { o["name"] = full; changed = true; }
        }
        changed |= Move(o, "headline", Headline) | Move(o, "homepage", Homepage) | Move(o, "location", ["address", "city"]) | Move(o, "email", ["mail", "emailAddress"]);

        // phones: a string, strings, or a phone field → [{number}]
        if (o["phones"] is JsonValue pv && pv.GetValueKind() == JsonValueKind.String)
        {
            o["phones"] = Numbers(pv.GetValue<string>().Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            changed = true;
        }
        else if (o["phones"] is JsonArray pa && pa.Any(p => p is JsonValue))
        {
            o["phones"] = new JsonArray(pa.Select(p => p is JsonValue v && v.GetValueKind() == JsonValueKind.String
                ? new JsonObject { ["number"] = v.GetValue<string>() }
                : p?.DeepClone()).ToArray());
            changed = true;
        }
        if (o["phones"] is null || (o["phones"] is JsonArray empty && empty.Count == 0))
        {
            var phone = Take(o, Phone);
            if (!string.IsNullOrWhiteSpace(phone)) { o["phones"] = Numbers([phone.Trim()]); changed = true; }
        }
        return changed;
    }

    private static JsonArray Numbers(IEnumerable<string> numbers) =>
        new(numbers.Select(n => (JsonNode)new JsonObject { ["number"] = n }).ToArray());

    /// <summary>Fills <paramref name="field"/> from the first alias with text, when it is empty.</summary>
    private static bool Move(JsonObject o, string field, string[] aliases)
    {
        if (!IsEmpty(o, field)) return false;
        var value = Take(o, aliases);
        if (string.IsNullOrEmpty(value)) return false;
        o[field] = value;
        return true;
    }

    /// <summary>The first alias with text (matched without case), removed from the object.</summary>
    private static string? Take(JsonObject o, string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var key = o.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            var node = o[key];
            var text = node is JsonValue v ? v.GetValueKind() switch
            {
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.Number => v.ToJsonString(),
                _ => null,
            } : null;
            if (string.IsNullOrWhiteSpace(text)) continue;
            o.Remove(key);
            return text.Trim();
        }
        return null;
    }

    private static bool IsEmpty(JsonObject o, string field) =>
        o[field] is null || (o[field] is JsonValue v && v.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetValue<string>()));
}
