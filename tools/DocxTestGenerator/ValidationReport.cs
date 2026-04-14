using System.Text.Json;
using System.IO.Compression;

namespace DocxTestGenerator;

/// <summary>
/// Validates that imported blocks cover expected block types and
/// extracts the main.tex from the LaTeX ZIP for manual review.
/// </summary>
public static class ValidationReport
{
    // Expected block types and minimum count each should appear at least once
    private static readonly (string Type, int MinCount, string Description)[] Expected =
    [
        ("heading",      6,  "H1–H6 headings"),
        ("paragraph",    5,  "Body paragraphs"),
        ("equation",     5,  "Display equations (Gaussian, Euler-Poisson, quadratic, matrix, sum, Maxwell)"),
        ("code",         3,  "Python/Go/SQL code blocks"),
        ("table",        2,  "Data tables"),
        ("list",         2,  "Bullet + numbered lists"),
        ("theorem",      4,  "Theorem/Lemma/Definition/Proof environments"),
        ("blockquote",   1,  "Indented blockquote paragraph"),
        ("bibliography", 1,  "Bibliography / references section"),
        ("abstract",     1,  "Abstract block"),
        ("figure",       1,  "Embedded image / figure"),
    ];

    public static ValidationResult Run(List<BlockDto> blocks, byte[]? latexZip)
    {
        var counts = blocks
            .GroupBy(b => b.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("  LILIA DOCX IMPORT VALIDATION REPORT");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Total blocks imported: {blocks.Count}");
        Console.WriteLine();
        Console.WriteLine("  Block type distribution:");
        foreach (var kv in counts.OrderByDescending(k => k.Value))
            Console.WriteLine($"    {kv.Key,-20} {kv.Value,4}");

        Console.WriteLine();
        Console.WriteLine("  Coverage checks:");

        var passed = 0;
        var failed = 0;
        var warnings = new List<string>();

        foreach (var (type, min, desc) in Expected)
        {
            var actual = counts.TryGetValue(type, out var c) ? c : 0;
            var ok     = actual >= min;
            var symbol = ok ? "✓" : "✗";
            Console.WriteLine($"    {symbol} {type,-20} expected≥{min}  got={actual}   ({desc})");
            if (ok) passed++; else { failed++; warnings.Add($"MISSING: {type} ({desc})"); }
        }

        Console.WriteLine();
        Console.WriteLine($"  Result: {passed}/{passed + failed} checks passed");

        // Print any unknown block types (importer gaps)
        var knownTypes = Expected.Select(e => e.Type).ToHashSet();
        var unknown = counts.Keys.Where(k => !knownTypes.Contains(k)).ToList();
        if (unknown.Count > 0)
            Console.WriteLine($"  Extra types (not in checklist): {string.Join(", ", unknown)}");

        // Extract main.tex and print a preview
        if (latexZip != null)
        {
            Console.WriteLine();
            Console.WriteLine("  LaTeX export preview (main.tex, first 120 lines):");
            Console.WriteLine("  ─────────────────────────────────────────────────");
            var tex = ExtractMainTex(latexZip);
            if (tex != null)
            {
                var lines = tex.Split('\n').Take(120);
                foreach (var line in lines)
                    Console.WriteLine("  " + line);
                Console.WriteLine("  [... truncated ...]");
            }
            else
            {
                Console.WriteLine("  (could not extract main.tex from ZIP)");
            }
        }

        Console.WriteLine("══════════════════════════════════════════════════════════════");

        return new ValidationResult(passed, failed, warnings, blocks.Count);
    }

    private static string? ExtractMainTex(byte[] zipBytes)
    {
        try
        {
            using var ms  = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("main.tex", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".tex", StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Prints a detailed dump of all blocks for debugging.
    /// </summary>
    public static void DumpBlocks(List<BlockDto> blocks)
    {
        Console.WriteLine("\n  Block dump:");
        foreach (var b in blocks)
        {
            var preview = GetContentPreview(b);
            Console.WriteLine($"    [{b.SortOrder:D3}] {b.Type,-20} depth={b.Depth} | {preview}");
        }
    }

    private static string GetContentPreview(BlockDto b)
    {
        try
        {
            if (b.Content.ValueKind == JsonValueKind.Object)
            {
                if (b.Content.TryGetProperty("text", out var text))
                    return Truncate(text.GetString(), 80);
                if (b.Content.TryGetProperty("latex", out var latex))
                    return $"latex: {Truncate(latex.GetString(), 70)}";
                if (b.Content.TryGetProperty("code", out var code))
                    return $"code: {Truncate(code.GetString(), 70)}";
                if (b.Content.TryGetProperty("level", out var level))
                    return $"H{level.GetInt32()}";
                return b.Content.GetRawText()[..Math.Min(80, b.Content.GetRawText().Length)];
            }
        }
        catch { }
        return "(no preview)";
    }

    private static string Truncate(string? s, int max) =>
        s == null ? "(null)" : s.Length <= max ? s : s[..max] + "…";
}

public record ValidationResult(int Passed, int Failed, List<string> Warnings, int TotalBlocks)
{
    public bool IsFullPass => Failed == 0;
}
