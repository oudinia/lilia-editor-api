namespace Lilia.Api.Services.Semantic;

/// <summary>
/// Ask Lilia's router with meaning as well as words (19 Oct). The keyword router stays as it is; the
/// example classifier (<see cref="SkillExamples"/>, on <see cref="E5Embedder"/>) decides when the
/// keywords found nothing, and overrides a weak keyword match when it is clearly sure. Without the
/// model, this is the keyword router exactly. How it decides was chosen by measuring
/// (tests: RoutingMeasureTests).
/// </summary>
public sealed class SemanticAskRouter : IAskLiliaRouter
{
    /// <summary>Below this the classifier is not believed (the keyword answer, or the architect, stands).</summary>
    public const float MinScore = 0.15f;
    /// <summary>How clearly it must win to override a keyword match of one word.</summary>
    public const float OverrideMargin = 0.05f;

    private readonly AskLiliaRouter _keywords;
    private readonly ExampleClassifier? _classifier;

    public SemanticAskRouter(AskLiliaRouter keywords, ITextEmbedder embedder)
    {
        _keywords = keywords;
        _classifier = new ExampleClassifier(embedder, SkillExamples.All);
    }

    public IReadOnlyList<AiSkill> Skills => _keywords.Skills;
    public AiSkill Get(string id) => _keywords.Get(id);
    public string BuildPreamble(AiSkill skill, Proficiency level) => _keywords.BuildPreamble(skill, level);

    public AskRoute Route(string message)
    {
        var byWords = _keywords.Route(message);
        if (_classifier is not { Ready: true }) return byWords;
        var byMeaning = _classifier.Classify(message);
        return Decide(byWords, byMeaning, _keywords);
    }

    /// <summary>The rule, apart so it can be measured: keywords when they are clear, meaning otherwise.</summary>
    public static AskRoute Decide(AskRoute byWords, Classified? byMeaning, IAskLiliaRouter skills)
    {
        if (byMeaning is null || byMeaning.Score < MinScore) return byWords;
        var weakWords = byWords.Confidence <= 0.65;          // no match (0.2) or one single-word match (0.65)
        var noWords = byWords.Confidence < 0.5;
        if (byMeaning.Label == byWords.SkillId) return byWords;
        if (noWords || (weakWords && byMeaning.Margin >= OverrideMargin))
        {
            var skill = skills.Get(byMeaning.Label);
            return new AskRoute(skill.Id, skill.Name, Math.Round(byMeaning.Score, 2),
                $"meaning: closest to {skill.Name} examples ({byMeaning.Score:0.00}, next {byMeaning.Second} {byMeaning.SecondScore:0.00})");
        }
        return byWords;
    }
}
