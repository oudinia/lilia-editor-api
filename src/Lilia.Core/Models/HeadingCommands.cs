namespace Lilia.Core.Models;

/// <summary>
/// Which LaTeX sectioning command a Lilia heading level prints as, by document class.
///
/// <para>In a class that has <c>\chapter</c> (report, book, memoir, KOMA's scrbook and scrreprt,
/// amsbook) a level-1 heading is a chapter and level 2 a section, so sections number 1.1 under
/// chapter 1. Everywhere else level 1 is <c>\section</c>. Writing <c>\section</c> for level 1 in a
/// report printed every number as "0.1", because the chapter counter never moved (user's
/// decision, 6 Oct 2026). The importer reads the same table back, so a report's <c>\chapter</c>
/// imports as a level-1 heading.</para>
/// </summary>
public static class HeadingCommands
{
    /// <summary>The classes whose top numbered heading is <c>\chapter</c>.</summary>
    public static readonly IReadOnlySet<string> ChapterClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "report", "book", "memoir", "scrbook", "scrreprt", "amsbook",
    };

    private static readonly string[] SectionTop = { "section", "subsection", "subsubsection", "paragraph", "subparagraph" };
    private static readonly string[] ChapterTop = { "chapter", "section", "subsection", "subsubsection", "paragraph", "subparagraph" };

    /// <summary>True when <paramref name="documentClass"/> has <c>\chapter</c>.</summary>
    public static bool HasChapters(string? documentClass) =>
        !string.IsNullOrWhiteSpace(documentClass) && ChapterClasses.Contains(documentClass.Trim());

    /// <summary>
    /// The command for heading <paramref name="level"/> (1-based). LaTeX has nothing below
    /// <c>\subparagraph</c>, so a deeper level stays at the deepest command.
    /// </summary>
    public static string For(int level, string? documentClass)
    {
        var commands = HasChapters(documentClass) ? ChapterTop : SectionTop;
        return commands[Math.Clamp(level, 1, commands.Length) - 1];
    }

    /// <summary>
    /// The heading level a sectioning command imports as. A <c>\chapter</c> in a class without
    /// chapters (it would not compile there) is still the top level.
    /// </summary>
    public static int LevelOf(string command, string? documentClass)
    {
        var commands = HasChapters(documentClass) ? ChapterTop : SectionTop;
        var index = Array.IndexOf(commands, command);
        return index >= 0 ? index + 1 : 1;
    }

    /// <summary>The deepest heading level the class has a command for (5, or 6 with chapters).</summary>
    public static int DeepestLevel(string? documentClass) =>
        HasChapters(documentClass) ? ChapterTop.Length : SectionTop.Length;
}
