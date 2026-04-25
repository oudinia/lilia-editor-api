using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

// Generates DOCX fixtures that pandoc cannot produce — particularly
// "manual bulleted lists" where the Word user typed a bullet glyph
// (•) at the start of plain paragraphs instead of using Word's list
// style. Output goes to ../../curated/.
//
// Run: dotnet run --project HandcraftedDocxGenerator

var here = AppContext.BaseDirectory;
// bin/Debug/net10.0 → up 3 to project root, up 2 more to docx-corpus
var outDir = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", "..", "curated"));
Directory.CreateDirectory(outDir);

void Save(string name, Action<Body> build)
{
    var path = Path.Combine(outDir, $"{name}.docx");
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document();
    var body = mainPart.Document.AppendChild(new Body());
    build(body);
    Console.WriteLine($"  wrote {name}.docx");
}

Paragraph PlainPara(string text) => new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

Paragraph BulletPara(string text, string bullet = "•")
    => new(new Run(new Text(bullet + " " + text) { Space = SpaceProcessingModeValues.Preserve }));

// ---- Manual bulleted list (the case the user flagged) ----
// User typed "•" at the start of plain paragraphs. Word displays them
// as a list visually but no list style is applied — so a naive parser
// emits paragraphs with leading bullet glyphs, not list-item blocks.
Save("manual-bullet-list-dot", body =>
{
    body.Append(PlainPara("My favorite languages:"));
    body.Append(BulletPara("Python — versatile and readable"));
    body.Append(BulletPara("Go — fast and concurrent"));
    body.Append(BulletPara("Rust — safe and performant"));
    body.Append(PlainPara("End of list."));
});

Save("manual-bullet-list-dash", body =>
{
    body.Append(PlainPara("Action items:"));
    body.Append(BulletPara("Schedule the kickoff meeting", "-"));
    body.Append(BulletPara("Prepare slide deck", "-"));
    body.Append(BulletPara("Send invites", "-"));
});

Save("manual-bullet-list-asterisk", body =>
{
    body.Append(PlainPara("Pros and cons:"));
    body.Append(BulletPara("Pro: easy to deploy", "*"));
    body.Append(BulletPara("Pro: low cost", "*"));
    body.Append(BulletPara("Con: vendor lock-in", "*"));
});

Save("manual-bullet-list-square", body =>
{
    body.Append(PlainPara("Status report:"));
    body.Append(BulletPara("All systems operational", "▪"));
    body.Append(BulletPara("Backups completed", "▪"));
    body.Append(BulletPara("Monitoring green", "▪"));
});

Save("manual-bullet-list-mixed", body =>
{
    body.Append(PlainPara("Meeting notes:"));
    body.Append(BulletPara("Project A is on track"));
    body.Append(BulletPara("Project B blocked by vendor", "-"));
    body.Append(BulletPara("Project C complete", "*"));
    body.Append(BulletPara("Project D pending review", "▪"));
});

Save("manual-numbered-list", body =>
{
    body.Append(PlainPara("Steps to reproduce:"));
    body.Append(PlainPara("1. Open the app"));
    body.Append(PlainPara("2. Click Settings"));
    body.Append(PlainPara("3. Navigate to Profile"));
    body.Append(PlainPara("4. Observe error"));
});

Save("manual-numbered-paren-style", body =>
{
    body.Append(PlainPara("Requirements:"));
    body.Append(PlainPara("(1) Must support OAuth"));
    body.Append(PlainPara("(2) Must scale to 1M users"));
    body.Append(PlainPara("(3) Must have audit logging"));
});

Save("manual-numbered-letter-style", body =>
{
    body.Append(PlainPara("Options:"));
    body.Append(PlainPara("a) Continue with current vendor"));
    body.Append(PlainPara("b) Migrate to new vendor"));
    body.Append(PlainPara("c) Build in-house"));
});

// ---- Mixed: real Word list + manual bullets in same doc ----
Save("mixed-real-and-manual-lists", body =>
{
    body.Append(PlainPara("Manual bullets first:"));
    body.Append(BulletPara("Item one"));
    body.Append(BulletPara("Item two"));
    body.Append(PlainPara("Then a paragraph break."));
    body.Append(PlainPara("And manual numbered:"));
    body.Append(PlainPara("1. First"));
    body.Append(PlainPara("2. Second"));
});

// ---- Tabbed indented paragraphs (also misread as lists sometimes) ----
Save("tab-indented-pseudo-list", body =>
{
    body.Append(PlainPara("Categories:"));
    body.Append(PlainPara("\tFruits — apple, banana, cherry"));
    body.Append(PlainPara("\tVegetables — carrot, broccoli, spinach"));
    body.Append(PlainPara("\tGrains — rice, wheat, oats"));
});

// ---- Deep nesting: bullets at multiple visible indent levels ----
Save("manual-bullets-indented-tree", body =>
{
    body.Append(PlainPara("Org chart:"));
    body.Append(PlainPara("• CEO"));
    body.Append(PlainPara("    • CTO"));
    body.Append(PlainPara("        • VP Eng"));
    body.Append(PlainPara("        • VP Data"));
    body.Append(PlainPara("    • CFO"));
    body.Append(PlainPara("    • CMO"));
});

// ---- Headings via plain paragraphs with size/bold (not Heading style) ----
Save("plain-bold-as-heading", body =>
{
    var bold = new Paragraph(new Run(new RunProperties(new Bold()), new Text("PROJECT OVERVIEW") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(bold);
    body.Append(PlainPara("This document covers the new initiative to consolidate our analytics platforms."));
    var bold2 = new Paragraph(new Run(new RunProperties(new Bold()), new Text("OBJECTIVES") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(bold2);
    body.Append(PlainPara("Reduce cost by 40% within two quarters."));
});

// ---- Two real bullet lists separated by a paragraph (proper Word numbering) ----
// We use NumPr referencing numId 1; the numbering definition is added below.
Save("real-bullet-lists-with-numbering-part", body =>
{
    body.Append(PlainPara("First list:"));
    foreach (var t in new[] { "alpha", "beta", "gamma" })
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "ListParagraph" }, new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }))
            { },
            new Run(new Text(t))));
    body.Append(PlainPara("Then prose between."));
    body.Append(PlainPara("Second list:"));
    foreach (var t in new[] { "delta", "epsilon", "zeta" })
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "ListParagraph" }, new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }))
            { },
            new Run(new Text(t))));
});

// ---- Empty + whitespace-only paragraphs sprinkled through content ----
Save("whitespace-paragraphs", body =>
{
    body.Append(PlainPara("Para one."));
    body.Append(PlainPara(""));
    body.Append(PlainPara("Para two after empty."));
    body.Append(PlainPara("   "));
    body.Append(PlainPara("Para three after whitespace."));
});

Console.WriteLine();
Console.WriteLine($"Done. Output in {outDir}");
