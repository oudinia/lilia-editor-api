using System.Text.RegularExpressions;
using Lilia.Import.Models;

namespace Lilia.Import.Detection;

/// <summary>
/// Abstract base for conditions that evaluate a ParagraphAnalysis to determine
/// whether a detection rule should match.
/// </summary>
public abstract class DetectionCondition
{
    /// <summary>
    /// Evaluate whether this condition matches the given paragraph analysis.
    /// </summary>
    public abstract bool Evaluate(ParagraphAnalysis analysis);

    /// <summary>
    /// Human-readable description of this condition.
    /// </summary>
    public abstract string Description { get; }
}

/// <summary>
/// How to match a style ID string.
/// </summary>
public enum StyleMatchMode
{
    Exact,
    Contains,
    StartsWith,
    Regex
}

/// <summary>
/// Matches against the paragraph's StyleId.
/// </summary>
public class StyleMatchCondition : DetectionCondition
{
    private readonly string _pattern;
    private readonly StyleMatchMode _mode;
    private readonly StringComparison _comparison;
    private readonly Regex? _regex;

    public StyleMatchCondition(string pattern, StyleMatchMode mode = StyleMatchMode.Contains, bool ignoreCase = true)
    {
        _pattern = pattern;
        _mode = mode;
        _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (mode == StyleMatchMode.Regex)
        {
            var opts = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            _regex = new Regex(pattern, opts | RegexOptions.Compiled);
        }
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (string.IsNullOrEmpty(analysis.StyleId))
            return false;

        return _mode switch
        {
            StyleMatchMode.Exact => analysis.StyleId.Equals(_pattern, _comparison),
            StyleMatchMode.Contains => analysis.StyleId.Contains(_pattern, _comparison),
            StyleMatchMode.StartsWith => analysis.StyleId.StartsWith(_pattern, _comparison),
            StyleMatchMode.Regex => _regex!.IsMatch(analysis.StyleId),
            _ => false
        };
    }

    public override string Description => $"StyleId {_mode} '{_pattern}'";
}

/// <summary>
/// Matches against the paragraph's font family using a set of known fonts.
/// </summary>
public class FontMatchCondition : DetectionCondition
{
    private readonly HashSet<string> _fonts;

    public FontMatchCondition(HashSet<string> fonts)
    {
        _fonts = fonts;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        return !string.IsNullOrEmpty(analysis.FontFamily) && _fonts.Contains(analysis.FontFamily);
    }

    public override string Description => $"FontFamily in [{string.Join(", ", _fonts.Take(3))}...]";
}

/// <summary>
/// Matches against shading fill color (gray brightness range).
/// </summary>
public class ShadingCondition : DetectionCondition
{
    private readonly int _minBrightness;
    private readonly int _maxBrightness;

    public ShadingCondition(int minBrightness = 180, int maxBrightness = 250)
    {
        _minBrightness = minBrightness;
        _maxBrightness = maxBrightness;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (string.IsNullOrEmpty(analysis.ShadingFill) || analysis.ShadingFill.Length != 6)
            return false;

        if (!analysis.ShadingFill.All(char.IsLetterOrDigit))
            return false;

        try
        {
            var r = Convert.ToInt32(analysis.ShadingFill.Substring(0, 2), 16);
            var g = Convert.ToInt32(analysis.ShadingFill.Substring(2, 2), 16);
            var b = Convert.ToInt32(analysis.ShadingFill.Substring(4, 2), 16);

            // Exclude white and near-white
            if (r >= 250 && g >= 250 && b >= 250)
                return false;

            var isGray = Math.Abs(r - g) < 20 && Math.Abs(g - b) < 20 && Math.Abs(r - b) < 20;
            var isInRange = r > _minBrightness && g > _minBrightness && b > _minBrightness
                         && r < _maxBrightness && g < _maxBrightness && b < _maxBrightness;
            return isGray && isInRange;
        }
        catch
        {
            return false;
        }
    }

    public override string Description => $"ShadingFill gray in [{_minBrightness}-{_maxBrightness}]";
}

