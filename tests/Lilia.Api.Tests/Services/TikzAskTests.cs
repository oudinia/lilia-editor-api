using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.Extensions.AI;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Ask Lilia on a figure (TikZ step 3, 1c): the context it reads and nothing more, the prompt's
/// rules, how a reply is read, the minimality check, and the compile-before-propose loop
/// (draws first try; draws on retry with the error fed back; fails twice → "I couldn't get this
/// to draw", with the line). The model is scripted and the compiler faked: no AI is called.
/// </summary>
public class TikzAskTests
{
    private const string Triangle = """
        \begin{tikzpicture}[node distance=2.4cm]
          \node (A) {$A$};
          \node (B) [right of=A] {$B$};
          \node (C) [below of=B] {$C$};
          \draw[->] (A) -- node[above] {$f$} (B);
          \draw[->] (B) -- node[right] {$g$} (C);
          \draw[->, lilia-accent] (A) -- node[below left] {$g\circ f$} (C);
        \end{tikzpicture}
        """;

    private static readonly IReadOnlyDictionary<string, string> Colours =
        new Dictionary<string, string> { ["lilia-ink"] = "#22262B", ["lilia-accent"] = "#0A76A4" };

    private static BlockDto B(Guid id, string type, object content, int sort) =>
        new(id, Guid.Empty, type, JsonSerializer.SerializeToElement(content), sort, null, 0, DateTime.UtcNow, DateTime.UtcNow);

    private static (DocumentDto Doc, Guid Figure, Guid Before) Document()
    {
        var figure = Guid.NewGuid();
        var before = Guid.NewGuid();
        var doc = AskLiliaHarness.Doc(
            B(Guid.NewGuid(), "paragraph", new { text = "FAR AWAY secret paragraph." }, 0),
            B(Guid.NewGuid(), "heading", new { text = "Composites", level = 2 }, 1),
            B(before, "paragraph", new { text = "The composite g∘f is drawn below." }, 2),
            B(figure, "figure", new { kind = "tikz", source = Triangle, caption = "The composite g∘f.", label = "fig:tri" }, 3),
            B(Guid.NewGuid(), "paragraph", new { text = "As the triangle shows, it commutes." }, 4),
            B(Guid.NewGuid(), "figure", new { kind = "tikz", source = @"\begin{tikzpicture}\node {OTHER FIGURE};\end{tikzpicture}", caption = "Other" }, 5),
            B(Guid.NewGuid(), "paragraph", new { text = "LATER secret paragraph." }, 6))
            with
        {
            Title = "SECRET TITLE",
            CustomPreamble = "\\newcommand{\\secretmacro}{x}\n\\usetikzlibrary{arrows.meta}\n\\tikzset{every node/.style={font=\\small}}",
        };
        return (doc, figure, before);
    }

    // ── Context: what it reads, and only that ──────────────────────────

    [Fact]
    public void The_context_is_the_figure_its_preamble_lines_the_colours_and_the_paragraphs_around_it_and_nothing_else()
    {
        var (doc, figure, _) = Document();
        var ctx = TikzAsk.Context(doc, new TikzAskFigure(BlockId: figure.ToString()), Colours)!;
        var prompt = TikzAsk.SystemPrompt(ctx, "change");

        prompt.Should().Contain(@"2|   \node (A) {$A$};", "the source is numbered for line chips")
            .And.Contain("The composite g∘f.").And.Contain("fig:tri")
            .And.Contain(@"\usetikzlibrary{arrows.meta}").And.Contain(@"\tikzset{every node/.style={font=\small}}")
            .And.Contain("lilia-accent #0A76A4")
            .And.Contain("The composite g∘f is drawn below.")
            .And.Contain("As the triangle shows, it commutes.");

        prompt.Should().NotContain("FAR AWAY").And.NotContain("LATER secret").And.NotContain("OTHER FIGURE")
            .And.NotContain("SECRET TITLE").And.NotContain("secretmacro", "only the preamble's TikZ lines");
    }

