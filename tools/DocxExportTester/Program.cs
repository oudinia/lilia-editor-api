using DocxExportTester;

// ── CONFIGURATION ─────────────────────────────────────────────────────────────
var apiBase   = args.ElementAtOrDefault(0) ?? "http://localhost:5001";
var devUserId = args.ElementAtOrDefault(1) ?? "kp_6969289c438e4b20b46e13f18c7933f2";
var outputDir = args.ElementAtOrDefault(2) ?? Path.GetTempPath();
var roundTrip = !args.Contains("--no-roundtrip");
var keepDoc   = args.Contains("--keep");

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       LILIA DOCX EXPORT TEST                                 ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  API:        {apiBase}");
Console.WriteLine($"  User:       {devUserId}");
Console.WriteLine($"  Output:     {outputDir}");
Console.WriteLine($"  Round-trip: {(roundTrip ? "yes" : "no")}");
Console.WriteLine();

var client = new LiliaApiClient(apiBase, devUserId);
Guid docId = Guid.Empty;

// ── STEP 1: CREATE TEST DOCUMENT ─────────────────────────────────────────────
Console.Write("  [1/5] Creating comprehensive test document...");
try
{
    docId = await client.CreateDocumentAsync("Lilia DOCX Export — Comprehensive Feature Test");
    Console.WriteLine($" ✓  DocId: {docId}");
}
catch (Exception ex)
{
    Console.WriteLine($" ✗  {ex.Message}"); return 1;
}

