using Lilia.Import.Models;

namespace Lilia.Import.Detection;

/// <summary>
/// State machine that tracks the current document section as headings are encountered.
/// Provides context for section-aware detection rules (e.g., abstract paragraphs, bibliography entries).
/// </summary>
public class SectionTracker
{
    /// <summary>
    /// The current section type based on the most recent heading.
    /// </summary>
    public SectionType CurrentSection { get; private set; } = SectionType.Unknown;

    /// <summary>
    /// Whether we are currently inside an abstract section.
    /// Set when an abstract heading is encountered, cleared when the next non-abstract heading appears
    /// or certain block types break the section (e.g., lists).
    /// </summary>
    public bool InAbstractSection { get; set; }

    /// <summary>
    /// Called when a heading element is encountered during parsing.
    /// Updates the section tracker state based on the heading text.
    /// </summary>
    public void OnHeadingEncountered(string text, int level)
    {
        var sectionType = SectionKeywordRegistry.Classify(text);

        if (sectionType != SectionType.Unknown)
        {
            CurrentSection = sectionType;
        }

        // Any heading other than abstract ends the abstract section
        if (sectionType == SectionType.Abstract)
        {
            InAbstractSection = true;
        }
        else
        {
            InAbstractSection = false;
        }
    }

    /// <summary>
    /// Called when a Title/Subtitle styled element is encountered that matches abstract keywords.
    /// These may not be formal headings but still indicate the start of an abstract section.
    /// </summary>
    public void OnTitleOrSubtitleEncountered(string text)
    {
        if (SectionKeywordRegistry.IsAbstractKeyword(text))
        {
            CurrentSection = SectionType.Abstract;
            InAbstractSection = true;
        }
    }

    /// <summary>
    /// Called when an element type is encountered that breaks the abstract section
    /// (e.g., list items, which don't belong in an abstract).
    /// </summary>
    public void EndAbstractSection()
    {
        InAbstractSection = false;
    }

    /// <summary>
    /// Reset the tracker to its initial state.
    /// </summary>
    public void Reset()
    {
        CurrentSection = SectionType.Unknown;
        InAbstractSection = false;
    }
}
