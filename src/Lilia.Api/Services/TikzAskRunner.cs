using System.Diagnostics;
using System.Text;
using Lilia.Core.Blocks;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>What one figure turn produced: the reply, the proposal, and the tokens it cost.</summary>
public sealed record TikzAskRun(string Reply, TikzAskProposal Proposal, int InputTokens, int OutputTokens, int ModelCalls);

/// <summary>
/// One Ask Lilia turn on a figure (TikZ step 3, 1c): the model, then the compiler.
///
/// <para><b>Only drawings that drew are shown.</b> Source the model writes is compiled before the
/// author sees it (<see cref="ITikzFigureService.ProposalAsync"/>: the draft path's safety, cache and
/// budget). If it does not draw, TeX's error goes back to the model once; if the second source does
/// not draw either, the turn says so (<see cref="TikzAsk.CouldNotDraw"/>) and hands back the source
/// for the split view, never a drawing. A compile that timed out or was refused by the budget is not
/// the model's to fix: no retry, the same "couldn't draw" answer with what happened.</para>
///
/// <para>Explain and caption never edit: any source in those replies is dropped. So is any source
/// for someone who may only read.</para>
/// </summary>
public sealed class TikzAskRunner
{
    public const int MaxAttempts = 2;

    public delegate Task<TikzRenderResult> Compile(string source, string? preambleAdditions, CancellationToken ct);

    private readonly IChatClient _chat;
    private readonly Compile _compile;

    public TikzAskRunner(IChatClient chat, Compile compile)
    {
        _chat = chat;
        _compile = compile;
    }

    /// <param name="messages">System prompt, history, and the author's message, in order. The retry is appended to it.</param>
    /// <param name="mayWrite">False for a reader: answers and explanations only.</param>
    public async Task<TikzAskRun> RunAsync(
        List<ChatMessage> messages, ChatOptions options, TikzAskContext ctx, string? intent, string message,
        bool mayWrite, string? preamble, CancellationToken ct)
    {
        var lineCount = TikzAsk.SplitLines(ctx.Source).Length;
        int input = 0, output = 0, calls = 0;

        async Task<TikzAskReply> AskModelAsync()
        {
            var response = await _chat.GetResponseAsync(messages, options, ct);
            calls++;
            input += (int?)response.Usage?.InputTokenCount ?? 0;
            output += (int?)response.Usage?.OutputTokenCount ?? 0;
            messages.AddRange(response.Messages);
            return TikzAsk.Parse(response.Text, lineCount);
        }

        var reply = await AskModelAsync();
        var readOnly = intent is "explain" or "caption" || !mayWrite;

        if (reply.Source is null || readOnly)
        {
            TikzAskProposal answer;
            if (intent != "explain" && mayWrite && reply.Caption is { Length: > 0 } caption)
                answer = new TikzAskProposal("caption", Caption: caption);
            else if (reply.Lines.Count > 0)
                answer = new TikzAskProposal("explain", Lines: reply.Lines);
            else
                answer = new TikzAskProposal("answer");
            var text = reply.Prose.Length > 0 ? reply.Prose
                : answer.Kind == "caption" ? "Here's a caption for it." : "I don't have an answer for that figure.";
            return new TikzAskRun(text, answer, input, output, calls);
        }

        var kind = string.IsNullOrWhiteSpace(ctx.Source) ? "draw" : "change";
        TikzAskReply current = reply;
        string? source = null;
        IReadOnlyList<string> additions = Array.Empty<string>();
        TikzRenderResult? drawn = null;
        long drawMs = 0;
        var attempt = 0;
        while (attempt < MaxAttempts)
        {
            attempt++;
            source = kind == "change"
                ? TikzAsk.KeepUntouchedLines(ctx.Source, current.Source!, message)
                : current.Source!;
            additions = TikzAsk.PreambleAdditions(current.PreambleLines, preamble);
            var clock = Stopwatch.StartNew();
            drawn = await _compile(source, additions.Count > 0 ? string.Join("\n", additions) : null, ct);
            drawMs = clock.ElapsedMilliseconds;
            if (drawn.Ok || drawn.Error is not { Kind: "tex" } || attempt == MaxAttempts) break;

            messages.Add(new ChatMessage(ChatRole.User, TikzAsk.RetryMessage(drawn.Error)));
            var next = await AskModelAsync();
            if (next.Source is null) break;   // it gave up: the first attempt is what there is
            current = next;
        }

        var packages = TikzFigure.RequiredPackages(source).Where(p => p != "tikz").ToList();
        var lines = TikzAsk.SplitLines(source!).Length;
        if (drawn is { Ok: true })
        {
            var proposal = new TikzAskProposal(
                kind, source, Encoding.UTF8.GetString(drawn.Svg!), (int)drawMs, attempt, lines,
                kind == "change" ? TikzAsk.ChangedLines(ctx.Source, source!) : null,
                additions, packages);
            var prose = reply.Prose.Length > 0 && attempt == 1 ? reply.Prose
                : current.Prose.Length > 0 ? current.Prose
                : kind == "draw" ? "Here it is. It drew." : "Here's the change. It drew.";
            return new TikzAskRun(prose, proposal, input, output, calls);
        }

        var e = drawn!.Error!;
        var failed = new TikzAskProposal(
            "failed", source, null, null, attempt, lines,
            kind == "change" ? TikzAsk.ChangedLines(ctx.Source, source!) : null,
            additions, packages,
            new TikzAskProposalError(e.Kind, e.Message, e.Line, e.Excerpt));
        var said = e.Kind == "tex"
            ? TikzAsk.CouldNotDraw(e.Line)
            : TikzAsk.CouldNotDraw(null) + " " + e.Message;
        return new TikzAskRun(said, failed, input, output, calls);
    }
}