// ── STEP 2: POPULATE WITH ALL BLOCK TYPES ────────────────────────────────────
Console.Write("  [2/5] Populating blocks (all types)...");
int order = 0;
try
{
    // Abstract
    await client.AddBlockAsync(docId, "abstract", new
    {
        text = "This document tests the complete DOCX export pipeline of Lilia Editor. " +
               "It exercises headings, paragraphs, equations (OMML), code, tables, lists, " +
               "blockquotes, theorems, and bibliography entries."
    }, order++);

    // H1
    await client.AddBlockAsync(docId, "heading", new { text = "Introduction", level = 1 }, order++);

    // Paragraph with inline equation
    await client.AddBlockAsync(docId, "paragraph", new
    {
        text = "Einstein's mass-energy equivalence is expressed as E = mc² where c is the speed of light."
    }, order++);

    // Display equations — covers all major OMML constructs
    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"E = mc^2",
        displayMode = true
    }, order++);

    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"\frac{1}{\sigma\sqrt{2\pi}} e^{-\frac{(x-\mu)^2}{2\sigma^2}}",
        displayMode = true
    }, order++);

    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"\int_{-\infty}^{\infty} e^{-x^2} \, dx = \sqrt{\pi}",
        displayMode = true
    }, order++);

    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}",
        displayMode = true
    }, order++);

    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"\sum_{k=1}^{n} k = \frac{n(n+1)}{2}",
        displayMode = true
    }, order++);

    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"\begin{pmatrix} \cos\theta & -\sin\theta \\ \sin\theta & \cos\theta \end{pmatrix}",
        displayMode = true
    }, order++);

    await client.AddBlockAsync(docId, "equation", new
    {
        latex = @"\nabla \times \mathbf{B} = \mu_0 \mathbf{J} + \mu_0 \epsilon_0 \frac{\partial \mathbf{E}}{\partial t}",
        displayMode = true
    }, order++);

    // H2
    await client.AddBlockAsync(docId, "heading", new { text = "Methods", level = 2 }, order++);

    // Code block
    await client.AddBlockAsync(docId, "code", new
    {
        code = "def gradient_descent(f, grad, x0, lr=0.01, n=1000):\n    x = x0\n    for _ in range(n):\n        x -= lr * grad(x)\n    return x",
        language = "python"
    }, order++);

    // H2
    await client.AddBlockAsync(docId, "heading", new { text = "Results", level = 2 }, order++);

    // Table
    await client.AddBlockAsync(docId, "table", new
    {
        rows = new[]
        {
            new[] { new { text = "Method", colSpan = 1, rowSpan = 1 }, new { text = "Accuracy", colSpan = 1, rowSpan = 1 }, new { text = "Time (s)", colSpan = 1, rowSpan = 1 } },
            new[] { new { text = "Baseline", colSpan = 1, rowSpan = 1 }, new { text = "72.3%", colSpan = 1, rowSpan = 1 }, new { text = "0.12", colSpan = 1, rowSpan = 1 } },
            new[] { new { text = "Proposed", colSpan = 1, rowSpan = 1 }, new { text = "91.7%", colSpan = 1, rowSpan = 1 }, new { text = "0.34", colSpan = 1, rowSpan = 1 } },
        },
        hasHeader = true
    }, order++);

    // List
    await client.AddBlockAsync(docId, "list", new
    {
        listType = "bullet",
        items = new[]
        {
            new { text = "Higher accuracy on benchmark datasets", level = 0 },
            new { text = "Robust to hyperparameter variation", level = 0 },
            new { text = "Scales linearly with data size", level = 0 },
        }
    }, order++);

    // Theorem
    await client.AddBlockAsync(docId, "theorem", new
    {
        theoremType = "theorem",
        title = "Convergence",
        body = "For any convex function f with Lipschitz-continuous gradient, gradient descent converges at rate O(1/k)."
    }, order++);

    // Definition
    await client.AddBlockAsync(docId, "theorem", new
    {
        theoremType = "definition",
        title = "Lipschitz Continuity",
        body = "A function f is L-Lipschitz if |f(x) - f(y)| ≤ L|x - y| for all x, y."
    }, order++);

    // Blockquote
    await client.AddBlockAsync(docId, "blockquote", new
    {
        text = "The art of doing mathematics consists in finding that special case which contains all the germs of generality. — David Hilbert"
    }, order++);

    // H2
    await client.AddBlockAsync(docId, "heading", new { text = "Discussion", level = 2 }, order++);

    // Paragraph
    await client.AddBlockAsync(docId, "paragraph", new
    {
        text = "The results confirm the theoretical guarantees. Further analysis using the identity " +
               "∑ 1/n² = π²/6 (Basel problem) motivates the regularisation approach described above."
    }, order++);

    // H2
    await client.AddBlockAsync(docId, "heading", new { text = "Conclusion", level = 2 }, order++);

    await client.AddBlockAsync(docId, "paragraph", new
    {
        text = "We presented a comprehensive evaluation of the Lilia export pipeline. " +
               "All block types serialise correctly to DOCX with native OMML math rendering."
    }, order++);

    // Bibliography
    await client.AddBlockAsync(docId, "bibliography", new
    {
        entries = new[]
        {
            new { citeKey = "lecun98", entryType = "article", fields = new Dictionary<string, string>
            {
                ["author"] = "LeCun, Y. and Bottou, L. and Bengio, Y. and Haffner, P.",
                ["title"]  = "Gradient-based learning applied to document recognition",
                ["year"]   = "1998",
                ["journal"] = "Proceedings of the IEEE",
                ["volume"] = "86", ["number"] = "11", ["pages"] = "2278--2324"
            }},
            new { citeKey = "goodfellow16", entryType = "book", fields = new Dictionary<string, string>
            {
                ["author"] = "Goodfellow, I. and Bengio, Y. and Courville, A.",
                ["title"]  = "Deep Learning",
                ["year"]   = "2016",
                ["publisher"] = "MIT Press"
            }}
        }
    }, order++);

    Console.WriteLine($" ✓  {order} blocks added.");
}
catch (Exception ex)
{
    Console.WriteLine($" ✗  {ex.Message}");
    if (!keepDoc) await client.DeleteDocumentAsync(docId);
    return 1;
}

// ── STEP 3: EXPORT TO DOCX ────────────────────────────────────────────────────
Console.Write("  [3/5] Exporting to DOCX...");
byte[] docxBytes;
try
{
    docxBytes = await client.ExportDocxAsync(docId);
    var path = Path.Combine(outputDir, $"lilia-export-{docId}.docx");
    await File.WriteAllBytesAsync(path, docxBytes);
    Console.WriteLine($" ✓  {docxBytes.Length / 1024} KB → {path}");
}
catch (Exception ex)
{
    Console.WriteLine($" ✗  {ex.Message}");
    if (!keepDoc) await client.DeleteDocumentAsync(docId);
    return 1;
}

