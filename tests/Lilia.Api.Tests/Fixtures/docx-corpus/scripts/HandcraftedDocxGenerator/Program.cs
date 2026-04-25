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

// ============================================================
// Advanced fixtures — features pandoc cannot produce.
// Each uses MainDocumentPart directly so it can wire up
// footnote/comment/relationship parts.
// ============================================================

void SaveAdvanced(string name, Action<MainDocumentPart> build)
{
    var path = Path.Combine(outDir, $"{name}.docx");
    using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document(new Body());
    build(mainPart);
    mainPart.Document.Save();
    Console.WriteLine($"  wrote {name}.docx");
}

// ---- OMML equations (Word's native math format) ----
// Inline equation: a + b = c
SaveAdvanced("equation-inline-omml", main =>
{
    var body = main.Document.Body!;
    body.Append(PlainPara("Inline math example:"));

    var p = new Paragraph();
    p.Append(new Run(new Text("Let ") { Space = SpaceProcessingModeValues.Preserve }));
    var oMath = new DocumentFormat.OpenXml.Math.OfficeMath(
        new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text("a + b = c")));
    p.Append(oMath);
    p.Append(new Run(new Text(" be the equation.") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p);
});

// Display equation: standalone OMML
SaveAdvanced("equation-display-omml", main =>
{
    var body = main.Document.Body!;
    body.Append(PlainPara("The Pythagorean theorem:"));

    // Display math is an m:oMathPara wrapped in a Word paragraph —
    // matches how Word actually emits standalone equations.
    var p = new Paragraph();
    p.Append(new DocumentFormat.OpenXml.Math.Paragraph(
        new DocumentFormat.OpenXml.Math.OfficeMath(
            new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text("a^2 + b^2 = c^2")))));
    body.Append(p);

    body.Append(PlainPara("relates the sides of a right triangle."));
});

// Multiple equations, mixed display + inline
SaveAdvanced("equation-mixed-omml", main =>
{
    var body = main.Document.Body!;
    body.Append(PlainPara("Statistics review."));

    var meanPara = new Paragraph();
    meanPara.Append(new DocumentFormat.OpenXml.Math.Paragraph(
        new DocumentFormat.OpenXml.Math.OfficeMath(
            new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text("μ = (1/n) Σ x_i")))));
    body.Append(meanPara);

    var p2 = new Paragraph();
    p2.Append(new Run(new Text("And the variance is given by ") { Space = SpaceProcessingModeValues.Preserve }));
    p2.Append(new DocumentFormat.OpenXml.Math.OfficeMath(
        new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text("σ^2 = E[(X − μ)^2]"))));
    p2.Append(new Run(new Text(".") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p2);
});

