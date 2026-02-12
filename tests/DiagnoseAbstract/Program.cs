using Lilia.Import.Models;
using Lilia.Import.Services;

var docxPath = args.Length > 0 ? args[0] : "/home/oussama/projects/documents/Statistical study  (1).docx";

Console.WriteLine($"=== Parsing with DocxParser: {Path.GetFileName(docxPath)} ===\n");

var parser = new DocxParser();
var result = await parser.ParseAsync(docxPath);

Console.WriteLine($"Title: {result.Title}");
Console.WriteLine($"Total elements: {result.Elements.Count}");
Console.WriteLine($"Warnings: {result.Warnings.Count}");
Console.WriteLine($"Paragraph traces: {result.ParagraphTraces.Count}\n");

// Show paragraph traces — this is the key diagnostic
Console.WriteLine("=== PARAGRAPH TRACES (every body element) ===\n");
Console.WriteLine($"{"Idx",4} {"Type",-12} {"Rule",-25} {"Detected",-18} {"#El",3} {"Style",-20} {"Font",-18} {"Shade",-8} {"Section",-15} {"InAbs",-5} {"Text (first 80 chars)"}");
Console.WriteLine(new string('-', 220));

foreach (var t in result.ParagraphTraces)
{
    var textPreview = t.RawText.Replace("\n", " ").Replace("\r", "");
    if (textPreview.Length > 80) textPreview = textPreview[..80] + "...";

    Console.WriteLine($"{t.BodyIndex,4} {t.ElementType,-12} {t.MatchedRuleId,-25} {t.DetectedType,-18} {t.ElementsProduced,3} {t.StyleId ?? "-",-20} {t.FontFamily ?? "-",-18} {t.ShadingFill ?? "-",-8} {t.CurrentSection ?? "-",-15} {(t.InAbstractSection ? "YES" : "-"),-5} {textPreview}");

    if (t.Notes != null)
        Console.WriteLine($"     NOTE: {t.Notes}");
}

// Show element summary
Console.WriteLine("\n=== ELEMENT SUMMARY ===\n");
var typeCounts = result.Elements
    .GroupBy(e => e.Type)
    .OrderByDescending(g => g.Count());
foreach (var g in typeCounts)
{
    Console.WriteLine($"  {g.Key,-20} {g.Count(),4}");
}

// Show first 60 elements
Console.WriteLine("\n--- Elements (first 60) ---\n");
for (int i = 0; i < Math.Min(60, result.Elements.Count); i++)
{
    var el = result.Elements[i];
    var type = el.Type.ToString();
    var text = el switch
    {
        ImportAbstract abs => abs.Text,
        ImportCodeBlock code => $"[{code.DetectionReason}] {code.Text}",
        ImportHeading h => $"H{h.Level}: {h.Text}",
        ImportParagraph p => p.Text,
        ImportImage img => "[image]",
        ImportBlockquote bq => $"[quote] {bq.Text}",
        ImportTheorem th => $"[{th.EnvironmentType}] {th.Text}",
        ImportBibliographyEntry bib => $"[bib] {bib.Text}",
        ImportTable => "[table]",
        ImportListItem li => $"  - {li.Text}",
        ImportEquation eq => $"[equation: {eq.LatexContent?[..Math.Min(60, eq.LatexContent?.Length ?? 0)]}]",
        ImportPageBreak => "[page break]",
        ImportTableOfContents => "[TOC]",
        _ => "(other)"
    };
    if (text != null && text.Length > 120) text = text[..120] + "...";

    var marker = el.Type switch
    {
        ImportElementType.Abstract => " <<< ABSTRACT",
        ImportElementType.CodeBlock => " <<< CODE",
        ImportElementType.Blockquote => " <<< BLOCKQUOTE",
        ImportElementType.Theorem => " <<< THEOREM",
        ImportElementType.BibliographyEntry => " <<< BIB",
        _ => ""
    };

    Console.WriteLine($"[{i,3}] {type,-15} {text}{marker}");
}

if (result.Warnings.Count > 0)
{
    Console.WriteLine("\n--- Warnings ---");
    foreach (var w in result.Warnings)
    {
        Console.WriteLine($"  [{w.Type}] {w.Message}");
    }
}
