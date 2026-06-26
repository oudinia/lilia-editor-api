using System.Collections.Concurrent;
using System.Reflection;

namespace Lilia.Api.Services;

/// <summary>
/// Loads each skill's guidance from its embedded `AiSkills/&lt;id&gt;.md` (a copy of
/// lilia-docs/ai-skill/&lt;id&gt;/SKILL.md, compiled in as an EmbeddedResource — the API
/// repo can't read the docs repo at runtime). Strips the YAML frontmatter and caches.
/// Returns "" if a skill's resource is missing, so the Ask path degrades gracefully.
/// </summary>
public static class AiSkillGuidance
{
    private static readonly Assembly Asm = typeof(AiSkillGuidance).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Get(string skillId) => Cache.GetOrAdd(skillId, Load);

    private static string Load(string id)
    {
        // Resource is pinned to "AiSkills.<id>.md" via LogicalName; match resiliently
        // (tolerate any prefix and a build that swapped '-' for '_').
        var name = Asm.GetManifestResourceNames().FirstOrDefault(n =>
            n.Replace('_', '-').EndsWith($".{id}.md", StringComparison.OrdinalIgnoreCase));
        if (name is null) return string.Empty;
        using var stream = Asm.GetManifestResourceStream(name);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream);
        return StripFrontmatter(reader.ReadToEnd()).Trim();
    }

    private static string StripFrontmatter(string md)
    {
        if (!md.StartsWith("---", StringComparison.Ordinal)) return md;
        var close = md.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (close < 0) return md;
        var nl = md.IndexOf('\n', close + 1);
        return nl < 0 ? string.Empty : md[(nl + 1)..];
    }
}
