using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

public class LatexPageSetupExtractorTests
{
    /// <summary>A lecture-notes preamble of the kind the author feeds Ask Lilia.</summary>
    internal const string Lecture = @"
\documentclass[12pt,a4paper,twoside]{article}
\usepackage[T1]{fontenc}
\usepackage[utf8]{inputenc}
\usepackage[top=2.5cm,bottom=2.5cm,left=3cm,right=2cm]{geometry}
\usepackage{mathpazo}
\usepackage{setspace}
\usepackage{fancyhdr}
\usepackage{amsmath}
\onehalfspacing
\setlength{\parindent}{0pt}
\newcommand{\R}{\mathbb{R}}
\DeclareMathOperator{\tr}{tr}
\pagestyle{fancy}
\fancyhf{}
\fancyhead[L]{Lecture 3}
\fancyhead[R]{\textit{Fluid dynamics}}
\fancyfoot[C]{\thepage}
\lfoot{M. Curie}
\pagenumbering{roman}
\title{Fluid dynamics}
\author{M. Curie}
\begin{document}
\maketitle
\section{Intro}
Hello $\R$.
\end{document}";

    private static LatexPageSetupExtractor.Result Run(string tex) =>
        LatexPageSetupExtractor.Extract(tex, new LatexParser().ParseTextAsync(tex).GetAwaiter().GetResult().Metadata);

    [Fact]
    public void A_lecture_preamble_becomes_settings()
    {
        var r = Run(Lecture);
        var s = r.Settings;
        s.FontSize.Should().Be(12);
        s.PaperSize.Should().Be("a4");
        s.MarginTop.Should().Be("2.5cm");
        s.MarginBottom.Should().Be("2.5cm");
        s.MarginLeft.Should().Be("3cm");
        s.MarginRight.Should().Be("2cm");
        s.FontFamily.Should().Be("palatino");
        s.LineSpacing.Should().Be(1.5);
        s.ParagraphIndent.Should().Be("none");
        s.PageNumbering.Should().Be("roman");
        s.HeaderLeft.Should().Be("Lecture 3");
        s.HeaderRight.Should().Be("Fluid dynamics", "\\textit is unwrapped to its text");
        s.FooterLeft.Should().Be("M. Curie");
        s.FooterCenter.Should().BeNull("only \\thepage was there, which a plain slot cannot hold");
        s.CustomPreamble.Should().Contain(@"\newcommand{\R}").And.Contain(@"\DeclareMathOperator{\tr}");
        r.Sides.Should().Be("two");
        r.DocumentClass.Should().Be("article");
        r.PackagesJson.Should().Contain("amsmath");
    }

    [Fact]
    public void What_cannot_be_carried_is_reported_not_dropped_silently()
    {
        var r = Run(Lecture);
        r.NotApplied.Should().Contain(n => n.Contains(@"\thepage") && n.Contains("page number"));
        r.Applied.Should().Contain(a => a.StartsWith("marginLeft = 3cm"));
    }

