using DocxTestGenerator;

// ── CONFIGURATION ────────────────────────────────────────────────────────────
var apiBase    = args.ElementAtOrDefault(0) ?? "http://localhost:5001";
var devUserId  = args.ElementAtOrDefault(1) ?? "kp_6969289c438e4b20b46e13f18c7933f2"; // oussama.dinia@gmail.com
var outputDir  = args.ElementAtOrDefault(2) ?? Path.GetTempPath();
var verbose    = args.Contains("--verbose");
var exportLatex = !args.Contains("--no-latex");

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       LILIA DOCX IMPORT TEST GENERATOR                      ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  API:     {apiBase}");
Console.WriteLine($"  User:    {devUserId}");
Console.WriteLine($"  Output:  {outputDir}");
Console.WriteLine();

// ── STEP 1: BUILD DOCX ───────────────────────────────────────────────────────
var docxPath = Path.Combine(outputDir, $"lilia-comprehensive-test-{DateTime.Now:yyyyMMdd-HHmmss}.docx");
Console.Write("  [1/4] Building comprehensive DOCX...");

try
{
    DocxBuilder.Build(docxPath);
    var sizeKb = new FileInfo(docxPath).Length / 1024;
    Console.WriteLine($" ✓  ({sizeKb} KB → {docxPath})");
}
catch (Exception ex)
{
    Console.WriteLine($" ✗  FAILED: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    return 1;
}

// ── STEP 2: IMPORT VIA API ───────────────────────────────────────────────────
Console.Write("  [2/4] Importing DOCX via Lilia API (skipReview=true)...");

var client = new LiliaApiClient(apiBase, devUserId);
ImportResult importResult;

try
{
    var docxBytes = await File.ReadAllBytesAsync(docxPath);
    importResult  = await client.ImportDocxAsync(
        docxBytes,
        filename: Path.GetFileName(docxPath),
        title: "Lilia DOCX Import — Comprehensive Feature Test");

    if (importResult.Document != null)
    {
        Console.WriteLine($" ✓  DocumentId: {importResult.Document.Id}");
    }
    else if (importResult.ReviewSessionId.HasValue)
    {
        Console.WriteLine($" ⚠  Review session returned (skipReview may need server restart).");
        Console.Write($"     Auto-finalizing session {importResult.ReviewSessionId}...");
        var finalizeResult = await client.FinalizeSessionAsync(
            importResult.ReviewSessionId.Value,
            "Lilia DOCX Import — Comprehensive Feature Test");
        if (finalizeResult.Document != null)
        {
            Console.WriteLine($" ✓  DocumentId: {finalizeResult.Document.Id}");
            importResult = importResult with
            {
                Document = new DocInfo(finalizeResult.Document.Id, finalizeResult.Document.Title),
                ReviewSessionId = null
            };
        }
        else
        {
            Console.WriteLine(" ✗  Finalize returned no document.");
            return 1;
        }
    }
    else
    {
        Console.WriteLine(" ✗  No document and no review session in response.");
        return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($" ✗  FAILED: {ex.Message}");
    return 1;
}

var documentId = importResult.Document!.Id;

// ── STEP 3: FETCH BLOCKS ─────────────────────────────────────────────────────
Console.Write("  [3/4] Fetching imported blocks...");

List<BlockDto> blocks;
try
{
    blocks = await client.GetBlocksAsync(documentId);
    Console.WriteLine($" ✓  {blocks.Count} blocks retrieved.");
}
catch (Exception ex)
{
    Console.WriteLine($" ✗  FAILED: {ex.Message}");
    return 1;
}

if (verbose)
    ValidationReport.DumpBlocks(blocks);

// ── STEP 4: EXPORT TO LATEX & VALIDATE ───────────────────────────────────────
byte[]? latexZip = null;

if (exportLatex)
{
    Console.Write("  [4/4] Exporting document to LaTeX...");
    try
    {
        latexZip = await client.ExportLatexZipAsync(documentId);
        var zipPath = Path.Combine(outputDir, $"lilia-export-{documentId}.zip");
        await File.WriteAllBytesAsync(zipPath, latexZip);
        Console.WriteLine($" ✓  {latexZip.Length / 1024} KB → {zipPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($" ⚠  Export failed (non-fatal): {ex.Message}");
    }
}

// ── VALIDATION REPORT ─────────────────────────────────────────────────────────
var report = ValidationReport.Run(blocks, latexZip);

Console.WriteLine();
Console.WriteLine($"  Document URL: {apiBase.TrimEnd('/')}/document/{documentId}");
Console.WriteLine($"  DOCX file:    {docxPath}");
if (latexZip != null)
    Console.WriteLine($"  LaTeX ZIP:    {outputDir}/lilia-export-{documentId}.zip");
Console.WriteLine();

if (report.IsFullPass)
{
    Console.WriteLine("  ✅  ALL CHECKS PASSED — DOCX import pipeline is healthy.");
    return 0;
}
else
{
    Console.WriteLine($"  ❌  {report.Failed} CHECK(S) FAILED:");
    foreach (var w in report.Warnings)
        Console.WriteLine($"       • {w}");
    return 1;
}