// ---- Footnotes ----
// Word stores footnotes in a separate XML stream (FootnotesPart).
SaveAdvanced("footnote-single", main =>
{
    var body = main.Document.Body!;

    var footnotesPart = main.AddNewPart<FootnotesPart>();
    footnotesPart.Footnotes = new Footnotes(
        new Footnote(new Paragraph(new Run(new Text("This is the footnote text."))))
        {
            Id = 1,
        });

    var p = new Paragraph();
    p.Append(new Run(new Text("This statement has a footnote") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new RunProperties(new RunStyle { Val = "FootnoteReference" }), new FootnoteReference { Id = 1 }));
    p.Append(new Run(new Text(" attached.") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p);
});

SaveAdvanced("footnote-multiple", main =>
{
    var body = main.Document.Body!;

    var footnotesPart = main.AddNewPart<FootnotesPart>();
    footnotesPart.Footnotes = new Footnotes(
        new Footnote(new Paragraph(new Run(new Text("First footnote.")))) { Id = 1 },
        new Footnote(new Paragraph(new Run(new Text("Second footnote with more detail.")))) { Id = 2 },
        new Footnote(new Paragraph(new Run(new Text("Third footnote.")))) { Id = 3 });

    var p = new Paragraph();
    p.Append(new Run(new Text("First claim") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new FootnoteReference { Id = 1 }));
    p.Append(new Run(new Text(", second claim") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new FootnoteReference { Id = 2 }));
    p.Append(new Run(new Text(", third claim") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new FootnoteReference { Id = 3 }));
    p.Append(new Run(new Text(".") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p);
});

// ---- Tracked changes (insertions and deletions) ----
SaveAdvanced("tracked-changes-mixed", main =>
{
    var body = main.Document.Body!;

    var p = new Paragraph();
    p.Append(new Run(new Text("The original text begins. ") { Space = SpaceProcessingModeValues.Preserve }));
    // Insertion: w:ins wraps a Run
    p.Append(new InsertedRun(
        new Run(new Text("This text was added later. ") { Space = SpaceProcessingModeValues.Preserve }))
    {
        Id = "1",
        Author = "Reviewer",
        Date = DateTime.UtcNow,
    });
    p.Append(new Run(new Text("Middle of paragraph. ") { Space = SpaceProcessingModeValues.Preserve }));
    // Deletion: w:del with DeletedText
    p.Append(new DeletedRun(
        new Run(new DeletedText("This text was removed. ") { Space = SpaceProcessingModeValues.Preserve }))
    {
        Id = "2",
        Author = "Reviewer",
        Date = DateTime.UtcNow,
    });
    p.Append(new Run(new Text("End of paragraph.") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p);
});

// ---- Field codes (HYPERLINK) ----
SaveAdvanced("field-hyperlink", main =>
{
    var body = main.Document.Body!;
    body.Append(PlainPara("See the documentation for details."));

    var p = new Paragraph();
    p.Append(new Run(new Text("Visit ") { Space = SpaceProcessingModeValues.Preserve }));
    // Field: HYPERLINK "url"
    p.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));
    p.Append(new Run(new FieldCode("HYPERLINK \"https://example.com\"") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
    p.Append(new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }), new Text("Example") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
    p.Append(new Run(new Text(" for more information.") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p);
});

// ---- Field codes (TOC, PAGEREF) ----
SaveAdvanced("field-toc", main =>
{
    var body = main.Document.Body!;
    var heading = new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Table of Contents")));
    body.Append(heading);

    // TOC field
    var tocPara = new Paragraph();
    tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));
    tocPara.Append(new Run(new FieldCode("TOC \\o \"1-3\" \\h \\z") { Space = SpaceProcessingModeValues.Preserve }));
    tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
    tocPara.Append(new Run(new Text("(Table of contents will appear here)") { Space = SpaceProcessingModeValues.Preserve }));
    tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
    body.Append(tocPara);

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Introduction"))));
    body.Append(PlainPara("Body text of introduction."));

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Conclusion"))));
    body.Append(PlainPara("Concluding remarks."));
});

// (Embedded-image fixture skipped — full Drawing XML is non-trivial
// to construct via OpenXml SDK 3.x without typed Drawing classes,
// and the image-staging path is already exercised by the LaTeX
// Overleaf zip import tests.)

// ---- Heading-only doc with deep nesting ----
SaveAdvanced("deep-heading-tree", main =>
{
    var body = main.Document.Body!;
    void Heading(string text, int level)
    {
        var styleId = $"Heading{level}";
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new Text(text))));
    }
    Heading("Part I", 1);
    Heading("Chapter 1: Foundations", 2);
    Heading("1.1 Notation", 3);
    body.Append(PlainPara("Notation prose."));
    Heading("1.2 Conventions", 3);
    body.Append(PlainPara("Conventions prose."));
    Heading("Chapter 2: Theory", 2);
    Heading("2.1 Definitions", 3);
    body.Append(PlainPara("Definitions prose."));
    Heading("Part II", 1);
    Heading("Chapter 3: Applications", 2);
    body.Append(PlainPara("Applications prose."));
});

// ---- Realistic resume template ----
SaveAdvanced("realistic-resume", main =>
{
    var body = main.Document.Body!;

    // Name (large bold heading)
    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Title" }),
        new Run(new RunProperties(new Bold(), new FontSize { Val = "32" }), new Text("Jane A. Doe"))));

    // Contact line
    body.Append(PlainPara("Senior Software Engineer • jane@example.com • +1 555-1234 • linkedin.com/in/janedoe"));

    // Section: Experience
    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Professional Experience"))));

    body.Append(new Paragraph(
        new Run(new RunProperties(new Bold()), new Text("Big Tech Co — Senior Engineer ") { Space = SpaceProcessingModeValues.Preserve }),
        new Run(new RunProperties(new Italic()), new Text("(2023–Present)") { Space = SpaceProcessingModeValues.Preserve })));
    body.Append(BulletPara("Led migration of authentication service to OAuth 2.0 across 12 microservices."));
    body.Append(BulletPara("Designed event-driven architecture handling 5M events/second with sub-100ms latency."));
    body.Append(BulletPara("Mentored 4 junior engineers and led architecture review board."));

    body.Append(new Paragraph(
        new Run(new RunProperties(new Bold()), new Text("Startup Inc — Engineer ") { Space = SpaceProcessingModeValues.Preserve }),
        new Run(new RunProperties(new Italic()), new Text("(2020–2023)") { Space = SpaceProcessingModeValues.Preserve })));
    body.Append(BulletPara("Built billing module from scratch, supporting Stripe and PayPal."));
    body.Append(BulletPara("Reduced API p99 latency by 60% through caching and query optimization."));

    // Section: Education
    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Education"))));
    body.Append(PlainPara("BS Computer Science, State University, 2020"));

    // Section: Skills
    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Skills"))));
    body.Append(PlainPara("Languages: Go, Rust, Python, TypeScript, Java"));
    body.Append(PlainPara("Infra: Kubernetes, Terraform, AWS, GCP"));
    body.Append(PlainPara("Databases: PostgreSQL, MongoDB, Redis"));
});