// ── STEP 4: VALIDATE DOCX ─────────────────────────────────────────────────────
Console.WriteLine("  [4/5] Validating DOCX structure...");
var expected = new ExportTestDocument(
    EquationCount: 7,
    HeadingCount:  5,
    TableCount:    1,
    HasCode:       true,
    HasAbstract:   true
);

var validation = DocxValidator.Validate(docxBytes, expected);
var stats = validation.Stats;

Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine("  LILIA DOCX EXPORT VALIDATION REPORT");
Console.WriteLine("══════════════════════════════════════════════════════════════");
Console.WriteLine($"  File size:          {docxBytes.Length / 1024} KB");
Console.WriteLine($"  Total elements:     {stats.TotalElements}");
Console.WriteLine();
Console.WriteLine("  Equation rendering:");
Console.WriteLine($"    OMML (native):    {stats.OmmlEquations,4}  ← ideal");
Console.WriteLine($"    PNG images:       {stats.ImageEquations,4}  ← fallback (complex LaTeX)");
Console.WriteLine($"    Plain text:       {stats.PlainTextEquations,4}  ← should be 0");
Console.WriteLine();
Console.WriteLine("  Content inventory:");
Console.WriteLine($"    Headings:         {stats.Headings,4}");
Console.WriteLine($"    Tables:           {stats.Tables,4}");
Console.WriteLine($"    Code blocks:      {stats.CodeBlocks,4}");
Console.WriteLine($"    Abstract:         {(stats.HasAbstract ? "yes" : "no")}");
Console.WriteLine($"    Bibliography:     {(stats.HasBibliography ? "yes" : "no")}");
Console.WriteLine();
Console.WriteLine("  Checks:");
foreach (var f in validation.Failures)
    Console.WriteLine($"    ✗ {f}");
var checksPassed = validation.Passed;
var checksTotal  = validation.Passed + validation.Failed;
Console.WriteLine($"\n  Result: {checksPassed}/{checksTotal} checks passed");

// ── STEP 5: ROUND-TRIP TEST (export DOCX → re-import → compare) ───────────────
if (roundTrip)
{
    Console.WriteLine();
    Console.Write("  [5/5] Round-trip: re-importing exported DOCX...");
    try
    {
        var rtDocId = await client.ImportDocxAsync(docxBytes, "Round-Trip Test");
        if (rtDocId.HasValue)
        {
            var rtBlocks = await client.GetBlocksAsync(rtDocId.Value);
            var rtEqCount = rtBlocks.Count(b => b.Type == "equation");
            var rtHCount  = rtBlocks.Count(b => b.Type == "heading");
            Console.WriteLine($" ✓  {rtBlocks.Count} blocks re-imported (eq={rtEqCount}, h={rtHCount})");
            Console.WriteLine($"       Round-trip Doc URL: {apiBase.TrimEnd('/')}/document/{rtDocId.Value}");
            if (!keepDoc) await client.DeleteDocumentAsync(rtDocId.Value);
        }
        else
        {
            Console.WriteLine(" ⚠  No document returned from re-import.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($" ⚠  Round-trip failed (non-fatal): {ex.Message}");
    }
}
else
{
    Console.WriteLine("  [5/5] Round-trip skipped (--no-roundtrip).");
}

// ── CLEANUP & RESULT ──────────────────────────────────────────────────────────
if (!keepDoc) await client.DeleteDocumentAsync(docId);

Console.WriteLine();
Console.WriteLine($"  Document URL: {apiBase.TrimEnd('/')}/document/{docId}");
Console.WriteLine($"  DOCX file:    {outputDir}/lilia-export-{docId}.docx");
Console.WriteLine();

if (validation.IsFullPass)
{
    Console.WriteLine("  ✅  ALL CHECKS PASSED — DOCX export pipeline is healthy.");
    return 0;
}
else
{
    Console.WriteLine($"  ❌  {validation.Failed} CHECK(S) FAILED:");
    foreach (var f in validation.Failures)
        Console.WriteLine($"       • {f}");
    return 1;
}