    [Fact]
    public void The_source_the_author_sees_wins_over_the_saved_one()
    {
        var (doc, figure, _) = Document();
        var ctx = TikzAsk.Context(doc, new TikzAskFigure(BlockId: figure.ToString(), Source: @"\begin{tikzpicture}\draw (0,0) -- (1,0);\end{tikzpicture}", Caption: "Typed"), Colours)!;
        ctx.Source.Should().Contain("(1,0)");
        ctx.Caption.Should().Be("Typed");
        ctx.Label.Should().Be("fig:tri", "unsent fields come from the saved block");
    }

    [Fact]
    public void A_block_that_is_not_a_tikz_figure_of_this_document_has_no_context()
    {
        var (doc, _, before) = Document();
        TikzAsk.Context(doc, new TikzAskFigure(BlockId: before.ToString()), Colours).Should().BeNull("a paragraph is not a figure");
        TikzAsk.Context(doc, new TikzAskFigure(BlockId: Guid.NewGuid().ToString()), Colours).Should().BeNull("another document's block");
        TikzAsk.Context(doc, new TikzAskFigure(BlockId: "nope"), Colours).Should().BeNull();
    }

    [Fact]
    public void A_new_figure_reads_the_paragraph_where_it_will_go()
    {
        var (doc, _, before) = Document();
        var ctx = TikzAsk.Context(doc, new TikzAskFigure(AfterBlockId: before.ToString(), Intent: "draw"), Colours)!;
        ctx.IsNew.Should().BeTrue();
        ctx.Source.Should().BeEmpty();
        ctx.ParagraphBefore.Should().Be("The composite g∘f is drawn below.");
        TikzAsk.SystemPrompt(ctx, "draw").Should().Contain("Source: (empty)").And.Contain("a new figure").And.NotContain("FAR AWAY");
    }

    [Fact]
    public void The_prompt_carries_the_rules_for_the_authors_tikz()
    {
        var (doc, figure, _) = Document();
        var prompt = TikzAsk.SystemPrompt(TikzAsk.Context(doc, new TikzAskFigure(BlockId: figure.ToString()), Colours)!, null);
        prompt.Should().Contain("Change only the lines the request needs")
            .And.Contain("Never reformat, re-indent, reorder")
            .And.Contain("Keep the author's naming")
            .And.Contain("Prefer the theme colour names")
            .And.Contain("Never add a TikZ library the figure does not need")
            .And.Contain("not the rest of the document");
        prompt.Should().NotContain(@"\\begin", "the model sees single backslashes");
    }

    [Fact]
    public void The_error_goes_into_the_context_for_fix_this_error()
    {
        var (doc, figure, _) = Document();
        var ctx = TikzAsk.Context(doc, new TikzAskFigure(BlockId: figure.ToString(), Intent: "fix", Error: new TikzAskFigureError(5, "This path doesn't end.")), Colours)!;
        TikzAsk.SystemPrompt(ctx, "fix").Should().Contain("It does not draw: line 5: This path doesn't end.").And.Contain("fix the error");
    }

    // ── Reading a reply ────────────────────────────────────────────────

    [Fact]
    public void A_reply_is_read_into_prose_source_preamble_caption_and_line_chips()
    {
        var reply = TikzAsk.Parse("""
            Here's the square. It uses positioning.
            ```tikz
            \begin{tikzpicture}
              \node (a) {A};
            \end{tikzpicture}
            ```
            ```preamble
            \usetikzlibrary{positioning}
            ```
            """, 0);
        reply.Prose.Should().Be("Here's the square. It uses positioning.");
        reply.Source.Should().Be("\\begin{tikzpicture}\n  \\node (a) {A};\n\\end{tikzpicture}");
        reply.PreambleLines.Should().Equal(@"\usetikzlibrary{positioning}");

        var explain = TikzAsk.Parse("A commutative triangle.\n[L2-4] Three nodes.\n- [L5–6]: The arrows f and g.\n[L7] The composite.\n[L40] Beyond the end.", 8);
        explain.Prose.Should().Be("A commutative triangle.");
        explain.Lines.Should().Equal(
            new TikzAskLine(2, 4, "Three nodes."), new TikzAskLine(5, 6, "The arrows f and g."),
            new TikzAskLine(7, 7, "The composite."), new TikzAskLine(8, 8, "Beyond the end."));

        TikzAsk.Parse("```caption\nThe composite $g\\circ f$.\n```", 8).Caption.Should().Be("The composite $g\\circ f$.");
    }

