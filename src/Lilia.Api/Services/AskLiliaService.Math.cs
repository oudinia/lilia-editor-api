using System.Diagnostics;
using System.Text.Json;
using Lilia.Api.Models.AiArchitect;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>
/// Ask Lilia reading maths (18 Oct; the user: "math should mostly also be about taking a picture and
/// uploading it directly"): the same dock, the same <c>POST /api/ai/ask</c>, the same gate, model,
/// audit and metering, with <see cref="AskLiliaRequest.Math"/> and the photo as an image attachment.
///
/// <para>It writes nothing. Each formula it reads is checked by TeX with the document's own preamble
/// (<see cref="MathAskRunner"/>) and comes back as <see cref="AskLiliaResponse.Math"/>; the editor
/// inserts it, or saves it to the author's library, when the author chooses to. Without a document
/// (the library page), the check uses Lilia's standard preamble.</para>
/// </summary>
public sealed partial class AskLiliaService
{
    private async Task<AskLiliaResult> AskMathAsync(string userId, AskLiliaRequest request, MathAskInput input, CancellationToken ct)
    {
        if (_latexRender is null) return AskLiliaResult.Lock("unavailable", "Maths can't be read here right now.");
        var images = (request.Attachments ?? Array.Empty<AskAttachment>())
            .Where(a => a is not null && (a.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (images.Count == 0 && string.IsNullOrWhiteSpace(input.Text))
            return AskLiliaResult.Lock("nothing-to-read", "Send a photo or paste the maths to read.");

        Document? entity = null;
        string? paragraphBefore = null;
        if (!string.IsNullOrWhiteSpace(request.DocumentId))
        {
            if (!Guid.TryParse(request.DocumentId, out var docId))
                return AskLiliaResult.Lock("no-access", "You can't open this document.");
            var document = await _documentService.GetDocumentAsync(docId, userId);
            if (document is null) return AskLiliaResult.Lock("no-access", "You can't open this document.");
            entity = await _context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId, ct);
            if (entity is null) return AskLiliaResult.Lock("no-access", "You can't open this document.");
            paragraphBefore = ParagraphBefore(document, input.AfterBlockId);
        }

        var level = ProficiencyGuidance.Parse(request.Proficiency ?? await AccountAskLevelAsync(userId, ct));
        var system = MathAsk.SystemPrompt(MathAsk.Shortcuts(entity?.CustomPreamble), paragraphBefore, ProficiencyGuidance.For(level))
            + "\n" + LanguageNote;
        var userParts = new List<AIContent> { new TextContent(MathAsk.UserMessage(request.Message, input.Text)) };
        userParts.AddRange(BuildAttachmentContents(images));
        var messages = new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, userParts) };

        var model = _mathModel is { Length: > 0 } m ? m : await ResolveModelAsync(userId, request.Model, ct);
        var aiRequestId = await PersistPendingAsync(userId, PurposeFor(MathAsk.SkillId), model, messages, entity?.Id, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("[AskLilia] maths images={Images} pasted={Pasted} user={UserId} model={Model}",
                images.Count, input.Text?.Length ?? 0, userId, model);
            var runner = new MathAskRunner(_chatClient, (formulas, c) => CheckFormulasAsync(entity, formulas, c));
            var options = new ChatOptions { ModelId = model, MaxOutputTokens = 2048 };
            var run = await runner.RunAsync(messages, options, ct);

            await MarkAsync(aiRequestId, "success", null, run.InputTokens, run.OutputTokens, (int)sw.ElapsedMilliseconds, ct);
            var costUsd = AiArchitectPricing.ComputeCostUsd(model, run.InputTokens, run.OutputTokens);
            var (credits, balance, creditsUsed) = await MeterAsync(userId, model, run.InputTokens, run.OutputTokens, aiRequestId, [], ct);
            _logger.LogInformation("[AskLilia] maths read formulas={Count} failed={Failed} attempts={Attempts} in={In} out={Out} ms={Ms}",
                run.Proposal.Formulas.Count, run.Proposal.Formulas.Count(f => !f.Ok), run.Proposal.Attempts,
                run.InputTokens, run.OutputTokens, sw.ElapsedMilliseconds);

            return AskLiliaResult.Ok(new AskLiliaResponse(
                MathAsk.SkillId, _router.Get(MathAsk.SkillId)?.Name ?? "Equation", run.Reply,
                new AiArchitectUsage(run.InputTokens, run.OutputTokens, costUsd, credits), balance, creditsUsed,
                Model: model, Math: run.Proposal));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AskLilia] maths turn failed for user {UserId}", userId);
            await MarkAsync(aiRequestId, "error", Truncate(ex.Message, 500), 0, 0, (int)sw.ElapsedMilliseconds, ct);
            throw;
        }
    }

    /// <summary>The text of the paragraph the formula would follow, for its notation.</summary>
    private static string? ParagraphBefore(Lilia.Core.DTOs.DocumentDto document, string? afterBlockId)
    {
        if (!Guid.TryParse(afterBlockId, out var id)) return null;
        var block = document.Blocks?.FirstOrDefault(b => b.Id == id);
        if (block is null || block.Type is not ("paragraph" or "equation")) return null;
        return block.Content.ValueKind == JsonValueKind.Object && block.Content.TryGetProperty("text", out var t)
            && t.ValueKind == JsonValueKind.String ? t.GetString()
            : block.Type == "equation" ? Lilia.Core.Blocks.EquationContent.ReadSource(block.Content) : null;
    }

    /// <summary>
    /// TeX on the formulas, in the document's preamble (or Lilia's standard one): all together first,
    /// the usual case being that they all print; then one by one, to say which did not.
    /// </summary>
    private async Task<IReadOnlyList<string?>> CheckFormulasAsync(Document? doc, IReadOnlyList<MathAskReplyFormula> formulas, CancellationToken ct)
    {
        var engine = doc is null ? LatexEngine.Pdflatex : (doc.LatexEngine ?? "pdflatex").ParseEngine();
        string Wrap(string body) => doc is null
            ? LaTeXPreamble.WrapForValidation(body, engine).Replace("\\begin{document}", MathMacros.PreambleShortcuts + "\n\\begin{document}")
            : RenderService.DedupeDocumentClass(RenderService.BuildPreambleForValidation(doc, engine) + "\n\\begin{document}\n" + body + "\n\\end{document}");

        async Task<string?> CheckAsync(IEnumerable<MathAskReplyFormula> some)
        {
            var source = Wrap(string.Join("\n\n", some.Select(Body)));
            try
            {
                var r = await _latexRender!.ValidateAsync(source, engine.ToCli());
                return r.Valid ? null : FirstLine(r.ParsedError?.ErrorRaw ?? r.Error ?? "TeX could not print it.");
            }
            catch (UnsafeLatexException) { return "It uses a command Lilia doesn't allow in documents."; }
        }

        var all = await CheckAsync(formulas);
        if (all is null) return formulas.Select(_ => (string?)null).ToList();
        if (formulas.Count == 1) return new[] { all };
        var each = new List<string?>();
        foreach (var f in formulas) each.Add(await CheckAsync(new[] { f }));
        return each;
    }

    /// <summary>A formula as the PDF prints it: an unnumbered display, or inside a sentence.</summary>
    internal static string Body(MathAskReplyFormula f) => f.Display
        ? EquationLatex.Render(JsonSerializer.SerializeToElement(new { latex = f.Latex, equationMode = "display*" }))
        : $"Text with ${f.Latex}$ inline.";

    private static string FirstLine(string s)
    {
        var line = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? s;
        return line.Length <= 300 ? line : line[..300] + "…";
    }
}
