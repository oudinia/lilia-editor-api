using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>Ask Lilia reading maths (18 Oct): how a reply is read, and the retry when TeX can't print it.</summary>
public class MathAskTests
{
    [Fact]
    public void A_reply_gives_each_formula_its_own_name_and_display()
    {
        var reply = MathAsk.Parse("""
            Name: Quadratic formula
            Display: equation
            ```latex
            x = \frac{-b \pm \sqrt{b^2-4ac}}{2a}
            ```
            Name: -
            Display: inline
            ```latex
            $a^2+b^2$
            ```
            The second symbol could be a 2 or a z.
            """);

        reply.Formulas.Should().HaveCount(2);
        reply.Formulas[0].Should().Be(new MathAskReplyFormula(@"x = \frac{-b \pm \sqrt{b^2-4ac}}{2a}", "Quadratic formula", true));
        reply.Formulas[1].Should().Be(new MathAskReplyFormula("a^2+b^2", null, false), "the $ it was told not to write is taken off");
        reply.Prose.Should().Be("The second symbol could be a 2 or a z.");
    }

    [Theory]
    [InlineData(@"\[ E = mc^2 \]", "E = mc^2")]
    [InlineData("$$E=mc^2$$", "E=mc^2")]
    [InlineData(@"\begin{equation} E=mc^2 \label{eq:e} \end{equation}", "E=mc^2")]
    [InlineData("$a$ and $b$", "$a$ and $b$")]
    [InlineData(@"\begin{aligned} a &= b \\ c &= d \end{aligned}", @"\begin{aligned} a &= b \\ c &= d \end{aligned}")]
    public void Delimiters_are_taken_off_and_environments_kept(string written, string kept) =>
        MathAsk.Unwrap(written).Should().Be(kept);

    [Fact]
    public void No_maths_is_no_formula_and_the_model_says_so()
    {
        var reply = MathAsk.Parse("There is no mathematics in this image: it shows a cat.");
        reply.Formulas.Should().BeEmpty();
        reply.Prose.Should().Contain("no mathematics");
    }

    private static List<ChatMessage> Messages() => new() { new(ChatRole.System, "read"), new(ChatRole.User, "Read the maths.") };

    [Fact]
    public async Task A_formula_TeX_cannot_print_goes_back_once_with_the_error()
    {
        var chat = new ScriptedChatClient(
            "Name: -\n```latex\n\\nosuch{x}\n```",
            "Name: -\n```latex\n\\sqrt{x}\n```");
        var checks = new List<string>();
        var run = await new MathAskRunner(chat, (fs, _) =>
        {
            checks.AddRange(fs.Select(f => f.Latex));
            return Task.FromResult<IReadOnlyList<string?>>(fs.Select(f => f.Latex.Contains("nosuch") ? "Undefined control sequence." : null).ToList());
        }).RunAsync(Messages(), new ChatOptions(), default);

        run.ModelCalls.Should().Be(2);
        chat.Calls[1].Last().Text.Should().Contain("formula 1: Undefined control sequence.");
        run.Proposal.Attempts.Should().Be(2);
        run.Proposal.Formulas.Should().ContainSingle().Which.Should().Be(new MathAskFormula(@"\sqrt{x}", null, true, true));
        run.Reply.Should().Be("Here it is. It prints.");
        run.InputTokens.Should().Be(200);
    }

    [Fact]
    public async Task Twice_unprintable_comes_back_marked_never_as_fine()
    {
        var chat = new ScriptedChatClient("```latex\n\\nosuch\n```", "```latex\n\\nosuch{}\n```");
        var run = await new MathAskRunner(chat, (fs, _) =>
            Task.FromResult<IReadOnlyList<string?>>(fs.Select(_ => (string?)"Undefined control sequence.").ToList()))
            .RunAsync(Messages(), new ChatOptions(), default);

        run.ModelCalls.Should().Be(2, "one retry, no more");
        var f = run.Proposal.Formulas.Should().ContainSingle().Subject;
        f.Ok.Should().BeFalse();
        f.Error.Should().Be("Undefined control sequence.");
        run.Reply.Should().Contain("doesn't print yet");
    }

    [Fact]
    public async Task Formulas_that_print_are_not_sent_back()
    {
        var chat = new ScriptedChatClient("Name: Euler\n```latex\ne^{i\\pi}+1=0\n```");
        var run = await new MathAskRunner(chat, (fs, _) => Task.FromResult<IReadOnlyList<string?>>(fs.Select(_ => (string?)null).ToList()))
            .RunAsync(Messages(), new ChatOptions(), default);
        run.ModelCalls.Should().Be(1);
        run.Proposal.Formulas.Single().Name.Should().Be("Euler");
    }
}

public class MathAskShortcutTests
{
    [Fact]
    public void The_documents_own_shortcuts_come_first_and_the_editor_only_ones_not_at_all()
    {
        var list = MathAsk.Shortcuts(@"\newcommand{\Prob}{\mathbb{P}}");
        list[0].Should().Be(new KeyValuePair<string, string>(@"\Prob", @"\mathbb{P}"));
        list.Select(kv => kv.Key).Should().Contain(@"\abs");
        list.Select(kv => kv.Key).Should().NotContain(Lilia.Engines.MathMacros.EditorStandIns.Keys);
    }
}
