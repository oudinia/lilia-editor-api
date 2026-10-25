using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// The LaTeX diff (publisher templates phase 3, 25 Oct 2026): what changed in a document's submission
/// LaTeX since a kept submission. Lines from GNU diff's unified output, so the comparison is the one
/// any author or editor would run themselves.
/// </summary>
public static partial class LatexDiff
{
    public sealed record Line(char Kind, string Text);
    public sealed record Hunk(int OldStart, int NewStart, IReadOnlyList<Line> Lines);
    public sealed record Result(int Added, int Removed, IReadOnlyList<Hunk> Hunks, bool Truncated);

    /// <summary>At most this many changed lines are returned; the counts are always whole.</summary>
    public const int MaxLines = 4000;

    public static async Task<Result> CompareAsync(string before, string after, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lilia-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "submitted.tex"), before, ct);
            await File.WriteAllTextAsync(Path.Combine(dir, "now.tex"), after, ct);
            var psi = new ProcessStartInfo("diff", "-u submitted.tex now.tex") { WorkingDirectory = dir, RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("diff did not start");
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return Parse(output);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Unified diff output into hunks (public for tests).</summary>
    public static Result Parse(string unified)
    {
        var hunks = new List<Hunk>();
        List<Line>? lines = null;
        int added = 0, removed = 0, kept = 0;
        var truncated = false;
        foreach (var raw in unified.Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("--- ") || raw.StartsWith("+++ ")) continue;
            if (HunkHeader().Match(raw) is { Success: true } h)
            {
                lines = [];
                hunks.Add(new Hunk(int.Parse(h.Groups[1].Value), int.Parse(h.Groups[2].Value), lines));
                continue;
            }
            if (lines is null || raw.Length == 0 || raw[0] is not (' ' or '+' or '-')) continue;
            if (raw[0] == '+') added++;
            if (raw[0] == '-') removed++;
            if (kept >= MaxLines) { truncated = true; continue; }
            lines.Add(new Line(raw[0], raw[1..]));
            kept++;
        }
        return new Result(added, removed, hunks, truncated);
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    /// <summary>
    /// The references for the marked-up PDF: the current ones, plus any the submission had that are
    /// gone now, so a citation that was removed still resolves where latexdiff shows it struck out.
    /// </summary>
    public static string MergeBib(string? submitted, string? now)
    {
        var current = now ?? "";
        var keys = BibKey().Matches(current).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var extra = BibEntry().Matches(submitted ?? "")
            .Where(m => !keys.Contains(m.Groups["key"].Value))
            .Select(m => m.Value);
        return string.Join("\n\n", new[] { current }.Concat(extra).Where(s => s.Trim().Length > 0));
    }

    [GeneratedRegex(@"@\w+\s*\{\s*([^,\s]+)\s*,")]
    private static partial Regex BibKey();

    // One entry: from its @type{key, to the line with the closing brace that starts the next or ends the file.
    [GeneratedRegex(@"@\w+\s*\{\s*(?<key>[^,\s]+)\s*,.*?(?=\n@|\z)", RegexOptions.Singleline)]
    private static partial Regex BibEntry();
}
