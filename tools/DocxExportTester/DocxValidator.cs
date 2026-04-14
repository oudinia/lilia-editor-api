using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocxExportTester;

/// <summary>
/// Validates a DOCX file produced by the Lilia export pipeline.
/// Checks that all expected block types rendered correctly and that
/// equations are native OMML (not plain-text fallbacks).
/// </summary>
public static class DocxValidator
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public record DocxValidationResult(
        int Passed, int Failed, List<string> Failures,
        DocxStats Stats)
    {
        public bool IsFullPass => Failed == 0;
    }

    public record DocxStats(
        int OmmlEquations,
        int ImageEquations,
        int PlainTextEquations,
        int Headings,
        int Paragraphs,
        int Tables,
        int CodeBlocks,
        bool HasAbstract,
        bool HasBibliography,
        int TotalElements
    );

    public static DocxValidationResult Validate(byte[] docxBytes, ExportTestDocument expected)
    {
        var failures = new List<string>();
        var passed = 0;

        string xml;
        using (var ms = new MemoryStream(docxBytes))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            var entry = zip.GetEntry("word/document.xml")
                ?? throw new Exception("word/document.xml not found in DOCX");
            using var reader = new StreamReader(entry.Open());
            xml = reader.ReadToEnd();
        }

        var doc = XDocument.Parse(xml);

        // ── Count OMML equations ──────────────────────────────────────────────
        var ommlCount  = doc.Descendants(M + "oMathPara").Count();
        var inlineOmml = doc.Descendants(M + "oMath")
            .Count(e => e.Parent?.Name != M + "oMathPara");
        var totalOmml  = ommlCount + inlineOmml;

        // ── Detect plain-text equation fallbacks ──────────────────────────────
        var allText = string.Join(" ", doc.Descendants(W + "t").Select(t => t.Value));
        var plainEqMatches = Regex.Matches(allText, @"\$[^$]+\$|\[[^\]]{5,}\]");
        var plainTextEqCount = plainEqMatches.Count;

        // ── Count structural elements ─────────────────────────────────────────
        var headings = doc.Descendants(W + "pStyle")
            .Count(e => e.Attribute(W + "val")?.Value.StartsWith("Heading") == true);

        var tables = doc.Descendants(W + "tbl").Count();

        // Detect code blocks by Consolas font
        var codeRuns = doc.Descendants(W + "rFonts")
            .Count(e => e.Attribute(W + "ascii")?.Value == "Consolas");
        var hasCodeBlocks = codeRuns > 0;

        // Images (equation PNGs or figures)
        var images = doc.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "blip").Count();

        // Abstract detection: style name contains "abstract", or text contains "Abstract" heading
        var allParas = doc.Descendants(W + "p").ToList();
        var hasAbstract = allParas.Any(p =>
            p.Descendants(W + "pStyle").Any(s =>
                s.Attribute(W + "val")?.Value.Contains("abstract", StringComparison.OrdinalIgnoreCase) == true)) ||
            allText.Contains("Abstract", StringComparison.OrdinalIgnoreCase);

        // Bibliography: look for typical ref marker text
        var hasBib = allText.Contains("[1]") || allText.Contains("References") || allText.Contains("Bibliography");

        var stats = new DocxStats(
            OmmlEquations: totalOmml,
            ImageEquations: images,
            PlainTextEquations: plainTextEqCount,
            Headings: headings,
            Paragraphs: allParas.Count,
            Tables: tables,
            CodeBlocks: hasCodeBlocks ? codeRuns : 0,
            HasAbstract: hasAbstract,
            HasBibliography: hasBib,
            TotalElements: allParas.Count + tables
        );

        // ── Checks ────────────────────────────────────────────────────────────

        Check(ref passed, failures,
            totalOmml >= expected.EquationCount,
            $"OMML equations: expected≥{expected.EquationCount}, got {totalOmml}");

        Check(ref passed, failures,
            plainTextEqCount == 0,
            $"Plain-text equation fallbacks found ({plainTextEqCount}) — OMML conversion incomplete");

        Check(ref passed, failures,
            headings >= expected.HeadingCount,
            $"Headings: expected≥{expected.HeadingCount}, got {headings}");

        Check(ref passed, failures,
            tables >= expected.TableCount,
            $"Tables: expected≥{expected.TableCount}, got {tables}");

        if (expected.HasCode)
            Check(ref passed, failures, hasCodeBlocks, "Code blocks: none found (expected Consolas-formatted runs)");

        if (expected.HasAbstract)
            Check(ref passed, failures, hasAbstract, "Abstract: not detected");

        // File size sanity: should be > 3 KB for any real document
        Check(ref passed, failures,
            docxBytes.Length > 3000,
            $"DOCX suspiciously small ({docxBytes.Length} bytes)");

        return new DocxValidationResult(passed, failures.Count, failures, stats);
    }

    private static void Check(ref int passed, List<string> failures, bool condition, string failMsg)
    {
        if (condition) passed++;
        else failures.Add(failMsg);
    }
}

/// <summary>What we expect to find in the exported DOCX.</summary>
public record ExportTestDocument(
    int EquationCount,
    int HeadingCount,
    int TableCount,
    bool HasCode,
    bool HasAbstract
);
