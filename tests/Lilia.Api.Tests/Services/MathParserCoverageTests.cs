using FluentAssertions;
using Lilia.Core.Models.MathAst;
using Lilia.Core.Services.MathParser;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// How much of real maths the existing in-house parser actually handles.
///
/// <para>P3.4 was framed as "evaluate MiTeX, KaTeX, Temml, LaTeXML, then
/// populate the ast". The spike compared those four and recommended Temml —
/// having failed to check whether a parser already existed. One does:
/// <see cref="LaTeXMathParser"/>, 763 lines, with a tokeniser and a Typst
/// emitter beside it. Adding a JavaScript dependency to a .NET service, and
/// the process bridge to run it, is a large cost to pay before establishing
/// that the thing already in the repository is inadequate.</para>
///
/// <para>So this measures it against real equations, the same way every other
/// claim in this plan has been settled.</para>
/// </summary>
public class MathParserCoverageTests(ITestOutputHelper output)
{
    private static readonly LaTeXMathParser Parser = new();

    /// <summary>
    /// Equations taken from the corpus, spanning what the editor's own
    /// documents contain.
    /// </summary>
    private static readonly string[] RealEquations =
    [
        @"E = mc^2",
        @"\frac{1}{2}",
        @"\int_0^1 x \, dx",
        @"\sum_{i=1}^{n} \alpha_i",
        @"x^2 + y^2 = z^2",
        @"\sqrt{x + 1}",
        @"\frac{\dot{a}}{a}",
        @"\alpha + \beta = \gamma",
        @"a^2 + 2ab + b^2 = c^2",
        @"\mathbb{R}^n",
        @"\left( \frac{1}{2} \right)",
        @"\begin{pmatrix} 1 & 0 \\ 0 & 1 \end{pmatrix}",
        @"\lim_{x \to 0} \frac{\sin x}{x} = 1",
        @"f(x) = \begin{cases} x & x > 0 \\ -x & x \le 0 \end{cases}",
        @"\nabla \cdot \mathbf{E} = \frac{\rho}{\epsilon_0}",
        @"\hbar \omega",
        @"\mathcal{L} = T - V",
        @"P(A \mid B) = \frac{P(B \mid A) P(A)}{P(B)}",
        @"\sum_{k=0}^{\infty} \frac{x^k}{k!} = e^x",
        @"\theta = \arctan\left(\frac{y}{x}\right)",
    ];

    [Fact]
    public void The_parser_handles_ordinary_maths()
    {
        var failures = new List<(string Equation, string Error)>();

        foreach (var equation in RealEquations)
        {
            try
            {
                var node = Parser.Parse(equation);
                if (node is null) failures.Add((equation, "returned null"));
            }
            catch (Exception ex)
            {
                failures.Add((equation, ex.GetType().Name + ": " + ex.Message));
            }
        }

        var rate = 1.0 - (double)failures.Count / RealEquations.Length;
        output.WriteLine($"parsed {RealEquations.Length - failures.Count}/{RealEquations.Length} ({rate:P0})");
        foreach (var (equation, error) in failures) output.WriteLine($"  FAIL {equation}\n       {error}");

        // Deliberately a floor rather than a target. What matters is learning
        // where it stands before deciding whether to replace it.
        rate.Should().BeGreaterThan(0.5, "the in-house parser is the incumbent and its coverage decides P3.4");
    }

    /// <summary>All 180 equations the editor's own documents contain.</summary>
    private static string[] CorpusEquations()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "real-equations.txt");
        return File.ReadAllLines(path)
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Replace(@"\n", "\n").Replace(@"\\", @"\"))
            .ToArray();
    }

    [Fact]
    public void The_whole_corpus_parses()
    {
        // The measurement that decides P3.4. If the incumbent handles what our
        // documents actually contain, adding a JavaScript parser and a process
        // bridge to a .NET service buys nothing.
        var equations = CorpusEquations();
        equations.Length.Should().BeGreaterThan(150, "the fixture must actually be present");

        var failures = new List<(string Equation, string Error)>();

        foreach (var equation in equations)
        {
            try
            {
                if (Parser.Parse(equation) is null) failures.Add((equation, "returned null"));
            }
            catch (Exception ex)
            {
                failures.Add((equation, ex.GetType().Name + ": " + ex.Message));
            }
        }

        var rate = 1.0 - (double)failures.Count / equations.Length;
        output.WriteLine($"parsed {equations.Length - failures.Count}/{equations.Length} ({rate:P1})");
        foreach (var (equation, error) in failures.Take(10))
        {
            output.WriteLine($"  FAIL {equation[..Math.Min(70, equation.Length)]}\n       {error}");
        }

        rate.Should().BeGreaterThan(0.95);
    }

    [Fact]
    public void Parsing_never_throws_on_input_a_person_might_type()
    {
        // The AST is meant to be computed on save and on import. A parser that
        // throws on malformed input turns a half-typed equation into a failed
        // request, so whatever it cannot understand it must degrade on.
        var awkward = new[]
        {
            "", "   ", @"\", @"\frac{", @"}{", @"\unknowncommand{x}",
            @"x^", @"_{", @"\begin{pmatrix}", @"&", @"\\", @"$$",
        };

        foreach (var input in awkward)
        {
            var act = () => Parser.Parse(input);
            act.Should().NotThrow($"'{input}' is something a person can type mid-edit");
        }
    }

    [Fact]
    public void A_parsed_equation_carries_structure_rather_than_one_blob()
    {
        // The whole argument for an AST: a string can only be printed, a tree
        // can be used. If \frac came back as an opaque text node, the AST would
        // buy nothing over the source we already store.
        var node = Parser.Parse(@"\frac{1}{2}");

        node.Should().NotBeNull();
        Flatten(node).Should().Contain(n => n is FractionNode,
            "a fraction has to survive as a fraction for the AST to be worth storing");
    }

    private static IEnumerable<MathNode> Flatten(MathNode? node)
    {
        if (node is null) yield break;
        yield return node;

        foreach (var property in node.GetType().GetProperties())
        {
            if (typeof(MathNode).IsAssignableFrom(property.PropertyType))
            {
                foreach (var child in Flatten(property.GetValue(node) as MathNode)) yield return child;
            }
            else if (property.GetValue(node) is IEnumerable<MathNode> children)
            {
                foreach (var child in children.SelectMany(Flatten)) yield return child;
            }
        }
    }
}