    [Fact]
    public void Line_numbers_copied_from_the_prompt_are_taken_off()
    {
        TikzAsk.StripNumbers("1| \\begin{tikzpicture}\n2|   \\node {A};\n3| \\end{tikzpicture}")
            .Should().Be("\\begin{tikzpicture}\n  \\node {A};\n\\end{tikzpicture}");
        TikzAsk.StripNumbers("\\begin{tikzpicture}\n2| x\n\\end{tikzpicture}").Should().Contain("2| x", "only when every line has one");
    }

    [Fact]
    public void Only_library_lines_the_preamble_lacks_are_added()
    {
        TikzAsk.PreambleAdditions(new[]
        {
            @"\usetikzlibrary{positioning}", @"\usetikzlibrary{arrows.meta}", @"\usepackage{evil}",
            @"\usetikzlibrary{positioning}", @"\input{secrets}", @"\usepgfplotslibrary{fillbetween}",
        }, @"\usetikzlibrary{arrows.meta, calc}")
            .Should().Equal(@"\usetikzlibrary{positioning}", @"\usepgfplotslibrary{fillbetween}");
    }

    // ── Minimality ─────────────────────────────────────────────────────

    [Fact]
    public void Lines_the_model_only_reindented_go_back_to_the_authors()
    {
        var proposed = Triangle
            .Replace("  \\node (A) {$A$};", "    \\node (A)   {$A$};")
            .Replace("\\draw[->] (A) -- node[above] {$f$} (B);", "\\draw[->, dashed] (A) -- node[above] {$f$} (B);");
        var kept = TikzAsk.KeepUntouchedLines(Triangle, proposed, "make f dashed");
        kept.Should().Contain("\n  \\node (A) {$A$};\n").And.Contain("[->, dashed] (A)");
        TikzAsk.ChangedLines(Triangle, kept).Should().Be(1);

        TikzAsk.KeepUntouchedLines(Triangle, proposed, "fix the indentation and make f dashed")
            .Should().Contain("    \\node (A)   {$A$};", "the author asked for layout");
    }

    [Fact]
    public void The_diff_lists_removed_then_added_lines()
    {
        var diff = TikzAsk.Diff("a\nb\nc\nd", "a\nB\nc\nd\ne");
        diff.Select(d => $"{d.Op}{d.Text}").Should().Equal(" a", "-b", "+B", " c", " d", "+e");
        TikzAsk.ChangedLines("a\nb\nc\nd", "a\nB\nc\nd\ne").Should().Be(2);
    }

    // ── The loop: compile before proposing ────────────────────────────

    private sealed class FakeCompiler
    {
        private readonly Queue<TikzRenderResult> _results;
        public List<(string Source, string? Preamble)> Seen { get; } = new();
        public FakeCompiler(params TikzRenderResult[] results) => _results = new(results);