/// <summary>
/// Matches against formatting properties (bold, italic, font size, caps).
/// </summary>
public class FormattingCondition : DetectionCondition
{
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public double? MinFontSize { get; init; }
    public double? MaxFontSize { get; init; }
    public bool? AllCaps { get; init; }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (Bold.HasValue && analysis.AllBold != Bold.Value)
            return false;
        if (Italic.HasValue && analysis.AllItalic != Italic.Value)
            return false;
        if (AllCaps.HasValue && analysis.AllCaps != AllCaps.Value)
            return false;
        if (MinFontSize.HasValue && (!analysis.FontSizePoints.HasValue || analysis.FontSizePoints.Value < MinFontSize.Value))
            return false;
        if (MaxFontSize.HasValue && analysis.FontSizePoints.HasValue && analysis.FontSizePoints.Value > MaxFontSize.Value)
            return false;
        return true;
    }

    public override string Description
    {
        get
        {
            var parts = new List<string>();
            if (Bold.HasValue) parts.Add($"Bold={Bold}");
            if (Italic.HasValue) parts.Add($"Italic={Italic}");
            if (MinFontSize.HasValue) parts.Add($"FontSize>={MinFontSize}");
            if (MaxFontSize.HasValue) parts.Add($"FontSize<={MaxFontSize}");
            if (AllCaps.HasValue) parts.Add($"AllCaps={AllCaps}");
            return $"Formatting({string.Join(", ", parts)})";
        }
    }
}

/// <summary>
/// Matches the paragraph text against a regex pattern.
/// </summary>
public class ContentPatternCondition : DetectionCondition
{
    private readonly Regex _regex;
    private readonly string _patternDesc;

    public enum MatchMode { FullMatch, Contains, StartsWith }

    public ContentPatternCondition(string pattern, MatchMode mode = MatchMode.Contains, bool ignoreCase = true)
    {
        var opts = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var adjustedPattern = mode switch
        {
            MatchMode.FullMatch => $"^{pattern}$",
            MatchMode.StartsWith => $"^{pattern}",
            _ => pattern
        };
        _regex = new Regex(adjustedPattern, opts | RegexOptions.Compiled);
        _patternDesc = pattern;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        return !string.IsNullOrEmpty(analysis.Text) && _regex.IsMatch(analysis.Text);
    }

    public override string Description => $"Text matches '{_patternDesc}'";
}

/// <summary>
/// Matches based on numbering properties.
/// </summary>
public class NumberingCondition : DetectionCondition
{
    public bool? HasNumbering { get; init; }
    public bool? IsNumbered { get; init; }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (HasNumbering.HasValue && analysis.HasNumberingProperties != HasNumbering.Value)
            return false;
        if (IsNumbered.HasValue && analysis.IsNumberedList != IsNumbered.Value)
            return false;
        return true;
    }

    public override string Description => $"Numbering(has={HasNumbering}, numbered={IsNumbered})";
}

/// <summary>
/// Matches based on OpenXml element presence (math, drawings, page breaks).
/// </summary>
public class OpenXmlCondition : DetectionCondition
{
    public bool? HasMathElements { get; init; }
    public bool? HasDrawings { get; init; }
    public bool? HasPageBreaks { get; init; }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (HasMathElements.HasValue && analysis.HasMathElements != HasMathElements.Value)
            return false;
        if (HasDrawings.HasValue && analysis.HasDrawings != HasDrawings.Value)
            return false;
        if (HasPageBreaks.HasValue && analysis.HasPageBreaks != HasPageBreaks.Value)
            return false;
        return true;
    }

    public override string Description => $"OpenXml(math={HasMathElements}, draw={HasDrawings}, pb={HasPageBreaks})";
}

/// <summary>
/// Matches based on the current document section context from the SectionTracker.
/// </summary>
public class SectionContextCondition : DetectionCondition
{
    private readonly HashSet<SectionType>? _allowedSections;
    private readonly HashSet<SectionType>? _disallowedSections;
    private readonly bool? _inAbstractSection;

