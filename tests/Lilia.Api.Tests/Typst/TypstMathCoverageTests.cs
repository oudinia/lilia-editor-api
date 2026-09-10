using FluentAssertions;
using Lilia.Api.Services;
using Xunit;
using Xunit.Abstractions;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// How much LaTeX mathematics the Typst generator can actually express.
///
/// <para>Typst is already the default PDF path — it is tried first for every
/// document and the result is used when it compiles. Maths, however, is passed
/// through largely as written and reinterpreted under Typst's own rules, which
/// overlap LaTeX only in the simplest cases. <c>$a^2 + b^2 = c^2$</c> happens
/// to mean the same thing in both. <c>$\blacksquare$</c> does not: Typst reads
/// <c>\b</c> as an escape and is left with the variable <c>lacksquare</c>,
/// which is the error a real paper produced on 2026-09-10.</para>
///
/// <para>When Typst fails the PDF path falls back to pdflatex, so nothing
/// breaks visibly — which is precisely why nobody noticed that the engine
/// producing a given PDF depends on whether its equations happened to be
/// expressible.</para>
///
/// <para>This suite measures that, expression by expression, with the compiler
/// as the oracle. A string assertion would prove a regex fired; only the
/// compiler proves the output is Typst.</para>
/// </summary>
public class TypstMathCoverageTests(ITestOutputHelper output)
{
    private static string Build(params Lilia.Core.Entities.Block[] blocks) =>
        new TypstExportService().BuildTypstDocument(Doc(), [.. blocks]);

    /// <summary>Every expression here appears in ordinary mathematical writing.</summary>
    public static TheoryData<string, string> Expressions => new()
    {
        { "a^2 + b^2 = c^2",                 "the Pythagorean identity itself" },
        { "x_i",                             "a subscript" },
        { @"\frac{a}{b}",                    "a fraction" },
        { @"\dfrac{a}{b}",                   "a display fraction" },
        { @"\sqrt{2}",                       "a square root" },
        { @"\sqrt[3]{x}",                    "an nth root" },
        { @"\sum_{i=1}^{n} x_i",             "a sum with limits" },
        { @"\int_0^1 f(x)\,dx",              "a definite integral" },
        { @"\lim_{x \to 0} f(x)",            "a limit" },
        { @"\alpha + \beta = \gamma",        "Greek letters" },
        { @"\Delta x \approx 0",             "an uppercase Greek letter" },
        { @"a \cdot b",                      "a multiplication dot" },
        { @"a \times b",                     "a times sign" },
        { @"x \leq y \geq z",                "inequalities" },
        { @"x \neq y",                       "not equal" },
        { @"A \subset B",                    "set inclusion" },
        { @"x \in S",                        "set membership" },
        { @"\mathbb{R}^n",                   "blackboard bold" },
        { @"\mathcal{L}",                    "calligraphic" },
        { @"\mathbf{v}",                     "bold vector" },
        { @"\text{if } x > 0",               "text inside maths" },
        { @"\blacksquare",                   "the QED square — the one that failed on a real paper" },
        { @"\triangle ABC \sim \triangle DEF", "similar triangles" },
        { @"\angle ABC = 90^\circ",          "an angle in degrees" },
        { @"\vec{v} \cdot \vec{w}",          "vectors" },
        { @"\hat{x}",                        "a hat accent" },
        { @"\bar{x}",                        "a bar accent" },
        { @"\partial f / \partial x",        "partial derivatives" },
        { @"\nabla f",                       "gradient" },
        { @"\infty",                         "infinity" },
        { @"\pm 1",                          "plus-minus" },
        { @"\ldots",                         "an ellipsis" },
        { @"\begin{pmatrix} a & b \\ c & d \end{pmatrix}", "a matrix" },
        { @"\left( \frac{a}{b} \right)",     "sized delimiters" },
        { @"f: X \to Y",                     "a mapping arrow" },
        { @"a \equiv b \pmod{n}",            "a congruence" },
    };

    /// <summary>
    /// Compiling is necessary and not sufficient.
    ///
    /// <para>The first version of this suite asserted only that typst
    /// succeeded, and reported 36/36. It was measuring silent deletion as
    /// success: the generator drops commands it has no mapping for, so
    /// <c>\blacksquare \times \nabla</c> emitted <c>$  $</c> — an empty
    /// equation, which compiles perfectly. A test that a wrong answer passes
    /// is worse than no test.</para>
    /// </summary>
    private static void ShouldSurvive(string latex, string what, Lilia.Core.Entities.Block block)
    {
        var typst = Build(block);
        var math = ExtractMath(typst);

        math.Trim().Should().NotBeEmpty(
            $"{what} — the generator emitted an empty equation rather than translating "
            + $"$ {latex} $. Dropping what it cannot express is the worst of the options.");

        math.Should().NotMatchRegex(@"\\[a-zA-Z]+",
            $"{what} — raw LaTeX reached the Typst source for $ {latex} $: {math.Trim()}");

        var result = Compile(typst);
        result.Ok.Should().BeTrue(
            $"{what} — $ {latex} $ must compile. typst said: {result.FirstProblem}");
    }

    /// <summary>The maths between the dollars, which is what is under test.</summary>
    private static string ExtractMath(string typst)
    {
        // The longest $…$ in the document. A preamble line can contain a
        // stray pair, and taking the first match reported every expression as
        // deleted — a wrong measurement is worse than none.
        return System.Text.RegularExpressions.Regex
            .Matches(typst, @"\$(.+?)\$", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .OrderByDescending(v => v.Length)
            .FirstOrDefault() ?? "";
    }

    [Theory, MemberData(nameof(Expressions))]
    public void AnEquationSurvivesIntoTypst(string latex, string what) =>
        ShouldSurvive(latex, what, Block("equation", new { source = latex, displayMode = true }));

    /// <summary>
    /// The same expressions inline in prose, which takes a different path
    /// through the generator than an equation block.
    /// </summary>
    [Theory, MemberData(nameof(Expressions))]
    public void TheSameExpressionSurvivesInlineInAParagraph(string latex, string what) =>
        ShouldSurvive(latex, what, Para($"Consider ${latex}$ in context."));

    /// <summary>
    /// Not an assertion — a measurement, printed so the number is visible and
    /// can be watched moving.
    /// </summary>
    [Fact]
    public void CoverageReport()
    {
        var total = 0;
        var ok = 0;
        var failures = new List<string>();

        foreach (var row in Expressions)
        {
            var latex = (string)row[0];
            total++;
            var typst = Build(Block("equation", new { source = latex, displayMode = true }));
            var math = ExtractMath(typst);
            var survived = !string.IsNullOrWhiteSpace(math)
                           && !System.Text.RegularExpressions.Regex.IsMatch(math, @"\\[a-zA-Z]+")
                           && Compile(typst).Ok;
            if (survived) ok++;
            else failures.Add($"{latex}   →   {(string.IsNullOrWhiteSpace(math) ? "DELETED" : math.Trim())}");
        }

        output.WriteLine($"Typst maths coverage: {ok}/{total} ({100.0 * ok / total:F0}%)");
        foreach (var f in failures) output.WriteLine($"  fails: {f}");

        ok.Should().BeGreaterThan(0, "if nothing compiles the generator is not emitting Typst at all");
    }
}
