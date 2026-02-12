using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Import.Detection;

/// <summary>
/// Orchestrator that evaluates detection rules in priority order against a ParagraphAnalysis.
/// Manages the section tracker state and delegates element creation to matched rules.
/// </summary>
public class ElementDetectionPipeline
{
    private readonly List<ElementDetectionRule> _rules;
    private readonly SectionTracker _tracker;
    private readonly List<ParagraphTraceEntry> _traces = [];

    /// <summary>
    /// The section tracker managed by this pipeline.
    /// </summary>
    public SectionTracker Tracker => _tracker;

    /// <summary>
    /// All trace entries collected during evaluation.
    /// Each entry records what happened to a body element.
    /// </summary>
    public IReadOnlyList<ParagraphTraceEntry> Traces => _traces;

    /// <summary>
    /// Create a new detection pipeline.
    /// </summary>
    /// <param name="defaultRules">The default detection rules.</param>
    /// <param name="customRules">Optional custom rules to merge in (added alongside defaults).</param>
    /// <param name="disabledRuleIds">Optional set of rule IDs to disable.</param>
    /// <param name="tracker">Optional section tracker (creates a new one if null).</param>
    public ElementDetectionPipeline(
        IEnumerable<ElementDetectionRule> defaultRules,
        IEnumerable<ElementDetectionRule>? customRules = null,
        HashSet<string>? disabledRuleIds = null,
        SectionTracker? tracker = null)
    {
        _tracker = tracker ?? new SectionTracker();

        // Combine default and custom rules
        var allRules = new List<ElementDetectionRule>(defaultRules);
        if (customRules != null)
        {
            allRules.AddRange(customRules);
        }

        // Disable rules by ID
        if (disabledRuleIds != null)
        {
            foreach (var rule in allRules)
            {
                if (disabledRuleIds.Contains(rule.Id))
                {
                    rule.Enabled = false;
                }
            }
        }

        // Filter enabled and sort by priority (lower first)
        _rules = allRules
            .Where(r => r.Enabled)
            .OrderBy(r => r.Priority)
            .ToList();
    }

    /// <summary>
    /// Evaluate all rules against the given paragraph analysis.
    /// Returns the elements produced by the first matching rule, or null if no rule matches.
    /// Updates the section tracker state as a side effect.
    /// Also records a trace entry for diagnostics.
    /// </summary>
    /// <param name="analysis">The pre-computed paragraph analysis.</param>
    /// <param name="parser">The DocxParser instance (for element creation delegates).</param>
    /// <returns>The import elements produced, or null if the paragraph should be skipped.</returns>
    public IEnumerable<ImportElement>? Evaluate(ParagraphAnalysis analysis, DocxParser parser)
    {
        // Inject section tracker state into the analysis
        analysis.CurrentSection = _tracker.CurrentSection;
        analysis.InAbstractSection = _tracker.InAbstractSection;

        // Build trace entry from analysis
        var trace = new ParagraphTraceEntry
        {
            BodyIndex = _traces.Count,
            ElementType = "Paragraph",
            RawText = analysis.Text.Length > 500 ? analysis.Text[..500] : analysis.Text,
            FullText = analysis.Text,
            StyleId = analysis.StyleId,
            FontFamily = analysis.FontFamily,
            FontSizePoints = analysis.FontSizePoints,
            AllBold = analysis.AllBold,
            AllItalic = analysis.AllItalic,
            HasNumbering = analysis.HasNumberingProperties,
            HasMathElements = analysis.HasMathElements,
            HasDrawings = analysis.HasDrawings,
            HasPageBreaks = analysis.HasPageBreaks,
            ShadingFill = analysis.ShadingFill,
            OutlineLevel = analysis.OutlineLevel,
            IndentLeftTwips = analysis.IndentLeftTwips,
            HasLeftBorder = analysis.HasLeftBorder,
            CurrentSection = _tracker.CurrentSection.ToString(),
            InAbstractSection = _tracker.InAbstractSection
        };

        foreach (var rule in _rules)
        {
            if (rule.Condition.Evaluate(analysis))
            {
                // Create the elements
                var elements = rule.CreateElements(analysis, parser);

                // null means "condition matched but rule can't handle this paragraph" — fall through to next rule
                if (elements == null)
                    continue;

                // Execute side-effects (section tracker updates, etc.) only when rule produced a result
                rule.OnMatch?.Invoke(analysis, _tracker);

                // Record the match in the trace
                var elementList = elements.ToList();
                trace.MatchedRuleId = rule.Id;
                trace.MatchedRuleName = rule.Name;
                trace.DetectedType = rule.TargetType.ToString();
                trace.ElementsProduced = elementList.Count;

                if (elementList.Count == 0)
                {
                    trace.Notes = "Consumed without output (e.g., abstract heading marker)";
                }

                _traces.Add(trace);

                // Empty list means "consumed without output" (e.g., abstract heading marker)
                // Non-empty list means "matched with output elements"
                return elementList;
            }
        }

        // No rule matched
        trace.MatchedRuleId = "none";
        trace.DetectedType = "Dropped";
        trace.ElementsProduced = 0;
        trace.Notes = "No detection rule matched this paragraph";
        _traces.Add(trace);

        return null;
    }

    /// <summary>
    /// Add a trace entry for a non-paragraph body element (Table, SdtBlock, etc.).
    /// Called by DocxParser to trace all body elements, not just paragraphs.
    /// </summary>
    public void AddNonParagraphTrace(string elementType, string text, int elementsProduced, string detectedType)
    {
        _traces.Add(new ParagraphTraceEntry
        {
            BodyIndex = _traces.Count,
            ElementType = elementType,
            RawText = text.Length > 500 ? text[..500] : text,
            FullText = text,
            MatchedRuleId = "n/a",
            DetectedType = detectedType,
            ElementsProduced = elementsProduced,
            CurrentSection = _tracker.CurrentSection.ToString(),
            InAbstractSection = _tracker.InAbstractSection
        });
    }
}
