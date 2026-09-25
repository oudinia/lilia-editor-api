using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// The Typst preview prints references the way the PDF does.
///
/// <para>Before this, the preview — what an author sees by default — printed
/// <c>\cref{…}</c> as raw text, and a <c>\ref</c> made Typst refuse the whole
/// document ("label does not exist"), so the preview silently fell back to the
/// slow pdflatex path. Each expectation below is what pdflatex printed for the
/// same command, measured on 25 Sep; these tests compile real Typst and read
/// the PDF back.</para>
/// </summary>
public class TypstReferencesTests
{
    private static string Build(params Lilia.Core.Entities.Block[] blocks) =>
        new TypstExportService().BuildTypstDocument(Doc(), [.. blocks], null);

    private static readonly Lilia.Core.Entities.Block[] Targets =
    [
        Block("heading", new { text = "Method", level = 1, label = "sec:method" }, 1),
        Block("table", new
        {
            caption = "Top-1 accuracy", label = "tab:results", hasHeader = true,
            rows = new[] { new[] { "Model", "Top-1" }, new[] { "ResNet", "76.1" } },
        }, 2),
        Block("equation", new { source = "a = b", displayMode = true, label = "eq:loss" }, 3),
        Block("figure", new { src = "https://example.org/arch.png", caption = "Architecture", label = "fig:arch" }, 4),
        Block("theorem", new { kind = "theorem", text = "All is well.", label = "thm:main" }, 5),
    ];

    /// <summary>Each probe paragraph reads "P-name=… end."; returns name → what the PDF shows.</summary>
    private static Dictionary<string, string> Probe(params (string Name, string Latex)[] probes)
    {
        var blocks = Targets.Concat(probes.Select((p, i) => Para($"P-{p.Name}={p.Latex} end.", 10 + i))).ToArray();
        var source = Build(blocks);
        var (text, error) = CompileToText(source);
        text.Should().NotBeNull($"the preview must compile — typst said: {error}\n\n{source}");
        return Regex.Matches(Regex.Replace(text!, @"\s+", " "), @"P-([A-Za-z0-9-]+)=(.*?) end\.")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    }

    [Fact]
    public void Each_command_prints_what_pdflatex_prints()
    {
        var shown = Probe(
            ("ref-tab", @"\ref{tab:results}"),
            ("cref-tab", @"\cref{tab:results}"),
            ("Cref-tab", @"\Cref{tab:results}"),
            ("autoref-tab", @"\autoref{tab:results}"),
            ("eqref-tab", @"\eqref{tab:results}"),
            ("pageref-tab", @"\pageref{tab:results}"),
            ("nameref-tab", @"\nameref{tab:results}"),
            ("ref-eq", @"\ref{eq:loss}"),
            ("cref-eq", @"\cref{eq:loss}"),
            ("Cref-eq", @"\Cref{eq:loss}"),
            ("autoref-eq", @"\autoref{eq:loss}"),
            ("eqref-eq", @"\eqref{eq:loss}"),
            ("cref-sec", @"\cref{sec:method}"),
            ("Cref-sec", @"\Cref{sec:method}"),
            ("autoref-sec", @"\autoref{sec:method}"),
            ("cref-fig", @"\cref{fig:arch}"),
            ("Cref-fig", @"\Cref{fig:arch}"),
            ("at-ref", "@ref{tab:results}"));

        shown.Should().Equal(new Dictionary<string, string>
        {
            ["ref-tab"] = "1",
            ["cref-tab"] = "table 1",
            ["Cref-tab"] = "Table 1",
            ["autoref-tab"] = "Table 1",
            ["eqref-tab"] = "(1)",
            ["pageref-tab"] = "1",
            ["nameref-tab"] = "Top-1 accuracy",
            ["ref-eq"] = "1",
            ["cref-eq"] = "eq. (1)",
            ["Cref-eq"] = "Equation (1)",
            ["autoref-eq"] = "Equation 1",
            ["eqref-eq"] = "(1)",
            ["cref-sec"] = "section 1",
            ["Cref-sec"] = "Section 1",
            ["autoref-sec"] = "section 1",
            ["cref-fig"] = "fig. 1",
            ["Cref-fig"] = "Figure 1",
            ["at-ref"] = "1",
        });
    }

    [Fact]
    public void A_missing_label_prints_question_marks_and_the_document_still_compiles()
    {
        // It used to fail the whole compile, so the preview fell back to pdflatex.
        var shown = Probe(("ref-gone", @"\ref{fig:gone}"), ("cref-gone", @"\cref{fig:gone}"), ("eqref-gone", @"\eqref{fig:gone}"));
        shown.Should().Equal(new Dictionary<string, string>
        {
            ["ref-gone"] = "??", ["cref-gone"] = "??", ["eqref-gone"] = "(??)",
        });
    }

    [Fact]
    public void A_label_the_preview_cannot_number_shows_the_pending_slot_not_a_wrong_number()
    {
        // A theorem is a plain block in the preview; pdflatex numbers it. The
        // slot says "no number here yet", never ?? — the label is not broken.
        var shown = Probe(("cref-thm", @"\cref{thm:main}"), ("ref-thm", @"\ref{thm:main}"));
        shown["cref-thm"].Should().Be($"theorem {TypstExportService.PendingSlot}");
        shown["ref-thm"].Should().Be(TypstExportService.PendingSlot);
    }

    [Fact]
    public void Numbers_count_like_pdflatex_starred_headings_and_equations_take_none()
    {
        var shown = Probe(("second-eq", @"\eqref{eq:two}"), ("second-sec", @"\ref{sec:two}"));
        _ = shown; // establishes the Probe baseline compiles; the real check follows

        var blocks = Targets.Concat(new[]
        {
            Block("heading", new { text = "Aside", level = 1, numbered = false }, 6),
            Block("equation", new { source = "c = d", displayMode = true, numbered = false }, 7),
            Block("heading", new { text = "Results", level = 1, label = "sec:two" }, 8),
            Block("equation", new { source = "e = f", displayMode = true, label = "eq:two" }, 9),
            Para(@"P-second-eq=\eqref{eq:two} end.", 20),
            Para(@"P-second-sec=\ref{sec:two} end.", 21),
        }).ToArray();
        var (text, error) = CompileToText(Build(blocks));
        text.Should().NotBeNull(error);
        var flat = Regex.Replace(text!, @"\s+", " ");
        flat.Should().Contain("P-second-eq=(2) end.", "the unnumbered equation between them takes no number");
        flat.Should().Contain("P-second-sec=2 end.", "the unnumbered heading between them takes no number");
    }

    [Fact]
    public void A_key_with_an_underscore_survives_the_italic_rule()
    {
        var blocks = new[]
        {
            Block("table", new { caption = "T", label = "tab:top_1_acc", rows = new[] { new[] { "a" } } }, 1),
            Para(@"P-under=\cref{tab:top_1_acc} end.", 2),
        };
        var source = Build(blocks);
        var (text, error) = CompileToText(source);
        text.Should().NotBeNull(error);
        Regex.Replace(text!, @"\s+", " ").Should().Contain("P-under=table 1 end.", source);
    }
}
