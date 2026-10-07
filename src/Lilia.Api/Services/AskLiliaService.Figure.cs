using System.Diagnostics;
using Lilia.Api.Models.AiArchitect;
using Lilia.Engines.Themes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>
/// Ask Lilia on a figure (TikZ step 3, 1c): the same dock, the same <c>POST /api/ai/ask</c>, the
/// same gate, model, audit and metering, scoped to one TikZ figure by <see cref="AskLiliaRequest.Figure"/>.
///
/// <para>It reads the figure's source, caption and label, the preamble's TikZ lines, the theme's
/// colour names and the paragraphs around it (<see cref="TikzAsk.Context"/>), not the document. It
/// writes nothing: what it proposes comes back as <see cref="AskLiliaResponse.Figure"/>, compiled
/// first (<see cref="TikzAskRunner"/>), and the editor applies it when the author keeps it, through
/// the block's own save.</para>
///
/// <para>Who may ask what: anyone who can open the document may have a figure explained; drawing,
/// changing, fixing and captioning are for its owner and editors.</para>
/// </summary>
public sealed partial class AskLiliaService
{
    private const int MaxFigureHistoryTurns = 6;

    internal const string ReadOnlyFigureNote =
        "\nTHE AUTHOR MAY ONLY READ THIS DOCUMENT: explain or answer; never write source or a caption.";

    private async Task<AskLiliaResult> AskFigureAsync(string userId, AskLiliaRequest request, TikzAskFigure figure, CancellationToken ct)
    {
        if (_tikz is null) return AskLiliaResult.Lock("unavailable", "Figures can't be drawn here right now.");
        if (!Guid.TryParse(request.DocumentId, out var docId))
            return AskLiliaResult.Lock("no-document", "Ask Lilia on a figure needs the figure's document.");
        var intent = figure.Intent is { } i && TikzAsk.Intents.Contains(i) ? i : null;

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document is null) return AskLiliaResult.Lock("no-access", "You can't open this document.");
        var mayWrite = document.Role is "owner" or "editor";
        if (!mayWrite && TikzAsk.Proposes(intent ?? "explain"))
            return AskLiliaResult.Lock("read-only", "You can read this document but not change its figures. Explain this figure still works.");

        var entity = await _context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (entity is null) return AskLiliaResult.Lock("no-access", "You can't open this document.");
        var themes = await TikzFigureThemes.LoadAsync(_context, entity, ct);
        var placeId = Guid.TryParse(figure.BlockId, out var b) ? b : Guid.TryParse(figure.AfterBlockId, out var a) ? a : Guid.Empty;
        var theme = themes.For(placeId);

        var ctx = TikzAsk.Context(document, figure, FigureColours.Resolve(theme));
        if (ctx is null) return AskLiliaResult.Lock("not-a-figure", "That block is not a TikZ figure of this document.");

        var level = ProficiencyGuidance.Parse(request.Proficiency);
        var system = TikzAsk.SystemPrompt(ctx, intent, ProficiencyGuidance.For(level)) + (mayWrite ? "" : ReadOnlyFigureNote);
        var messages = new List<ChatMessage> { new(ChatRole.System, system) };
        // The thread so far (a follow-up like "now make it blue" needs the last turn), briefly.
        if (request.History is { Count: > 0 })
            foreach (var turn in request.History.Where(t => !string.IsNullOrWhiteSpace(t.Content)).TakeLast(MaxFigureHistoryTurns))
                messages.Add(new ChatMessage(
                    string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase) ? ChatRole.User : ChatRole.Assistant,
                    turn.Content));
        messages.Add(new ChatMessage(ChatRole.User, request.Message));

        var model = await ResolveModelAsync(userId, request.Model, ct);
        var aiRequestId = await PersistPendingAsync(userId, PurposeFor(TikzAsk.SkillId), model, messages, docId, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[AskLilia] figure intent={Intent} block={Block} user={UserId} model={Model}",
                intent ?? "(typed)", figure.BlockId ?? "(new)", userId, model);
            var runner = new TikzAskRunner(_chatClient,
                (source, additions, c) => _tikz.ProposalAsync(entity, source, userId, theme, additions, c));
            var options = new ChatOptions { ModelId = model, MaxOutputTokens = MaxOutputTokens };
            var run = await runner.RunAsync(messages, options, ctx, intent, request.Message, mayWrite, entity.CustomPreamble, ct);

            await MarkAsync(aiRequestId, "success", null, run.InputTokens, run.OutputTokens, (int)sw.ElapsedMilliseconds, ct);
            var costUsd = AiArchitectPricing.ComputeCostUsd(model, run.InputTokens, run.OutputTokens);
            var (credits, balance, creditsUsed) = await MeterAsync(userId, model, run.InputTokens, run.OutputTokens, aiRequestId, 0, "", ct);
            _logger.LogInformation("[AskLilia] figure answered kind={Kind} attempts={Attempts} calls={Calls}",
                run.Proposal.Kind, run.Proposal.Attempts, run.ModelCalls);

            return AskLiliaResult.Ok(new AskLiliaResponse(
                TikzAsk.SkillId, _router.Get(TikzAsk.SkillId)?.Name ?? "Figure", run.Reply,
                new AiArchitectUsage(run.InputTokens, run.OutputTokens, costUsd, credits), balance, creditsUsed,
                Model: model, Figure: run.Proposal));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AskLilia] figure turn failed for user {UserId}", userId);
            await MarkAsync(aiRequestId, "error", Truncate(ex.Message, 500), 0, 0, (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
    }
}
