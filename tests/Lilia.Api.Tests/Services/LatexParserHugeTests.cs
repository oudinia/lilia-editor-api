using FluentAssertions;
using Lilia.Import.Services;
using Lilia.Import.Models;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Diagnostic round-trip checks for LaTeX size-switch commands
/// (`\Huge`, `\large`, etc.). Drives the discussion of whether
/// import-side stripping silently loses formatting on a real .tex
/// document. Test bodies use ITestOutputHelper so the assertions
/// double as a visible report ("here's what the user sees today").
/// </summary>
public class LatexParserHugeTests
{
    private readonly LatexParser _parser = new();
    private readonly ITestOutputHelper _output;

    public LatexParserHugeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Standalone_Huge_in_paragraph_is_silently_stripped()
    {
        const string src = @"
\documentclass{article}
\begin{document}
\Huge This headline should be very large.

Normal paragraph after the headline.
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        var paragraphs = doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).ToList();
        _output.WriteLine("===== Standalone \\Huge =====");
        foreach (var p in paragraphs) _output.WriteLine($"  paragraph: {p}");

        // Show the user that \Huge has been silently dropped.
        paragraphs.Should().Contain(p => p.Contains("This headline"),
            "paragraph text survives");
        paragraphs.Should().NotContain(p => p.Contains("\\Huge"),
            "but the size command is stripped — silent data loss");
    }

    [Fact]
    public async Task Inline_braced_Huge_loses_the_command_keeps_the_text()
    {
        const string src = @"
\documentclass{article}
\begin{document}
A line with {\Huge inline emphasis} sitting inside it.
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        var paragraphs = doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).ToList();
        _output.WriteLine("===== Inline {\\Huge ...} =====");
        foreach (var p in paragraphs) _output.WriteLine($"  paragraph: {p}");

        paragraphs.Should().Contain(p => p.Contains("inline emphasis"));
        paragraphs.Should().NotContain(p => p.Contains("\\Huge"));
    }

    [Fact]
    public async Task Real_world_cv_with_size_commands_loses_visual_hierarchy()
    {
        // The kind of .tex a CV / résumé / poster uses to size the name banner.
        const string src = @"
\documentclass{article}
\begin{document}
{\Huge \textbf{Jane Doe}}\\
{\large Senior Researcher, Centre for Antiquity Studies}\\
{\small jane@example.org}

\section{Education}
PhD in Archaeology, 2024.
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        _output.WriteLine("===== CV-style document =====");
        foreach (var el in doc.Elements)
        {
            _output.WriteLine($"  {el.GetType().Name}: {(el switch
            {
                ImportHeading h => h.Text,
                ImportParagraph p => p.Text,
                _ => el.ToString()
            })}");
        }

        // We keep the name + the role text, but the visual hierarchy
        // (Huge / large / small) is gone. After this, edit + export
        // won't put the size commands back. That is the round-trip bug.
        var allText = string.Join(" ", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        allText.Should().Contain("Jane Doe");
        allText.Should().NotContain("\\Huge", "size command silently removed on import");
        allText.Should().NotContain("\\large", "size command silently removed on import");
        allText.Should().NotContain("\\small", "size command silently removed on import");
    }
}
