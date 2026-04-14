using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

// OMML namespace constant — used for raw XML math generation
// (typed DocumentFormat.OpenXml.Math API has naming inconsistencies across SDK versions;
//  raw XML is the stable approach that matches what Word actually stores)


namespace DocxTestGenerator;

/// <summary>
/// Builds a comprehensive DOCX that exercises every block type the Lilia importer supports:
///   Headings H1-H6, Paragraph with all inline formatting, Abstract, Blockquote,
///   Ordered/Unordered/Nested lists, OMML Math (inline + display), Code block,
///   Table (with header row + merged cells), Figure (embedded PNG), Footnote,
///   Theorem/Lemma/Definition/Proof, Bibliography, TOC placeholder, Hyperlink,
///   Page break.
/// </summary>
public static class DocxBuilder
{
    // ----- COLOUR constants -----
    private const string ColourCode  = "000000";
    private const string CodeShade   = "F2F2F2";
    private const string BorderColour = "CCCCCC";

    public static void Build(string outputPath)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document, true);

        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        AddStyles(mainPart);
        AddNumbering(mainPart);
        AddFootnotesPart(mainPart);

        // ── METADATA ───────────────────────────────────────────────────────────
        var props = doc.AddCoreFilePropertiesPart();
        using (var writer = System.Xml.XmlWriter.Create(props.GetStream(System.IO.FileMode.Create)))
        {
            writer.WriteStartElement("cp", "coreProperties",
                "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
            writer.WriteElementString("dc", "title",
                "http://purl.org/dc/elements/1.1/", "Lilia DOCX Import — Comprehensive Feature Test");
            writer.WriteElementString("dc", "creator",
                "http://purl.org/dc/elements/1.1/", "Lilia QA");
            writer.WriteEndElement();
        }

        // ── 0. TOC placeholder ─────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("Table of Contents", "Heading1"));
        body.AppendChild(TocEntry("1. Introduction", 1));
        body.AppendChild(TocEntry("2. Mathematics", 1));
        body.AppendChild(TocEntry("3. Lists", 1));
        body.AppendChild(TocEntry("4. Tables", 1));
        body.AppendChild(TocEntry("5. Theorems", 1));
        body.AppendChild(TocEntry("6. Code", 1));
        body.AppendChild(TocEntry("7. Bibliography", 1));
        body.AppendChild(PageBreakParagraph());

        // ── 1. HEADINGS ────────────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("1. Introduction", "Heading1"));
        body.AppendChild(HeadingParagraph("1.1 Background", "Heading2"));
        body.AppendChild(HeadingParagraph("1.1.1 Motivation", "Heading3"));
        body.AppendChild(HeadingParagraph("1.1.1.1 Deep Heading 4", "Heading4"));
        body.AppendChild(HeadingParagraph("1.1.1.1.1 Deep Heading 5", "Heading5"));
        body.AppendChild(HeadingParagraph("1.1.1.1.1.1 Deep Heading 6", "Heading6"));

        // ── 2. ABSTRACT ────────────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("Abstract", "Heading1"));
        body.AppendChild(StyledParagraph("Abstract",
            "This document tests the complete import pipeline of Lilia Editor. " +
            "It exercises headings, inline formatting, equations, lists, tables, " +
            "figures, footnotes, bibliography, and theorem environments."));

        // ── 3. INLINE FORMATTING ───────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("Inline Formatting", "Heading2"));
        body.AppendChild(InlineFormattingParagraph());

        // ── 4. BLOCKQUOTE ─────────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("Block Quotes", "Heading2"));
        body.AppendChild(BlockquoteParagraph(
            "\"The art of writing is the art of discovering what you believe.\" — Gustave Flaubert"));
        body.AppendChild(BlockquoteParagraph(
            "Deeply indented passage that should be detected as a blockquote by the importer's " +
            "heuristic: left indent ≥ 720 twips with italic styling."));

        // ── 5. FOOTNOTE ───────────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("Footnotes", "Heading2"));
        body.AppendChild(FootnoteParagraph(mainPart,
            "This sentence has a footnote attached to it.",
            "This is the footnote content. It can contain formatted text."));

        // ── 6. MATHEMATICS ────────────────────────────────────────────────────
        body.AppendChild(PageBreakParagraph());
        body.AppendChild(HeadingParagraph("2. Mathematics", "Heading1"));

        // Inline math paragraph
        body.AppendChild(MixedMathParagraph());

        // Display equation — Gaussian
        body.AppendChild(NormalParagraph("The Gaussian probability density function:"));
        body.AppendChild(DisplayEquation(GaussianOmml()));

        // Display equation — Integral
        body.AppendChild(NormalParagraph("The Euler\u2013Poisson integral evaluates to:"));
        body.AppendChild(DisplayEquation(EulerPoissonOmml()));

        // Display equation — Quadratic formula
        body.AppendChild(NormalParagraph("Roots of the quadratic ax\u00B2 + bx + c = 0:"));
        body.AppendChild(DisplayEquation(QuadraticFormulaOmml()));

        // Display equation — Matrix
        body.AppendChild(NormalParagraph("A 2\u00D72 rotation matrix:"));
        body.AppendChild(DisplayEquation(RotationMatrixOmml()));

        // Display equation — Sum notation
        body.AppendChild(NormalParagraph("Summation notation for a discrete series:"));
        body.AppendChild(DisplayEquation(SummationOmml()));

        // Display equation — Maxwell (partial derivatives)
        body.AppendChild(NormalParagraph("One of Maxwell's equations (Faraday's law):"));
        body.AppendChild(DisplayEquation(MaxwellOmml()));

        // ── 7. LISTS ──────────────────────────────────────────────────────────
        body.AppendChild(PageBreakParagraph());
        body.AppendChild(HeadingParagraph("3. Lists", "Heading1"));

        body.AppendChild(HeadingParagraph("3.1 Bullet List", "Heading2"));
        for (int i = 1; i <= 3; i++)
            body.AppendChild(ListItem($"Bullet item {i}: Lorem ipsum dolor sit amet.", 0, false));
        body.AppendChild(ListItem("Nested bullet level 2 — first child.", 1, false));
        body.AppendChild(ListItem("Nested bullet level 2 — second child.", 1, false));
        body.AppendChild(ListItem("Back to level 1 — continuation.", 0, false));

        body.AppendChild(HeadingParagraph("3.2 Numbered List", "Heading2"));
        for (int i = 1; i <= 4; i++)
            body.AppendChild(ListItem($"Step {i}: Perform the required operation.", 0, true));
        body.AppendChild(ListItem("Sub-step A — detailed instruction.", 1, true));
        body.AppendChild(ListItem("Sub-step B — additional detail.", 1, true));
        body.AppendChild(ListItem("Sub-sub-step i — deepest nesting.", 2, true));
        body.AppendChild(ListItem("Step 5 — return to top level.", 0, true));

        // ── 8. TABLES ─────────────────────────────────────────────────────────
        body.AppendChild(PageBreakParagraph());
        body.AppendChild(HeadingParagraph("4. Tables", "Heading1"));

        body.AppendChild(HeadingParagraph("4.1 Simple Table", "Heading2"));
        body.AppendChild(NormalParagraph(
            "Table 1 below shows a simple 4-column data table with a header row."));
        body.AppendChild(SimpleTable());

        body.AppendChild(HeadingParagraph("4.2 Results Table", "Heading2"));
        body.AppendChild(NormalParagraph(
            "Table 2 shows experimental results comparing three algorithms."));
        body.AppendChild(ResultsTable());

        // ── 9. FIGURE ─────────────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("4.3 Figures", "Heading2"));
        body.AppendChild(NormalParagraph(
            "Figure 1 shows a small embedded test image (10\u00D710 px red square)."));
        body.AppendChild(FigureParagraph(mainPart, "Figure 1: Embedded test image."));
        // Caption paragraph (importer detects proximity to image)
        body.AppendChild(CaptionParagraph("Figure 1: Embedded test image (red square, 1 inch).\u00A0"));

        // ── 10. THEOREMS ──────────────────────────────────────────────────────
        body.AppendChild(PageBreakParagraph());
        body.AppendChild(HeadingParagraph("5. Theorems and Definitions", "Heading1"));

        body.AppendChild(TheoremParagraph("Theorem", 1,
            "Pythagorean Theorem",
            "For any right triangle with legs a and b and hypotenuse c, we have a² + b² = c²."));
        body.AppendChild(TheoremParagraph("Definition", 1,
            "Continuity",
            "A function f: ℝ → ℝ is continuous at a point x₀ if for every ε > 0 " +
            "there exists δ > 0 such that |x − x₀| < δ implies |f(x) − f(x₀)| < ε."));
        body.AppendChild(TheoremParagraph("Lemma", 1,
            null,
            "If f is differentiable on an open interval I, then f is continuous on I."));
        body.AppendChild(TheoremParagraph("Proof", null,
            null,
            "Let x₀ ∈ I. Since f is differentiable at x₀, the limit of [f(x) − f(x₀)]/(x − x₀) " +
            "exists as x → x₀. Therefore f(x) → f(x₀), confirming continuity. □"));
        body.AppendChild(TheoremParagraph("Corollary", 1,
            null,
            "Every polynomial is continuous on ℝ."));
        body.AppendChild(TheoremParagraph("Remark", 1,
            null,
            "The converse of Lemma 1 is false: continuity does not imply differentiability."));

        // ── 11. CODE BLOCKS ───────────────────────────────────────────────────
        body.AppendChild(PageBreakParagraph());
        body.AppendChild(HeadingParagraph("6. Code", "Heading1"));

        body.AppendChild(HeadingParagraph("6.1 Python", "Heading2"));
        body.AppendChild(NormalParagraph("The following snippet shows a training loop in Python:"));
        body.AppendChild(CodeBlock(
            "import torch\n\ndef train(model, loader, optimizer):\n" +
            "    for epoch in range(100):\n" +
            "        for batch in loader:\n" +
            "            loss = model(batch)\n" +
            "            optimizer.zero_grad()\n" +
            "            loss.backward()\n" +
            "            optimizer.step()\n" +
            "        print(f'Epoch {epoch}: loss={loss.item():.4f}')"));

        body.AppendChild(HeadingParagraph("6.2 Go", "Heading2"));
        body.AppendChild(CodeBlock(
            "package main\n\nimport \"fmt\"\n\nfunc fibonacci(n int) int {\n" +
            "    if n <= 1 { return n }\n" +
            "    return fibonacci(n-1) + fibonacci(n-2)\n" +
            "}\n\nfunc main() {\n    fmt.Println(fibonacci(10))\n}"));

        body.AppendChild(HeadingParagraph("6.3 SQL", "Heading2"));
        body.AppendChild(CodeBlock(
            "SELECT u.name, COUNT(d.id) AS doc_count\n" +
            "FROM users u\n" +
            "LEFT JOIN documents d ON d.owner_id = u.id\n" +
            "GROUP BY u.id\n" +
            "HAVING COUNT(d.id) > 5\n" +
            "ORDER BY doc_count DESC;"));

        // ── 12. HYPERLINKS ────────────────────────────────────────────────────
        body.AppendChild(HeadingParagraph("Hyperlinks", "Heading2"));
        body.AppendChild(HyperlinkParagraph(mainPart,
            "Visit the Lilia Editor documentation at ",
            "https://liliaeditor.com", "liliaeditor.com",
            " for full usage details."));

        // ── 13. BIBLIOGRAPHY ──────────────────────────────────────────────────
        body.AppendChild(PageBreakParagraph());
        body.AppendChild(HeadingParagraph("7. Bibliography", "Heading1"));
        body.AppendChild(BibEntry("[1]",
            "Goodfellow, I., Bengio, Y., & Courville, A. (2016). " +
            "Deep Learning. MIT Press. https://www.deeplearningbook.org"));
        body.AppendChild(BibEntry("[2]",
            "Vaswani, A., et al. (2017). Attention Is All You Need. " +
            "Advances in Neural Information Processing Systems, 30."));
        body.AppendChild(BibEntry("[3]",
            "LeCun, Y., Bottou, L., Bengio, Y., & Haffner, P. (1998). " +
            "Gradient-based learning applied to document recognition. " +
            "Proceedings of the IEEE, 86(11), 2278–2324."));
        body.AppendChild(BibEntry("[4]",
            "Knuth, D. E. (1984). The TeXbook. Addison-Wesley Professional."));
        body.AppendChild(BibEntry("[5]",
            "Lamport, L. (1994). LaTeX: A Document Preparation System, 2nd ed. " +
            "Addison-Wesley Professional."));

        // ── SECTION PROPERTIES (page size) ────────────────────────────────────
        var sectPr = new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }
        );
        body.AppendChild(sectPr);

        mainPart.Document.Save();
    }

    // =========================================================================
    //  STYLES
    // =========================================================================
    private static void AddStyles(MainDocumentPart main)
    {
        var sp = main.AddNewPart<StyleDefinitionsPart>();
        sp.Styles = new Styles();

        string[] headingIds = ["Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6"];
        string[] headingNames = ["heading 1", "heading 2", "heading 3", "heading 4", "heading 5", "heading 6"];
        int[] sizes = [28, 24, 22, 20, 18, 16];

        for (int i = 0; i < 6; i++)
        {
            var style = new Style { Type = StyleValues.Paragraph, StyleId = headingIds[i] };
            style.Append(new StyleName { Val = headingNames[i] });
            style.Append(new StyleRunProperties(
                new Bold(),
                new FontSize { Val = sizes[i].ToString() },
                new Color { Val = "1a1a2e" }));
            sp.Styles.Append(style);
        }

        // Normal paragraph style
        var normal = new Style { Type = StyleValues.Paragraph, StyleId = "Normal" };
        normal.Append(new StyleName { Val = "Normal" });
        normal.Append(new StyleRunProperties(new FontSize { Val = "22" }));
        sp.Styles.Append(normal);

        // Quote / blockquote
        var quote = new Style { Type = StyleValues.Paragraph, StyleId = "Quote" };
        quote.Append(new StyleName { Val = "Quote" });
        quote.Append(new StyleParagraphProperties(
            new Indentation { Left = "720" }));
        quote.Append(new StyleRunProperties(new Italic(), new Color { Val = "555555" }));
        sp.Styles.Append(quote);

        // Code style
        var code = new Style { Type = StyleValues.Paragraph, StyleId = "Code" };
        code.Append(new StyleName { Val = "Code" });
        code.Append(new StyleRunProperties(
            new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
            new FontSize { Val = "18" }));
        sp.Styles.Append(code);

        // Abstract
        var abs = new Style { Type = StyleValues.Paragraph, StyleId = "Abstract" };
        abs.Append(new StyleName { Val = "Abstract" });
        abs.Append(new StyleParagraphProperties(
            new Indentation { Left = "720", Right = "720" }));
        abs.Append(new StyleRunProperties(new Italic()));
        sp.Styles.Append(abs);

        // TOC entry
        var toc = new Style { Type = StyleValues.Paragraph, StyleId = "TOC1" };
        toc.Append(new StyleName { Val = "toc 1" });
        sp.Styles.Append(toc);

        sp.Styles.Save();
    }

    // =========================================================================
    //  NUMBERING (for lists)
    // =========================================================================
    private static void AddNumbering(MainDocumentPart main)
    {
        var np = main.AddNewPart<NumberingDefinitionsPart>();
        np.Numbering = new Numbering();

        // Abstract num 1 = Bullet
        var bulletAbs = new AbstractNum { AbstractNumberId = 1 };
        for (int lvl = 0; lvl < 9; lvl++)
        {
            var level = new Level { LevelIndex = lvl };
            level.Append(new StartNumberingValue { Val = 1 });
            level.Append(new NumberingFormat { Val = NumberFormatValues.Bullet });
            level.Append(new LevelText { Val = "•" });
            level.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            level.Append(new ParagraphProperties(
                new Indentation { Left = (360 + lvl * 360).ToString(), Hanging = "360" }));
            bulletAbs.Append(level);
        }
        np.Numbering.Append(bulletAbs);

        // Abstract num 2 = Decimal
        var decimalAbs = new AbstractNum { AbstractNumberId = 2 };
        for (int lvl = 0; lvl < 9; lvl++)
        {
            var level = new Level { LevelIndex = lvl };
            level.Append(new StartNumberingValue { Val = 1 });
            level.Append(new NumberingFormat { Val = NumberFormatValues.Decimal });
            level.Append(new LevelText { Val = $"%{lvl + 1}." });
            level.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            level.Append(new ParagraphProperties(
                new Indentation { Left = (360 + lvl * 360).ToString(), Hanging = "360" }));
            decimalAbs.Append(level);
        }
        np.Numbering.Append(decimalAbs);

        // Concrete num 1 → abstract 1 (bullets)
        np.Numbering.Append(new NumberingInstance(
            new AbstractNumId { Val = 1 }) { NumberID = 1 });

        // Concrete num 2 → abstract 2 (decimal)
        np.Numbering.Append(new NumberingInstance(
            new AbstractNumId { Val = 2 }) { NumberID = 2 });

        np.Numbering.Save();
    }

    // =========================================================================
    //  FOOTNOTES
    // =========================================================================
    private static void AddFootnotesPart(MainDocumentPart main)
    {
        var fp = main.AddNewPart<FootnotesPart>();
        fp.Footnotes = new Footnotes();
        // Separator footnote (id = -1)
        fp.Footnotes.Append(new Footnote { Type = FootnoteEndnoteValues.Separator, Id = -1 });
        // Continuation separator (id = 0)
        fp.Footnotes.Append(new Footnote { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        fp.Footnotes.Save();
    }

    // =========================================================================
    //  PARAGRAPH HELPERS
    // =========================================================================
    private static Paragraph HeadingParagraph(string text, string styleId)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        p.AppendChild(new Run(new Text(text)));
        return p;
    }

    private static Paragraph StyledParagraph(string styleId, string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph NormalParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph TocEntry(string text, int level)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new ParagraphStyleId { Val = $"TOC{level}" }));
        p.AppendChild(new Run(new Text(text)));
        return p;
    }

    private static Paragraph InlineFormattingParagraph()
    {
        var p = new Paragraph();

        void AddRun(string text, Action<RunProperties>? style = null)
        {
            var rp = new RunProperties();
            style?.Invoke(rp);
            p.AppendChild(new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        AddRun("This paragraph tests ");
        AddRun("bold text", rp => rp.Append(new Bold()));
        AddRun(", ");
        AddRun("italic text", rp => rp.Append(new Italic()));
        AddRun(", ");
        AddRun("underlined text", rp => rp.Append(new Underline { Val = UnderlineValues.Single }));
        AddRun(", ");
        AddRun("strikethrough text", rp => rp.Append(new Strike()));
        AddRun(", ");
        AddRun("bold-italic combined", rp => { rp.Append(new Bold()); rp.Append(new Italic()); });
        AddRun(", ");
        AddRun("superscript", rp => rp.Append(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }));
        AddRun(" and ");
        AddRun("subscript", rp => rp.Append(new VerticalTextAlignment { Val = VerticalPositionValues.Subscript }));
        AddRun(", inline ");
        AddRun("code()", rp =>
        {
            rp.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" });
            rp.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeShade });
        });
        AddRun(", and a ");
        AddRun("highlighted segment", rp =>
        {
            rp.Append(new Highlight { Val = HighlightColorValues.Yellow });
        });
        AddRun(".");

        return p;
    }

    private static Paragraph BlockquoteParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new ParagraphStyleId { Val = "Quote" },
            new Indentation { Left = "720" },
            new ParagraphBorders(
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Color = "888888",
                    Size = 12,
                    Space = 12
                })));
        var rp = new RunProperties();
        rp.Append(new Italic());
        rp.Append(new Color { Val = "555555" });
        p.AppendChild(new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph FootnoteParagraph(MainDocumentPart main, string bodyText, string footnoteText)
    {
        // Add footnote entry to FootnotesPart
        var fp = main.FootnotesPart!;
        var nextId = fp.Footnotes.Elements<Footnote>()
            .Where(f => f.Id != null && f.Id > 0)
            .Select(f => f.Id!.Value)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var fn = new Footnote { Id = nextId };
        fn.Append(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
            new Run(new RunProperties(
                new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }
            ), new FootnoteReferenceMark()),
            new Run(new Text($" {footnoteText}") { Space = SpaceProcessingModeValues.Preserve })
        ));
        fp.Footnotes.Append(fn);
        fp.Footnotes.Save();

        // Reference in body paragraph
        var p = new Paragraph();
        p.AppendChild(new Run(new Text(bodyText) { Space = SpaceProcessingModeValues.Preserve }));
        p.AppendChild(new Run(
            new RunProperties(
                new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }),
            new FootnoteReference { Id = nextId }
        ));
        return p;
    }

    private static Paragraph ListItem(string text, int level, bool numbered)
    {
        var p = new Paragraph();
        var numId = numbered ? 2 : 1;
        p.AppendChild(new ParagraphProperties(
            new NumberingProperties(
                new NumberingLevelReference { Val = level },
                new NumberingId { Val = numId }
            ),
            new SpacingBetweenLines { After = "80" }
        ));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph PageBreakParagraph()
    {
        var p = new Paragraph();
        p.AppendChild(new Run(new Break { Type = BreakValues.Page }));
        return p;
    }

    // =========================================================================
    //  EQUATIONS (OMML via raw XML — the stable approach for all SDK versions)
    // =========================================================================
    private const string MNs = "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";
    private const string WNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    // Create a Paragraph whose entire content is raw XML
    private static Paragraph RawXmlParagraph(string innerXml, bool centered = false)
    {
        var p = new Paragraph();
        if (centered)
            p.AppendChild(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
        p.InnerXml = (centered ? p.InnerXml : "") + innerXml;
        return p;
    }

    // Inline math paragraph: "The energy–mass equivalence E=mc² is..."
    private static Paragraph MixedMathParagraph() => RawXmlParagraph(
        $@"<w:r {WNs}><w:t xml:space=""preserve"">The energy&#x2013;mass equivalence </w:t></w:r>
<m:oMath {MNs}>
  <m:r><m:t>E</m:t></m:r>
  <m:r><m:t>=</m:t></m:r>
  <m:r><m:t>m</m:t></m:r>
  <m:sSup>
    <m:sSupPr><m:ctrlPr/></m:sSupPr>
    <m:e><m:r><m:t>c</m:t></m:r></m:e>
    <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
  </m:sSup>
</m:oMath>
<w:r {WNs}><w:t xml:space=""preserve""> is the most famous result in theoretical physics.</w:t></w:r>");

    // Display paragraph containing a centered OMML equation
    private static Paragraph DisplayEquation(string ommlXml)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
        p.InnerXml = p.InnerXml + ommlXml;
        return p;
    }

    // f(x) = 1/(σ√2π) · exp(-(x-μ)²/2σ²)
    private static string GaussianOmml() =>
        $@"<m:oMath {MNs}>
  <m:r><m:t>f(x)=</m:t></m:r>
  <m:f>
    <m:num><m:r><m:t>1</m:t></m:r></m:num>
    <m:den>
      <m:r><m:t>&#x3C3;</m:t></m:r>
      <m:rad>
        <m:radPr><m:degHide m:val=""1""/></m:radPr>
        <m:deg/>
        <m:e><m:r><m:t>2&#x3C0;</m:t></m:r></m:e>
      </m:rad>
    </m:den>
  </m:f>
  <m:r><m:t>&#x22C5;exp</m:t></m:r>
  <m:d>
    <m:dPr><m:begChr m:val=""(""/><m:endChr m:val="")""/></m:dPr>
    <m:e>
      <m:r><m:t>&#x2212;</m:t></m:r>
      <m:f>
        <m:num>
          <m:sSup>
            <m:sSupPr><m:ctrlPr/></m:sSupPr>
            <m:e><m:r><m:t>(x&#x2212;&#x3BC;)</m:t></m:r></m:e>
            <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
          </m:sSup>
        </m:num>
        <m:den>
          <m:sSup>
            <m:sSupPr><m:ctrlPr/></m:sSupPr>
            <m:e><m:r><m:t>2&#x3C3;</m:t></m:r></m:e>
            <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
          </m:sSup>
        </m:den>
      </m:f>
    </m:e>
  </m:d>
</m:oMath>";

    // ∫₋∞^∞ e^(-x²) dx = √π
    private static string EulerPoissonOmml() =>
        $@"<m:oMath {MNs}>
  <m:nary>
    <m:naryPr>
      <m:chr m:val=""&#x222B;""/>
      <m:limLoc m:val=""subSup""/>
    </m:naryPr>
    <m:sub><m:r><m:t>&#x2212;&#x221E;</m:t></m:r></m:sub>
    <m:sup><m:r><m:t>&#x221E;</m:t></m:r></m:sup>
    <m:e>
      <m:sSup>
        <m:sSupPr><m:ctrlPr/></m:sSupPr>
        <m:e><m:r><m:t>e</m:t></m:r></m:e>
        <m:sup>
          <m:r><m:t>&#x2212;</m:t></m:r>
          <m:sSup>
            <m:sSupPr><m:ctrlPr/></m:sSupPr>
            <m:e><m:r><m:t>x</m:t></m:r></m:e>
            <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
          </m:sSup>
        </m:sup>
      </m:sSup>
      <m:r><m:t xml:space=""preserve""> dx</m:t></m:r>
    </m:e>
  </m:nary>
  <m:r><m:t>=</m:t></m:r>
  <m:rad>
    <m:radPr><m:degHide m:val=""1""/></m:radPr>
    <m:deg/>
    <m:e><m:r><m:t>&#x3C0;</m:t></m:r></m:e>
  </m:rad>
</m:oMath>";

    // x = (-b ± √(b²-4ac)) / 2a
    private static string QuadraticFormulaOmml() =>
        $@"<m:oMath {MNs}>
  <m:r><m:t>x=</m:t></m:r>
  <m:f>
    <m:num>
      <m:r><m:t>&#x2212;b&#xB1;</m:t></m:r>
      <m:rad>
        <m:radPr><m:degHide m:val=""1""/></m:radPr>
        <m:deg/>
        <m:e>
          <m:sSup>
            <m:sSupPr><m:ctrlPr/></m:sSupPr>
            <m:e><m:r><m:t>b</m:t></m:r></m:e>
            <m:sup><m:r><m:t>2</m:t></m:r></m:sup>
          </m:sSup>
          <m:r><m:t>&#x2212;4ac</m:t></m:r>
        </m:e>
      </m:rad>
    </m:num>
    <m:den><m:r><m:t>2a</m:t></m:r></m:den>
  </m:f>
</m:oMath>";

    // R(θ) = [cosθ -sinθ; sinθ cosθ]
    private static string RotationMatrixOmml() =>
        $@"<m:oMath {MNs}>
  <m:r><m:t>R(&#x3B8;)=</m:t></m:r>
  <m:d>
    <m:dPr><m:begChr m:val=""[""/><m:endChr m:val=""]""/></m:dPr>
    <m:e>
      <m:m>
        <m:mPr>
          <m:mcs>
            <m:mc><m:mcPr><m:count m:val=""2""/><m:mcJc m:val=""center""/></m:mcPr></m:mc>
          </m:mcs>
        </m:mPr>
        <m:mr>
          <m:e><m:r><m:t>cos&#x3B8;</m:t></m:r></m:e>
          <m:e><m:r><m:t>&#x2212;sin&#x3B8;</m:t></m:r></m:e>
        </m:mr>
        <m:mr>
          <m:e><m:r><m:t>sin&#x3B8;</m:t></m:r></m:e>
          <m:e><m:r><m:t>cos&#x3B8;</m:t></m:r></m:e>
        </m:mr>
      </m:m>
    </m:e>
  </m:d>
</m:oMath>";

    // Σ_{k=1}^{n} k = n(n+1)/2
    private static string SummationOmml() =>
        $@"<m:oMath {MNs}>
  <m:nary>
    <m:naryPr>
      <m:chr m:val=""&#x2211;""/>
      <m:limLoc m:val=""subSup""/>
    </m:naryPr>
    <m:sub><m:r><m:t>k=1</m:t></m:r></m:sub>
    <m:sup><m:r><m:t>n</m:t></m:r></m:sup>
    <m:e><m:r><m:t>k</m:t></m:r></m:e>
  </m:nary>
  <m:r><m:t>=</m:t></m:r>
  <m:f>
    <m:num><m:r><m:t>n(n+1)</m:t></m:r></m:num>
    <m:den><m:r><m:t>2</m:t></m:r></m:den>
  </m:f>
</m:oMath>";

    // ∂B/∂t = -∇×E  (Faraday's law)
    private static string MaxwellOmml() =>
        $@"<m:oMath {MNs}>
  <m:f>
    <m:num><m:r><m:t>&#x2202;B</m:t></m:r></m:num>
    <m:den><m:r><m:t>&#x2202;t</m:t></m:r></m:den>
  </m:f>
  <m:r><m:t>=&#x2212;&#x2207;&#xD7;E</m:t></m:r>
</m:oMath>";

    // =========================================================================
    //  TABLE HELPERS
    // =========================================================================
    private static Table SimpleTable()
    {
        var tbl = new Table();
        tbl.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder    { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new LeftBorder   { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new RightBorder  { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4, Color = BorderColour }
            ),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }
        ));

        // Header row
        tbl.AppendChild(TableRow(bold: true, "Method", "Precision", "Recall", "F1-Score"));
        // Data rows
        tbl.AppendChild(TableRow(bold: false, "Baseline CNN",      "0.742", "0.689", "0.714"));
        tbl.AppendChild(TableRow(bold: false, "LSTM",              "0.801", "0.773", "0.787"));
        tbl.AppendChild(TableRow(bold: false, "Transformer",       "0.891", "0.863", "0.877"));
        tbl.AppendChild(TableRow(bold: false, "Our Method (Ours)", "0.923", "0.911", "0.917"));

        return tbl;
    }

    private static Table ResultsTable()
    {
        var tbl = new Table();
        tbl.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder    { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new LeftBorder   { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new RightBorder  { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = BorderColour },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4, Color = BorderColour }
            ),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }
        ));

        tbl.AppendChild(TableRow(bold: true, "Dataset", "Train Size", "Test Size", "Metric", "Score"));
        tbl.AppendChild(TableRow(bold: false, "CIFAR-10",  "50,000", "10,000", "Accuracy", "94.2%"));
        tbl.AppendChild(TableRow(bold: false, "ImageNet",  "1.2M",   "50,000",  "Top-5",   "97.1%"));
        tbl.AppendChild(TableRow(bold: false, "SQUAD 2.0", "130,319","11,873",  "F1",       "88.5%"));

        return tbl;
    }

    private static TableRow TableRow(bool bold, params string[] cells)
    {
        var row = new TableRow();
        foreach (var text in cells)
        {
            var rp = new RunProperties();
            if (bold) rp.Append(new Bold());
            var cell = new TableCell(
                new TableCellProperties(
                    new TableCellBorders(
                        new TopBorder    { Val = BorderValues.Single, Size = 4 },
                        new BottomBorder { Val = BorderValues.Single, Size = 4 },
                        new LeftBorder   { Val = BorderValues.Single, Size = 4 },
                        new RightBorder  { Val = BorderValues.Single, Size = 4 }
                    )
                ),
                new Paragraph(new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve }))
            );
            row.AppendChild(cell);
        }
        return row;
    }

    // =========================================================================
    //  CODE BLOCK
    // =========================================================================
    private static Paragraph CodeBlock(string code)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new ParagraphStyleId { Val = "Code" },
            new SpacingBetweenLines { Before = "120", After = "120" },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeShade }
        ));

        foreach (var line in code.Split('\n'))
        {
            var run = new Run();
            var rp = new RunProperties(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                new FontSize { Val = "18" },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeShade }
            );
            run.Append(rp);
            run.Append(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            p.Append(run);
            p.Append(new Run(new Break()));
        }

        return p;
    }

    // =========================================================================
    //  THEOREM PARAGRAPHS
    // =========================================================================
    private static Paragraph TheoremParagraph(string type, int? number, string? title, string body)
    {
        var p = new Paragraph();
        var pPr = new ParagraphProperties(
            new Indentation { Left = "0" },
            new SpacingBetweenLines { Before = "200", After = "100" }
        );
        p.AppendChild(pPr);

        // Label: "Theorem 1 (Pythagorean Theorem)."
        var labelRun = new Run();
        labelRun.AppendChild(new RunProperties(new Bold()));
        var label = number.HasValue ? $"{type} {number}" : type;
        if (!string.IsNullOrEmpty(title)) label += $" ({title})";
        label += ". ";
        labelRun.AppendChild(new Text(label) { Space = SpaceProcessingModeValues.Preserve });
        p.AppendChild(labelRun);

        // Body (italic for theorem/lemma, normal for proof)
        var bodyRun = new Run();
        bool italic = type is "Theorem" or "Lemma" or "Corollary" or "Proposition";
        if (italic)
            bodyRun.AppendChild(new RunProperties(new Italic()));
        bodyRun.AppendChild(new Text(body) { Space = SpaceProcessingModeValues.Preserve });
        p.AppendChild(bodyRun);

        return p;
    }

    // =========================================================================
    //  BIBLIOGRAPHY ENTRY
    // =========================================================================
    private static Paragraph BibEntry(string label, string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Indentation { Left = "720", Hanging = "720" },
            new SpacingBetweenLines { After = "80" }
        ));
        var labelRun = new Run();
        labelRun.AppendChild(new RunProperties(new Bold()));
        labelRun.AppendChild(new Text(label + " ") { Space = SpaceProcessingModeValues.Preserve });
        p.AppendChild(labelRun);
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    // =========================================================================
    //  FIGURE
    // =========================================================================
    private static Paragraph FigureParagraph(MainDocumentPart main, string caption)
    {
        // Create a tiny 10×10 red PNG in memory
        var pngBytes = CreateMinimalRedPng();

        var imagePart = main.AddImagePart(ImagePartType.Png);
        using (var stream = new System.IO.MemoryStream(pngBytes))
            imagePart.FeedData(stream);

        var relId = main.GetIdOfPart(imagePart);
        const long emuWidth  = 914400L; // 1 inch
        const long emuHeight = 914400L;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = emuWidth, Cy = emuHeight },
                new DW.EffectExtent(),
                new DW.DocProperties { Id = 1, Name = "Picture 1" },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0, Name = "image.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = emuWidth, Cy = emuHeight }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                    { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            ) { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });

        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center }));
        p.AppendChild(new Run(drawing));
        return p;
    }

    private static Paragraph CaptionParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { Before = "40", After = "160" }));
        var run = new Run();
        run.AppendChild(new RunProperties(new Italic(), new FontSize { Val = "18" }));
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        p.AppendChild(run);
        return p;
    }

    private static Paragraph HyperlinkParagraph(MainDocumentPart main, string before,
        string url, string linkText, string after)
    {
        var rel = main.AddHyperlinkRelationship(new Uri(url), true);
        var p = new Paragraph();
        p.AppendChild(new Run(new Text(before) { Space = SpaceProcessingModeValues.Preserve }));
        var link = new Hyperlink { Id = rel.Id };
        var linkRun = new Run();
        linkRun.AppendChild(new RunProperties(
            new Color { Val = "0563C1" },
            new Underline { Val = UnderlineValues.Single }));
        linkRun.AppendChild(new Text(linkText));
        link.AppendChild(linkRun);
        p.AppendChild(link);
        p.AppendChild(new Run(new Text(after) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    // =========================================================================
    //  MINIMAL PNG (10×10 red square) — no external dependencies
    // =========================================================================
    private static byte[] CreateMinimalRedPng()
    {
        // A valid 10×10 RGBA PNG with all pixels set to red.
        // Pre-computed bytes: signature + IHDR + IDAT + IEND.
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAIAAAACUFjqAAAAFElEQVQI12P8z8BQ" +
            "DwADhQGAWjR9awAAAABJRU5ErkJggg==");
    }
}
