using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>What one maths turn produced: the reply, the proposal, and the tokens it cost.</summary>
public sealed record MathAskRun(string Reply, MathAskProposal Proposal, int InputTokens, int OutputTokens, int ModelCalls);

/// <summary>
/// One Ask Lilia turn reading maths (18 Oct): the model, then TeX.
///
/// <para><b>Every formula is checked before the author sees it</b>, by TeX with the document's own
/// preamble (its packages and shortcuts), as the PDF will print it. If any does not print, TeX's
/// errors go back to the model once; what the second reply gives is checked again, and a formula
/// that still fails comes back marked, with its error, never as if it were fine.</para>
/// </summary>
public sealed class MathAskRunner
{
    public const int MaxAttempts = 2;

    /// <summary>Checks formulas together; returns null for each that printed, else its error.</summary>
    public delegate Task<IReadOnlyList<string?>> Check(IReadOnlyList<MathAskReplyFormula> formulas, CancellationToken ct);

    private readonly IChatClient _chat;
    private readonly Check _check;

    public MathAskRunner(IChatClient chat, Check check)
    {
        _chat = chat;
        _check = check;
    }

    /// <param name="messages">System prompt and the author's message (with the image), in order. The retry is appended to it.</param>
    public async Task<MathAskRun> RunAsync(List<ChatMessage> messages, ChatOptions options, CancellationToken ct)
    {
        int input = 0, output = 0, calls = 0;

        async Task<MathAskReply> AskModelAsync()
        {
            var response = await _chat.GetResponseAsync(messages, options, ct);
            calls++;
            input += (int?)response.Usage?.InputTokenCount ?? 0;
            output += (int?)response.Usage?.OutputTokenCount ?? 0;
            messages.AddRange(response.Messages);
            return MathAsk.Parse(response.Text);
        }

        var first = await AskModelAsync();
        if (first.Formulas.Count == 0)
        {
            var none = first.Prose.Length > 0 ? first.Prose : "I couldn't find any maths there.";
            return new MathAskRun(none, new MathAskProposal(Array.Empty<MathAskFormula>(), 1), input, output, calls);
        }

        var current = first;
        var errors = await _check(current.Formulas, ct);
        var attempt = 1;
        while (attempt < MaxAttempts && errors.Any(e => e is not null))
        {
            attempt++;
            messages.Add(new ChatMessage(ChatRole.User, MathAsk.RetryMessage(
                errors.Select((e, i) => (i, e)).Where(x => x.e is not null).Select(x => (x.i, x.e!)))));
            var next = await AskModelAsync();
            if (next.Formulas.Count == 0) break;   // it gave up: the first reading is what there is
            current = next;
            errors = await _check(current.Formulas, ct);
        }

        var formulas = current.Formulas
            .Select((f, i) => new MathAskFormula(f.Latex, f.Name, f.Display, errors[i] is null, errors[i]))
            .ToList();
        var failed = formulas.Count(f => !f.Ok);
        var prose = current.Prose.Length > 0 ? current.Prose : first.Prose;
        if (prose.Length == 0)
            prose = failed == 0
                ? (formulas.Count == 1 ? "Here it is. It prints." : $"Here are the {formulas.Count} formulas. They print.")
                : failed == formulas.Count && formulas.Count == 1
                    ? "Here's my reading, but it doesn't print yet: the error is below."
                    : $"Here's my reading. {failed} of {formulas.Count} don't print yet: their errors are below.";
        return new MathAskRun(prose, new MathAskProposal(formulas, attempt), input, output, calls);
    }
}