        public Task<TikzRenderResult> Compile(string source, string? preamble, CancellationToken ct)
        {
            Seen.Add((source, preamble));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private static TikzRenderResult Drew => new(Encoding.UTF8.GetBytes("<svg>ok</svg>"), null, false);
    private static TikzRenderResult Broke(int line, string message = "Undefined control sequence \\nodee.") =>
        new(null, new TikzRenderError("tex", message, line, @"\nodee (x) {};", null), false);

    private static string Fenced(string source, string prose = "Done.") => $"{prose}\n```tikz\n{source}\n```";

    private static (TikzAskContext Ctx, List<ChatMessage> Messages) Turn(string source, string message)
    {
        var (doc, figure, _) = Document();
        var ctx = TikzAsk.Context(doc, new TikzAskFigure(BlockId: figure.ToString(), Source: source), Colours)!;
        return (ctx, new List<ChatMessage> { new(ChatRole.System, TikzAsk.SystemPrompt(ctx, null)), new(ChatRole.User, message) });
    }

    private static Task<TikzAskRun> Run(ScriptedChatClient chat, FakeCompiler compiler, TikzAskContext ctx, List<ChatMessage> messages,
        string? intent = null, bool mayWrite = true, string message = "make f dashed") =>
        new TikzAskRunner(chat, compiler.Compile).RunAsync(messages, new ChatOptions(), ctx, intent, message, mayWrite, @"\usetikzlibrary{arrows.meta}", default);

    [Fact]
    public async Task A_drawing_that_draws_first_time_is_proposed_with_its_svg()
    {
        var (ctx, messages) = Turn("", "draw a commutative square");
        var square = "\\begin{tikzcd}\n  A \\arrow[r] \\arrow[d] & B \\arrow[d] \\\\\n  C \\arrow[r] & D\n\\end{tikzcd}";
        var chat = new ScriptedChatClient(Fenced(square, "Here's a square with tikz-cd.") + "\n```preamble\n\\usetikzlibrary{positioning}\n\\usetikzlibrary{arrows.meta}\n```");
        var compiler = new FakeCompiler(Drew);

        var run = await Run(chat, compiler, ctx, messages, intent: "draw");

        chat.Calls.Should().HaveCount(1);
        compiler.Seen.Should().ContainSingle().Which.Should().Be((square, @"\usetikzlibrary{positioning}"), "compiled with the line it adds, not the one the preamble has");
        run.Reply.Should().Be("Here's a square with tikz-cd.");
        run.Proposal.Kind.Should().Be("draw");
        run.Proposal.Svg.Should().Be("<svg>ok</svg>");
        run.Proposal.Attempts.Should().Be(1);
        run.Proposal.LineCount.Should().Be(4);
        run.Proposal.PreambleAdditions.Should().Equal(@"\usetikzlibrary{positioning}");
        run.Proposal.Packages.Should().Equal("tikz-cd");
        run.InputTokens.Should().Be(100);
    }

    [Fact]
    public async Task A_change_that_does_not_draw_goes_back_with_the_error_once_and_the_retry_that_draws_is_proposed()
    {
        var (ctx, messages) = Turn(Triangle, "make f dashed");
        var broken = Triangle.Replace("\\draw[->] (A) -- node[above]", "\\draw[->, dashed] (A) -- \\nodee[above]");
        var fixedOne = Triangle.Replace("\\draw[->] (A) -- node[above]", "\\draw[->, dashed] (A) -- node[above]");
        var chat = new ScriptedChatClient(Fenced(broken), Fenced(fixedOne, "Made f dashed."));
        var compiler = new FakeCompiler(Broke(5), Drew);

        var run = await Run(chat, compiler, ctx, messages);

        chat.Calls.Should().HaveCount(2);
        var retry = chat.Calls[1].Last();
        retry.Role.Should().Be(ChatRole.User);
        retry.Text.Should().Contain("That did not draw").And.Contain("line 5").And.Contain("Undefined control sequence");
        compiler.Seen.Select(s => s.Source).Should().Equal(broken, fixedOne);
        run.Proposal.Kind.Should().Be("change");
        run.Proposal.Attempts.Should().Be(2);
        run.Proposal.Source.Should().Be(fixedOne);
        run.Proposal.ChangedLines.Should().Be(1);
        run.Proposal.Svg.Should().NotBeNull();
        run.Reply.Should().Be("Made f dashed.");
        run.InputTokens.Should().Be(200, "both calls are metered");
    }

    [Fact]
    public async Task Two_failures_say_it_could_not_draw_with_the_line_and_show_no_drawing()
    {
        var (ctx, messages) = Turn("", "draw a pipeline");
        var chat = new ScriptedChatClient(Fenced("\\begin{tikzpicture}\n\\nodee {a};\n\\end{tikzpicture}"),
            Fenced("\\begin{tikzpicture}\n\\node {a};\n\\node {b}\n\\draw (a) -- (b);\n\\node {c};\n\\nodee {d};\n\\end{tikzpicture}"));
        var compiler = new FakeCompiler(Broke(2), Broke(6));

        var run = await Run(chat, compiler, ctx, messages, intent: "draw");

        chat.Calls.Should().HaveCount(2, "one retry, not a loop");
        run.Reply.Should().Be("I couldn't get this to draw. Here's the source; the error is on line 6.");
        run.Proposal.Kind.Should().Be("failed");
        run.Proposal.Svg.Should().BeNull();
        run.Proposal.Source.Should().Contain("\\nodee {d};", "the last attempt is the source handed back");
        run.Proposal.Error!.Line.Should().Be(6);
        run.Proposal.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task A_timeout_is_not_the_models_to_fix_so_there_is_no_retry()
    {
        var (ctx, messages) = Turn("", "draw a pipeline");
        var chat = new ScriptedChatClient(Fenced("\\begin{tikzpicture}\\node {a};\\end{tikzpicture}"));
        var compiler = new FakeCompiler(new TikzRenderResult(null, new TikzRenderError("timeout", "It took too long.", null, null, null), false));

        var run = await Run(chat, compiler, ctx, messages, intent: "draw");

        chat.Calls.Should().HaveCount(1);
        run.Proposal.Kind.Should().Be("failed");
        run.Reply.Should().StartWith("I couldn't get this to draw. Here's the source.").And.Contain("It took too long.");
    }

    [Fact]
    public async Task Explain_never_edits_even_when_the_model_sends_source()
    {
        var (ctx, messages) = Turn(Triangle, "Explain this figure");
        var chat = new ScriptedChatClient("A commutative triangle.\n[L2-4] Three nodes.\n[L7] The composite.\n```tikz\n\\begin{tikzpicture}\\end{tikzpicture}\n```");
        var compiler = new FakeCompiler();

        var run = await Run(chat, compiler, ctx, messages, intent: "explain");

        compiler.Seen.Should().BeEmpty("nothing is compiled: nothing is proposed");
        run.Proposal.Kind.Should().Be("explain");
        run.Proposal.Source.Should().BeNull();
        run.Proposal.Lines.Should().Equal(new TikzAskLine(2, 4, "Three nodes."), new TikzAskLine(7, 7, "The composite."));
        run.Reply.Should().Be("A commutative triangle.");
    }

    [Fact]
    public async Task A_reader_gets_answers_not_changes()
    {
        var (ctx, messages) = Turn(Triangle, "make it blue");
        var chat = new ScriptedChatClient(Fenced(Triangle.Replace("lilia-accent", "blue"), "Here."));
        var compiler = new FakeCompiler();

        var run = await Run(chat, compiler, ctx, messages, mayWrite: false);

        compiler.Seen.Should().BeEmpty();
        run.Proposal.Kind.Should().Be("answer");
        run.Proposal.Source.Should().BeNull();
    }

    [Fact]
    public async Task A_caption_is_proposed_as_a_caption()
    {
        var (ctx, messages) = Turn(Triangle, "Write a caption");
        var chat = new ScriptedChatClient("```caption\nThe composite $g\\circ f$ as a commutative triangle.\n```");

        var run = await Run(chat, new FakeCompiler(), ctx, messages, intent: "caption");

        run.Proposal.Kind.Should().Be("caption");
        run.Proposal.Caption.Should().Be("The composite $g\\circ f$ as a commutative triangle.");
    }

    [Fact]
    public async Task What_is_compiled_keeps_the_lines_the_model_only_reindented()
    {
        var (ctx, messages) = Turn(Triangle, "make f dashed");
        var proposed = Triangle
            .Replace("  \\node (C)", "\t\\node (C)")
            .Replace("\\draw[->] (A) -- node[above]", "\\draw[->, dashed] (A) -- node[above]");
        var compiler = new FakeCompiler(Drew);

        var run = await Run(new ScriptedChatClient(Fenced(proposed)), compiler, ctx, messages);

        compiler.Seen.Single().Source.Should().Contain("\n  \\node (C)").And.Contain("[->, dashed] (A)");
        run.Proposal.ChangedLines.Should().Be(1);
    }

    [Fact]
    public void A_viewer_may_only_ask_for_an_explanation()
    {
        TikzAsk.Proposes("explain").Should().BeFalse();
        foreach (var i in new[] { "draw", "change", "fix", "caption" }) TikzAsk.Proposes(i).Should().BeTrue();
    }
}