// ---- Realistic business letter ----
SaveAdvanced("realistic-business-letter", main =>
{
    var body = main.Document.Body!;

    // Sender
    body.Append(PlainPara("Jane Doe"));
    body.Append(PlainPara("123 Main Street"));
    body.Append(PlainPara("Boston, MA 02108"));
    body.Append(PlainPara(""));

    body.Append(PlainPara("April 25, 2026"));
    body.Append(PlainPara(""));

    // Recipient
    body.Append(PlainPara("Hiring Manager"));
    body.Append(PlainPara("Acme Corporation"));
    body.Append(PlainPara("456 Enterprise Blvd"));
    body.Append(PlainPara("New York, NY 10001"));
    body.Append(PlainPara(""));

    body.Append(PlainPara("Dear Hiring Manager,"));
    body.Append(PlainPara(""));

    body.Append(PlainPara("I am writing to express my strong interest in the Senior Engineer position advertised on your careers page. With over eight years of experience designing and building distributed systems, I would contribute to your platform team from day one."));
    body.Append(PlainPara(""));

    body.Append(PlainPara("In my current role, I led the design of a streaming ingestion pipeline that processes 5 million events per second with sub-100 millisecond end-to-end latency. Prior to that, I built a multi-region consensus layer that has been adopted across three product teams."));
    body.Append(PlainPara(""));

    body.Append(PlainPara("I am particularly drawn to Acme's recent work on real-time analytics, which aligns closely with my interests. I would welcome the opportunity to discuss how my background fits your team's goals."));
    body.Append(PlainPara(""));

    body.Append(PlainPara("Thank you for your consideration."));
    body.Append(PlainPara(""));

    body.Append(PlainPara("Sincerely,"));
    body.Append(PlainPara(""));
    body.Append(PlainPara("Jane Doe"));
});

// ---- Realistic project report ----
SaveAdvanced("realistic-project-report", main =>
{
    var body = main.Document.Body!;

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Title" }),
        new Run(new Text("Project Atlas: Q1 Status Report"))));

    body.Append(PlainPara("Author: Engineering Team — Date: April 25, 2026"));

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Executive Summary"))));

    body.Append(PlainPara("Project Atlas is on track for the planned Q3 launch. This quarter we completed the core API surface, deployed to staging, and onboarded our first three beta partners. Two minor risks have been identified and mitigated."));

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Milestones"))));

    body.Append(BulletPara("M1: Architecture finalised — completed Q4 2025"));
    body.Append(BulletPara("M2: Core API implemented — completed January 2026"));
    body.Append(BulletPara("M3: Frontend skeleton deployed — completed February 2026"));
    body.Append(BulletPara("M4: Beta testing — in progress"));
    body.Append(BulletPara("M5: Public launch — planned Q3 2026"));

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Risks"))));

    body.Append(BulletPara("Vendor delivery slip — backup vendor identified, contract under review."));
    body.Append(BulletPara("Performance regression detected in load testing — caching layer added."));

    body.Append(new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
        new Run(new Text("Next Steps"))));

    body.Append(BulletPara("Complete beta user onboarding (target: end of April)"));
    body.Append(BulletPara("Run end-to-end load test (target: mid-May)"));
    body.Append(BulletPara("Finalise launch communication plan (target: end of May)"));
});

// ---- Document with hyperlinks (proper Hyperlink element, not field) ----
SaveAdvanced("hyperlinks-proper", main =>
{
    var body = main.Document.Body!;

    main.AddHyperlinkRelationship(new Uri("https://example.com"), true, "rId1");
    main.AddHyperlinkRelationship(new Uri("https://example.org/docs"), true, "rId2");

    var p = new Paragraph();
    p.Append(new Run(new Text("Visit ") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Hyperlink(new Run(
        new RunProperties(new RunStyle { Val = "Hyperlink" }),
        new Text("our website") { Space = SpaceProcessingModeValues.Preserve }))
    { Id = "rId1" });
    p.Append(new Run(new Text(" or check the ") { Space = SpaceProcessingModeValues.Preserve }));
    p.Append(new Hyperlink(new Run(
        new RunProperties(new RunStyle { Val = "Hyperlink" }),
        new Text("documentation") { Space = SpaceProcessingModeValues.Preserve }))
    { Id = "rId2" });
    p.Append(new Run(new Text(" for details.") { Space = SpaceProcessingModeValues.Preserve }));
    body.Append(p);
});

// ---- Right-to-left paragraph (Arabic / Hebrew) ----
SaveAdvanced("rtl-paragraph", main =>
{
    var body = main.Document.Body!;

    body.Append(PlainPara("English paragraph above."));

    var rtl = new Paragraph(
        new ParagraphProperties(new BiDi()),
        new Run(new Text("هذا نص عربي يقرأ من اليمين إلى اليسار.")));
    body.Append(rtl);

    body.Append(PlainPara("English paragraph below."));
});

// ---- Mixed Latin + CJK content ----
SaveAdvanced("multi-script-mix", main =>
{
    var body = main.Document.Body!;

    body.Append(PlainPara("English text first."));
    body.Append(PlainPara("中文段落:这是一段中文文字,展示中日韩字符。"));
    body.Append(PlainPara("日本語の段落:これは日本語のテキストです。"));
    body.Append(PlainPara("한국어 단락: 이것은 한국어 텍스트입니다."));
    body.Append(PlainPara("Mixed: hello / 你好 / こんにちは / 안녕하세요 in one line."));
});

Console.WriteLine();
Console.WriteLine($"Done. Output in {outDir}");