    [Fact]
    public void Landscape_two_column_geometry_margin_and_class_options()
    {
        var r = Run(@"\documentclass[11pt,twocolumn,landscape,letterpaper]{report}
\usepackage[margin=1in]{geometry}
\setlength{\columnsep}{10mm}
\setlength{\columnseprule}{0.4pt}
\begin{document}x\end{document}");
        var s = r.Settings;
        s.FontSize.Should().Be(11);
        s.Columns.Should().Be(2);
        s.PaperSize.Should().Be("letter");
        s.Orientation.Should().Be("landscape");
        s.MarginTop.Should().Be("1in");
        s.MarginRight.Should().Be("1in");
        s.ColumnGap.Should().Be(1.0);
        s.ColumnSeparator.Should().Be("rule");
        r.DocumentClass.Should().Be("report");
    }

    [Fact]
    public void Unsupported_features_are_named()
    {
        var r = Run(@"\documentclass[14pt]{extarticle}
\usepackage{fontspec}
\setmainfont{Garamond}
\usepackage[a4paper,text={12cm,20cm}]{geometry}
\usepackage{draftwatermark}
\usepackage{titlesec}
\usepackage{parskip}
\begin{document}
\begin{multicols}{2}x\end{multicols}
\end{document}");
        r.NotApplied.Should().Contain(n => n.Contains("14pt"));
        r.NotApplied.Should().Contain(n => n.Contains("fontspec") && n.Contains("not supported"));
        r.NotApplied.Should().Contain(n => n.Contains("text={12cm,20cm}"));
        r.NotApplied.Should().Contain(n => n.Contains("watermark"));
        r.NotApplied.Should().Contain(n => n.Contains("titlesec"));
        r.NotApplied.Should().Contain(n => n.Contains("parskip"));
        r.NotApplied.Should().Contain(n => n.Contains("multicols"));
        r.Settings.FontSize.Should().BeNull();
        r.Settings.PaperSize.Should().Be("a4");
    }

    [Fact]
    public void A_margin_Lilia_cannot_hold_is_skipped_and_the_rest_survive()
    {
        var r = Run(@"\documentclass{article}
\usepackage[left=0.2\textwidth,right=2cm]{geometry}
\begin{document}x\end{document}");
        r.Settings.MarginLeft.Should().BeNull();
        r.Settings.MarginRight.Should().Be("2cm");
        r.NotApplied.Should().Contain(n => n.StartsWith("marginLeft: not applied") && n.Contains("unit"));
    }

    [Fact]
    public void Page_style_empty_means_no_page_numbers_and_linespread_is_read()
    {
        var r = Run(@"\documentclass{article}
\linespread{1.3}
\pagestyle{empty}
\renewcommand{\familydefault}{\sfdefault}
\begin{document}x\end{document}");
        r.Settings.PageNumbering.Should().Be("none");
        r.Settings.LineSpacing.Should().Be(1.3);
        r.Settings.FontFamily.Should().Be("sans-serif");
    }

    [Fact]
    public void Comments_are_not_settings()
    {
        var r = Run(@"\documentclass{article}
% \pagenumbering{roman}
% \usepackage[margin=5cm]{geometry}
\begin{document}x\end{document}");
        r.Settings.PageNumbering.Should().BeNull();
        r.Settings.MarginLeft.Should().BeNull();
    }

    [Fact]
    public void Header_text_is_plain()
    {
        LatexPageSetupExtractor.PlainText(@"\textbf{Notes} \& \emph{more}\hfill \thepage", out var dropped)
            .Should().Be("Notes & more");
        dropped.Should().Contain(@"\thepage");
    }

    // ── review of 5 Oct: the notes said a header removed the page number ──

    private const string Head = "\\documentclass{article}\n\\usepackage{fancyhdr}\n\\pagestyle{fancy}\n";
    private const string Body = "\\begin{document}\nText.\n\\end{document}\n";

    [Fact]
    public void A_header_alone_does_not_claim_the_page_number_is_lost()
    {
        var r = Run(Head + "\\fancyhead[L]{Lecture 8}\n" + Body);
        r.NotApplied.Should().NotContain(n => n.Contains("does not print"));
    }

    [Fact]
    public void A_footer_page_number_with_no_footer_slot_is_said_to_be_printed()
    {
        var r = Run(Head + "\\fancyhead[L]{Lecture 8}\n\\fancyfoot[C]{\\thepage}\n" + Body);
        r.NotApplied.Should().Contain(n => n.Contains("still printed"));
        r.NotApplied.Should().NotContain(n => n.Contains("not reproduced"));
    }

    [Fact]
    public void A_footer_slot_is_said_to_replace_the_page_number()
    {
        var r = Run(Head + "\\fancyfoot[L]{Draft}\n" + Body);
        r.NotApplied.Should().Contain(n => n.Contains("does not print the automatic page number"));
    }
}