    public SectionContextCondition(
        HashSet<SectionType>? allowedSections = null,
        HashSet<SectionType>? disallowedSections = null,
        bool? inAbstractSection = null)
    {
        _allowedSections = allowedSections;
        _disallowedSections = disallowedSections;
        _inAbstractSection = inAbstractSection;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (_allowedSections != null && !_allowedSections.Contains(analysis.CurrentSection))
            return false;
        if (_disallowedSections != null && _disallowedSections.Contains(analysis.CurrentSection))
            return false;
        if (_inAbstractSection.HasValue && analysis.InAbstractSection != _inAbstractSection.Value)
            return false;
        return true;
    }

    public override string Description => $"SectionContext(allowed={_allowedSections?.Count}, abstract={_inAbstractSection})";
}

/// <summary>
/// Matches based on paragraph indentation and borders.
/// </summary>
public class IndentCondition : DetectionCondition
{
    public int? MinIndentTwips { get; init; }
    public bool? HasLeftBorder { get; init; }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (MinIndentTwips.HasValue && analysis.IndentLeftTwips < MinIndentTwips.Value)
            return false;
        if (HasLeftBorder.HasValue && analysis.HasLeftBorder != HasLeftBorder.Value)
            return false;
        return true;
    }

    public override string Description => $"Indent(min={MinIndentTwips}, border={HasLeftBorder})";
}

/// <summary>
/// Matches when the paragraph text exactly matches any keyword in a set (case-insensitive).
/// Used for section heading detection.
/// </summary>
public class HeadingTextCondition : DetectionCondition
{
    private readonly HashSet<string> _keywords;

    public HeadingTextCondition(HashSet<string> keywords)
    {
        _keywords = keywords;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(analysis.Text))
            return false;
        return _keywords.Contains(analysis.Text.Trim());
    }

    public override string Description => $"HeadingText in [{string.Join(", ", _keywords.Take(3))}...]";
}

/// <summary>
/// Combines multiple conditions with AND or OR logic.
/// </summary>
public class CompositeCondition : DetectionCondition
{
    public enum CompositeMode { And, Or }

    private readonly List<DetectionCondition> _conditions;
    private readonly CompositeMode _mode;

    public CompositeCondition(CompositeMode mode, params DetectionCondition[] conditions)
    {
        _mode = mode;
        _conditions = [.. conditions];
    }

    public CompositeCondition(CompositeMode mode, IEnumerable<DetectionCondition> conditions)
    {
        _mode = mode;
        _conditions = [.. conditions];
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        return _mode switch
        {
            CompositeMode.And => _conditions.All(c => c.Evaluate(analysis)),
            CompositeMode.Or => _conditions.Any(c => c.Evaluate(analysis)),
            _ => false
        };
    }

    public override string Description => $"{_mode}({string.Join(", ", _conditions.Select(c => c.Description))})";

    public static CompositeCondition And(params DetectionCondition[] conditions) => new(CompositeMode.And, conditions);
    public static CompositeCondition Or(params DetectionCondition[] conditions) => new(CompositeMode.Or, conditions);
}

/// <summary>
/// Negates an inner condition.
/// </summary>
public class NotCondition : DetectionCondition
{
    private readonly DetectionCondition _inner;

    public NotCondition(DetectionCondition inner)
    {
        _inner = inner;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        return !_inner.Evaluate(analysis);
    }

    public override string Description => $"NOT({_inner.Description})";
}

/// <summary>
/// Always matches. Used as the fallback condition for the paragraph rule.
/// </summary>
public class AlwaysTrueCondition : DetectionCondition
{
    public override bool Evaluate(ParagraphAnalysis analysis) => true;

    public override string Description => "Always";
}

/// <summary>
/// Matches against a set of style patterns (case-insensitive contains check).
/// </summary>
public class StyleSetCondition : DetectionCondition
{
    private readonly HashSet<string> _patterns;

    public StyleSetCondition(HashSet<string> patterns)
    {
        _patterns = patterns;
    }

    public override bool Evaluate(ParagraphAnalysis analysis)
    {
        if (string.IsNullOrEmpty(analysis.StyleId))
            return false;

        foreach (var pattern in _patterns)
        {
            if (analysis.StyleId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public override string Description => $"StyleId contains any of [{string.Join(", ", _patterns.Take(3))}...]";
}
