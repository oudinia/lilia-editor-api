using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// Round-trip tests for the documentclass-first epic (LILIA-120).
/// Each test sets one or more layout settings on a Document, runs the
/// consolidated <see cref="LaTeXPreambleBuilder"/>, and asserts the
/// expected LaTeX is in the emitted preamble. The audit table in
/// <c>lilia-docs/teams/2026-05-06-documentclass-first/team-1-server-preamble/README.md</c>
/// is the source of truth for what each row must emit.
/// </summary>
public class PreambleEmissionTests
{
    /// <summary>
    /// Construct a Document with sensible defaults; tests override the
    /// fields they care about via object-initialiser. Mirrors the
    /// schema defaults from <see cref="Document"/>.
    /// </summary>
    private static Document NewDoc(Action<Document>? configure = null)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Preamble Emission Test",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 12,
            Columns = 1,
            ColumnSeparator = "none",
            ColumnGap = 1.5,
        };
        configure?.Invoke(doc);
        return doc;
    }

    /// <summary>
    /// Concatenates the class directive + layout block. The full
    /// preamble that callers stitch together has additional package
    /// lines in between (loaded by <see cref="LaTeXPreamble.Packages"/>);
    /// for assertion purposes, just the directive + layout is enough.
    /// </summary>
    private static string BuildPreamble(Document doc)
    {
        var result = LaTeXPreambleBuilder.Build(doc);
        return result.ClassDirective + "\n" + result.LayoutPreamble;
    }

    // ── ColumnGap ──────────────────────────────────────────────────────

    [Fact]
    public void ColumnGap_emits_setlength_columnsep_with_cm_value()
    {
        var doc = NewDoc(d =>
        {
            d.Columns = 2;
            d.ColumnGap = 1.25;
        });
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\setlength{\\columnsep}{1.25cm}");
    }

    // ── ColumnSeparator ────────────────────────────────────────────────

    [Fact]
    public void ColumnSeparator_line_emits_columnseprule_0_4pt()
    {
        var doc = NewDoc(d =>
        {
            d.Columns = 2;
            d.ColumnSeparator = "line";
        });
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\setlength{\\columnseprule}{0.4pt}");
    }

    [Fact]
    public void ColumnSeparator_none_emits_columnseprule_0pt()
    {
        var doc = NewDoc(d =>
        {
            d.Columns = 2;
            d.ColumnSeparator = "none";
        });
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\setlength{\\columnseprule}{0pt}");
    }

    // ── BalancedColumns ────────────────────────────────────────────────

    [Fact]
    public void BalancedColumns_drops_twocolumn_class_option_and_loads_multicol()
    {
        var doc = NewDoc(d =>
        {
            d.Columns = 2;
            d.BalancedColumns = true;
        });
        var result = LaTeXPreambleBuilder.Build(doc);
        result.ClassDirective.Should().NotContain("twocolumn");
        result.LayoutPreamble.Should().Contain("\\usepackage{multicol}");
        result.BodyOpener.Should().Be("\\begin{multicols}{2}");
        result.BodyCloser.Should().Be("\\end{multicols}");
    }

    // ── Margins ────────────────────────────────────────────────────────

    [Fact]
    public void Margins_emit_geometry_package_with_top_bottom_left_right()
    {
        var doc = NewDoc(d =>
        {
            d.MarginTop = "3cm";
            d.MarginBottom = "2cm";
            d.MarginLeft = "2.5cm";
            d.MarginRight = "2.5cm";
        });
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\usepackage[top=3cm,bottom=2cm,left=2.5cm,right=2.5cm]{geometry}");
    }

    // ── LineSpacing ────────────────────────────────────────────────────

    [Fact]
    public void LineSpacing_1_5_emits_onehalfspacing()
    {
        var doc = NewDoc(d => d.LineSpacing = 1.5);
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\onehalfspacing");
    }

    [Fact]
    public void LineSpacing_2_0_emits_doublespacing()
    {
        var doc = NewDoc(d => d.LineSpacing = 2.0);
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\doublespacing");
    }

    [Fact]
    public void LineSpacing_1_15_emits_linespread()
    {
        var doc = NewDoc(d => d.LineSpacing = 1.15);
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\linespread{1.15}");
    }

    // ── ParagraphIndent ────────────────────────────────────────────────

    [Fact]
    public void ParagraphIndent_value_emits_setlength_parindent()
    {
        var doc = NewDoc(d => d.ParagraphIndent = "1.5em");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\setlength{\\parindent}{1.5em}");
    }

    [Fact]
    public void ParagraphIndent_none_emits_parindent_0pt()
    {
        var doc = NewDoc(d => d.ParagraphIndent = "none");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\setlength{\\parindent}{0pt}");
    }

    // ── PageNumbering ──────────────────────────────────────────────────

    [Fact]
    public void PageNumbering_arabic_emits_pagenumbering_arabic()
    {
        var doc = NewDoc(d => d.PageNumbering = "arabic");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\pagenumbering{arabic}");
    }

    [Fact]
    public void PageNumbering_roman_emits_pagenumbering_roman()
    {
        var doc = NewDoc(d => d.PageNumbering = "roman");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\pagenumbering{roman}");
    }

    [Fact]
    public void PageNumbering_none_emits_pagestyle_empty()
    {
        var doc = NewDoc(d => d.PageNumbering = "none");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\pagestyle{empty}");
    }

    // ── HeaderText / FooterText ────────────────────────────────────────

    [Fact]
    public void HeaderText_loads_fancyhdr_and_emits_lhead()
    {
        var doc = NewDoc(d => d.HeaderText = "My Header");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\usepackage{fancyhdr}");
        preamble.Should().Contain("\\pagestyle{fancy}");
        preamble.Should().Contain("\\lhead{My Header}");
    }

    [Fact]
    public void FooterText_emits_rfoot()
    {
        var doc = NewDoc(d => d.FooterText = "Confidential");
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain("\\usepackage{fancyhdr}");
        preamble.Should().Contain("\\rfoot{Confidential}");
    }

    [Fact]
    public void HeaderText_with_latex_metachars_is_escaped_for_safety()
    {
        // User-supplied strings flow into LaTeX source; backslash + braces
        // would let users break compilation (or worse, inject macros).
        var doc = NewDoc(d => d.HeaderText = "Hack \\foo{bar} 50% & more_text");
        var preamble = BuildPreamble(doc);
        preamble.Should().NotContain("\\foo");           // backslash must be neutralised
        preamble.Should().Contain("\\textbackslash{}"); // backslash escape applied
        preamble.Should().Contain("\\%");                // percent escape
        preamble.Should().Contain("\\&");                // ampersand escape
        preamble.Should().Contain("\\_");                // underscore escape
    }

    // ── FontFamily ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Charter", "charter")]
    [InlineData("Times", "mathptmx")]
    [InlineData("Palatino", "palatino")]
    [InlineData("Bookman", "bookman")]
    public void FontFamily_emits_matching_native_package(string family, string pkg)
    {
        var doc = NewDoc(d => d.FontFamily = family);
        var preamble = BuildPreamble(doc);
        preamble.Should().Contain($"\\usepackage{{{pkg}}}");
    }

    [Fact]
    public void FontFamily_Georgia_emits_no_font_package()
    {
        // Decision LILIA-120: Georgia is dropped from supported fonts
        // (no native pdflatex equivalent without xelatex+fontspec). The
        // settings UI cleanup is a separate ticket; for now, picking
        // Georgia silently falls through to the class default.
        var doc = NewDoc(d => d.FontFamily = "Georgia");
        var preamble = BuildPreamble(doc);
        preamble.Should().NotContain("\\usepackage{georgia}");
        preamble.Should().NotContain("Georgia");
    }

    // ── FontSize / PaperSize / Columns (sanity — already-✅ rows) ─────

    [Fact]
    public void FontSize_emits_pt_class_option()
    {
        var doc = NewDoc(d => d.FontSize = 11);
        var directive = LaTeXPreambleBuilder.BuildClassDirective(doc);
        directive.Should().Contain("11pt");
    }

    [Fact]
    public void PaperSize_letter_emits_letterpaper_class_option()
    {
        var doc = NewDoc(d => d.PaperSize = "letter");
        var directive = LaTeXPreambleBuilder.BuildClassDirective(doc);
        directive.Should().Contain("letterpaper");
    }

    [Fact]
    public void Columns_2_without_balanced_emits_twocolumn_class_option()
    {
        var doc = NewDoc(d =>
        {
            d.Columns = 2;
            d.BalancedColumns = false;
        });
        var directive = LaTeXPreambleBuilder.BuildClassDirective(doc);
        directive.Should().Contain("twocolumn");
    }

    // ── Defaults / no-op behaviour ─────────────────────────────────────

    [Fact]
    public void Defaults_emit_minimal_preamble_with_no_layout_overrides()
    {
        // A blank-default doc should NOT emit geometry / fancyhdr /
        // setspace / pagenumbering / parindent / multicol / fontfamily
        // overrides. Only the class directive with FontSize+PaperSize
        // tokens is required.
        var doc = NewDoc();
        var result = LaTeXPreambleBuilder.Build(doc);
        result.ClassDirective.Should().StartWith("\\documentclass[12pt,a4paper]");
        result.LayoutPreamble.Should().NotContain("geometry");
        result.LayoutPreamble.Should().NotContain("fancyhdr");
        result.LayoutPreamble.Should().NotContain("\\onehalfspacing");
        result.LayoutPreamble.Should().NotContain("\\doublespacing");
        result.LayoutPreamble.Should().NotContain("\\linespread");
        result.LayoutPreamble.Should().NotContain("\\pagenumbering");
        result.LayoutPreamble.Should().NotContain("\\setlength{\\parindent}");
        result.LayoutPreamble.Should().NotContain("multicol");
        result.BodyOpener.Should().BeEmpty();
        result.BodyCloser.Should().BeEmpty();
    }
}
