namespace Lilia.Api.Tests.Integration.LatexStaging;

/// <summary>
/// .tex fixtures driving the staging-pipeline integration tests. Each
/// fixture targets a specific diagnostic category so coverage assertions
/// stay explicit. Complexity mix: clean article, moderncv, altacv, a
/// beamer deck, a load-order-trap, a dirty CV with missing asset, and a
/// programmatically-generated "big thesis" for the bulk-COPY path.
/// </summary>
internal static class LatexStagingFixtures
{
    public const string CleanArticle = @"\documentclass{article}
\usepackage{amsmath}
\title{A Perfectly Clean Paper}
\author{Test Author}

\begin{document}
\maketitle

\section{Introduction}
This is the opening paragraph. It contains no warnings at all.

\section{Methods}
A second paragraph with an inline equation $E = mc^2$ and a display:
\begin{equation}
\int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}.
\end{equation}

\section{Results}
Here are some findings with a list.
\begin{itemize}
  \item First finding.
  \item Second finding.
\end{itemize}

\section{Conclusion}
Everything worked out fine.
\end{document}
";

    public const string Beamer = @"\documentclass{beamer}
\usetheme{Madrid}
\title{Our Results}
\author{Alice}

\begin{document}
\begin{frame}
  \titlepage
\end{frame}

\begin{frame}{Introduction}
  First slide body.
\end{frame}

\begin{frame}{Conclusion}
  Final slide.
\end{frame}
\end{document}
";

    // Trigger multiple load-order traps in a single file so we can assert
    // detection without needing many different fixtures.
    public const string LoadOrderTraps = @"\documentclass{article}
\usepackage{cleveref}
\usepackage{hyperref}
\usepackage{subfig}
\usepackage{subcaption}

\begin{document}
\section{Load-order Test}
A paragraph to make the body non-empty.
\end{document}
";

    // ModernCV — one of the dominant CV classes. Should import as a series
    // of CV-domain blocks, zero errors.
    public const string ModernCv = @"\documentclass[11pt,a4paper]{moderncv}
\moderncvstyle{classic}
\moderncvcolor{blue}

\name{John}{Doe}
\title{Senior Software Engineer}
\email{john.doe@example.com}
\phone[mobile]{+1~(555)~555-1234}
\social[linkedin]{johndoe}

\begin{document}
\makecvtitle

\section{Experience}
\cventry{2020--Present}{Senior Engineer}{Acme Corp}{San Francisco}{}{Led a team of 5 engineers on a distributed ledger platform.}
\cventry{2017--2020}{Engineer}{Beta Co}{Seattle}{}{Built the data ingest pipeline from scratch.}

\section{Education}
\cventry{2013--2017}{B.S. Computer Science}{University of Somewhere}{City}{GPA: 3.8}{}

\section{Skills}
\cvitem{Languages}{C\#, TypeScript, Go, Python}
\cvitem{Platforms}{AWS, PostgreSQL, Kubernetes}
\end{document}
";

    // AltACV — stylish CV class, even less likely to have its .sty on the
    // server. Tests the fallback + shim path.
    public const string AltaCv = @"\documentclass[10pt,a4paper,ragged2e]{altacv}
\usepackage{fontawesome5}

\name{Jane Smith}
\tagline{Research Scientist — Machine Learning}
\photo{2cm}{profile}
\personalinfo{
  \email{jane.smith@example.com}
  \phone{+44~20~7946~0958}
  \location{London, UK}
  \linkedin{janesmith}
}

\begin{document}
\makecvheader

\cvsection{Experience}
\cvevent{Senior ML Researcher}{DeepLab}{2021--Now}{London}
\begin{itemize}
\item Designed transformer variants for time-series.
\item Mentored 3 PhD interns on reproducibility.
\end{itemize}

\cvsection{Publications}
Smith, J. et al. ``Scaling Laws Revisited.'' NeurIPS 2024.
\end{document}
";

    // Passes a minimal body but references a figure file we won't provide.
    // Relies on parser emitting the ImportImage element even when the asset
    // is absent, so the job can later flag missing_asset.
    public const string MissingAsset = @"\documentclass{article}
\usepackage{graphicx}

\begin{document}
\section{Experiment}
Below is our experimental rig.

\begin{figure}[h]
  \centering
  \includegraphics[width=0.5\linewidth]{figures/rig.png}
  \caption{Experimental apparatus.}
  \label{fig:rig}
\end{figure}

Discussion follows.
\end{document}
";

    /// <summary>
    /// Generate a programmatic thesis-sized .tex with many sections to
    /// exercise the COPY path past the ≤500-row threshold. Returns roughly
    /// 600+ parsed blocks.
    /// </summary>
    public static string GenerateLargeThesis(int sectionCount = 40, int paragraphsPerSection = 15)
    {
        using var sw = new StringWriter();
        sw.WriteLine(@"\documentclass{report}");
        sw.WriteLine(@"\usepackage{amsmath}");
        sw.WriteLine(@"\title{A Generated Thesis for Bulk Insert Testing}");
        sw.WriteLine(@"\author{Integration Suite}");
        sw.WriteLine(@"\begin{document}");
        sw.WriteLine(@"\maketitle");

        for (var s = 1; s <= sectionCount; s++)
        {
            sw.WriteLine($"\\section{{Section {s}}}");
            for (var p = 1; p <= paragraphsPerSection; p++)
            {
                sw.WriteLine($"Paragraph {p} of section {s}. The text contains some content to parse, an inline $x_{{{p}}}$ formula, and a reference to Section {s}. More filler follows so the paragraph is meaningful.");
                sw.WriteLine();
            }
        }

        sw.WriteLine(@"\end{document}");
        return sw.ToString();
    }
}
