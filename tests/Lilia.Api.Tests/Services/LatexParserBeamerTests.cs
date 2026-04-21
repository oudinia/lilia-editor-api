using FluentAssertions;
using Lilia.Import.Services;
using Lilia.Import.Models;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Batch C coverage tests — beamer frame shim. Each test feeds a
/// typical beamer pattern and asserts the Lilia block tree comes out
/// navigable (heading + children) rather than a raw passthrough.
/// </summary>
public class LatexParserBeamerTests
{
    private readonly LatexParser _parser = new();

    [Fact]
    public async Task Frame_with_brace_title_emits_heading_plus_body()
    {
        const string src = @"
\documentclass{beamer}
\begin{document}
\begin{frame}{Introduction}
This slide introduces the topic.
\begin{itemize}
\item First point
\item Second point
\end{itemize}
\end{frame}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        doc.Elements.OfType<ImportHeading>()
            .Should().Contain(h => h.Text.Contains("Introduction"),
                "frame title should become a heading");

        doc.Elements.OfType<ImportParagraph>()
            .Should().Contain(p => p.Text.Contains("introduces the topic"),
                "frame body paragraphs should be emitted as normal paragraphs");

        doc.Elements.OfType<ImportListItem>()
            .Should().HaveCountGreaterThanOrEqualTo(2,
                "frame body lists should be emitted as list items");
    }

    [Fact]
    public async Task Frame_with_options_and_title_still_emits_heading()
    {
        const string src = @"
\documentclass{beamer}
\begin{document}
\begin{frame}[allowframebreaks,label=intro]{A longer frame title}
Content here.
\end{frame}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        doc.Elements.OfType<ImportHeading>()
            .Should().Contain(h => h.Text.Contains("A longer frame title"),
                "frame options [allowframebreaks,label=intro] should be discarded, title still extracted");
    }

    [Fact]
    public async Task Frame_with_frametitle_command_emits_heading()
    {
        const string src = @"
\documentclass{beamer}
\begin{document}
\begin{frame}
\frametitle{Set via command}
Slide body.
\end{frame}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        doc.Elements.OfType<ImportHeading>()
            .Should().Contain(h => h.Text.Contains("Set via command"),
                "\\frametitle inside a frame should become a heading");
    }

    [Fact]
    public async Task Titlepage_rewrites_to_maketitle_and_captures_preamble()
    {
        const string src = @"
\documentclass{beamer}
\title{My Presentation}
\author{O. Dinia}
\date{April 2026}
\begin{document}
\begin{frame}
\titlepage
\end{frame}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        // Document metadata picks up title / author from the preamble
        // regardless of where \titlepage appeared.
        doc.Title.Should().Be("My Presentation");
    }
}
