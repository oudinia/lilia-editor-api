using FluentAssertions;
using Lilia.Engines;
using Xunit;
using Xunit.Abstractions;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// Every entry in the symbol table, compiled.
///
/// <para>A lookup table is only as good as its right-hand side, and those are
/// guesses until something checks them. <c>\partial</c> was mapped to
/// <c>diff</c>, which reads like a Typst name and is not one — the equation
/// compiled nowhere and the table looked fine. There is no way to catch that
/// by reading; the compiler has to be asked.</para>
///
/// <para>So: one test per table, walking every entry and compiling it. Adding
/// a symbol is a line of data and this suite says whether the line is
/// right.</para>
/// </summary>
public class TypstSymbolTableTests(ITestOutputHelper output)
{
    /// <summary>Compile a fragment in maths mode and report what typst said.</summary>
    private static (bool Ok, string Error) TryMath(string body)
    {
        var result = Compile($"#set page(width: 12cm, height: auto)\n$ {body} $\n");
        return (result.Ok, result.FirstProblem);
    }

    private void AssertEveryEntry(IEnumerable<(string Latex, string Typst)> entries, string table)
    {
        var bad = new List<string>();
        var count = 0;

        foreach (var (latex, typst) in entries)
        {
            count++;
            var (ok, error) = TryMath(typst);
            if (!ok) bad.Add($"\\{latex} → {typst}   ({error})");
        }

        output.WriteLine($"{table}: {count - bad.Count}/{count} entries valid");
        foreach (var b in bad) output.WriteLine($"  {b}");

        bad.Should().BeEmpty($"every {table} entry must be something Typst actually knows");
    }

    [Fact]
    public void EverySymbolIsRealTypst() =>
        AssertEveryEntry(
            LatexToTypstSymbols.Symbols.Select(kv => (kv.Key, kv.Value)), "Symbols");

    [Fact]
    public void EveryFunctionIsRealTypst() =>
        AssertEveryEntry(
            LatexToTypstSymbols.Functions.Select(f => (f, f)), "Functions");

    [Fact]
    public void EveryAccentIsRealTypst() =>
        AssertEveryEntry(
            LatexToTypstSymbols.Accents.Select(kv => (kv.Key, $"{kv.Value}(x)")), "Accents");

    [Fact]
    public void EveryFontIsRealTypst() =>
        AssertEveryEntry(
            LatexToTypstSymbols.Fonts
                .Where(kv => kv.Value != "text")          // text becomes a string literal, not a call
                .Select(kv => (kv.Key, $"{kv.Value}(x)")), "Fonts");

    [Fact]
    public void EverySpacingEntryIsRealTypst() =>
        AssertEveryEntry(
            LatexToTypstSymbols.Spacing
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => (kv.Key, $"a {kv.Value} b")), "Spacing");
}
