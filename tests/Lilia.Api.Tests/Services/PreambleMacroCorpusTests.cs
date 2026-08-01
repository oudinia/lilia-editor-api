using FluentAssertions;
using Lilia.Core.Capabilities;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The collector against 120 macro definitions taken verbatim from TeX.SE.
///
/// <para>Hand-written examples test what I imagined people write. This tests
/// what they actually wrote — and the corpus has already produced two
/// surprises: <c>\newcommand{\R}{8}</c>, and <c>\def</c> turning out to be
/// nearly as common as <c>\newcommand</c>.</para>
///
/// <para><b>Measured: 111 of 120, 92.5%.</b> The threshold is deliberately not
/// 100%, because all nine misses are things the collector should refuse:</para>
///
/// <list type="bullet">
/// <item><b>Internal names with <c>@</c></b> — <c>\def\@currentlabel</c>,
/// <c>\def\lst@boxpos</c>. Package plumbing, legal only inside
/// <c>\makeatletter</c>, and never something an author writes in an
/// equation.</item>
/// <item><b>Names with no backslash</b> — <c>\newcommand{foo}{...}</c>. Not a
/// valid definition; LaTeX requires <c>{\foo}</c>. Collecting it would invent a
/// macro the document does not have.</item>
/// <item><b>A different command</b> — <c>\newcommandx</c>, from <c>xargs</c>.
/// Matching it as <c>\newcommand</c> would misread its arguments.</item>
/// </list>
///
/// <para>So the number to watch is not the 7.5% missed but whether anything is
/// <i>wrongly</i> collected, which the name-shape test below covers. A phantom
/// macro makes the resolver stop asking about a command that really is missing
/// — a false all-clear, and the worst outcome available here.</para>
/// </summary>
public class PreambleMacroCorpusTests(ITestOutputHelper output)
{
    private static string[] RealDefinitions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "real-macro-definitions.txt");
        return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
    }

    [Fact]
    public void The_fixture_is_present_and_substantial()
    {
        // If this file goes missing the tests below would pass vacuously.
        RealDefinitions().Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public void Nearly_every_real_definition_yields_a_macro()
    {
        var lines = RealDefinitions();
        var missed = new List<string>();

        foreach (var line in lines)
        {
            if (PreambleMacroCollector.Collect(line).Count == 0) missed.Add(line);
        }

        var rate = 1.0 - (double)missed.Count / lines.Length;
        output.WriteLine($"collected {lines.Length - missed.Count}/{lines.Length} ({rate:P1})");
        foreach (var m in missed.Take(15)) output.WriteLine("  missed: " + m);

        rate.Should().BeGreaterThan(0.90,
            "the collector exists to handle what people actually write");
    }

    [Fact]
    public void No_definition_produces_a_macro_with_an_empty_or_absurd_name()
    {
        // A phantom or malformed name is the dangerous direction: the resolver
        // would treat some command as document-defined and stop asking about
        // it, which is a false all-clear.
        foreach (var line in RealDefinitions())
        {
            foreach (var (name, macro) in PreambleMacroCollector.Collect(line))
            {
                name.Should().StartWith("\\", $"in: {line}");
                name.Length.Should().BeGreaterThan(1, $"in: {line}");
                name.Should().NotContain(" ", $"in: {line}");
                macro.Arity.Should().BeInRange(0, 9, $"in: {line}");
            }
        }
    }

    [Fact]
    public void Collecting_the_whole_fixture_at_once_matches_line_by_line()
    {
        // A real preamble is many definitions in one string, and the scanner
        // must not lose or merge them. Later-wins means duplicates collapse, so
        // compare against the distinct set.
        var lines = RealDefinitions();

        var perLine = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            foreach (var name in PreambleMacroCollector.Collect(line).Keys) perLine.Add(name);
        }

        var together = PreambleMacroCollector.Collect(string.Join("\n", lines)).Keys.ToHashSet(StringComparer.Ordinal);

        together.Should().BeEquivalentTo(perLine,
            "scanning a whole preamble must find exactly what scanning its lines finds");
    }
}
