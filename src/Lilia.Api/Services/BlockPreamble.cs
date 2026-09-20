namespace Lilia.Api.Services;

/// <summary>
/// The preamble a single block is judged in.
/// </summary>
/// <remarks>
/// One backend, one pattern: this is not table-specific and must not become so.
/// Every tool that renders a block — tables today, equations and figures next —
/// validates against the same preamble, because they all end up in the same kind
/// of document.
///
/// The packages are the subset of <c>RenderService</c>'s generated preamble that
/// block content can actually reach. A block that compiles here compiles in the
/// paper it is going into, which is the only useful meaning of "valid".
/// </remarks>
public static class BlockPreamble
{
    private const string Head = """
\documentclass{article}
\usepackage[T1]{fontenc}
\usepackage{amsmath}
\usepackage{amssymb}
\usepackage{mathtools}
\usepackage{siunitx}
\usepackage{booktabs}
\usepackage{multirow}
\usepackage{tabularx}
\usepackage{longtable}
\usepackage{array}
\usepackage{graphicx}
\usepackage{float}
\usepackage{caption}
\usepackage{xcolor}
\usepackage{enumitem}
\usepackage{listings}
""";

    /// <summary>Wrap a rendered block so it can be compiled on its own.</summary>
    public static string Wrap(string fragment) =>
        $"{Head}\\begin{{document}}\n{fragment}\n\\end{{document}}\n";
}
