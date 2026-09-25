using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A cross-reference in an exported document resolves.
///
/// <para>None did. Measured on 25 Sep by compiling an exported document with
/// pdflatex: <c>\ref</c>, <c>\eqref</c> and <c>@ref</c> all printed
/// <c>??</c>, and <c>\cref</c> printed as raw text. Two causes, both in this
/// exporter:</para>
///
/// <para><b>It renamed every label.</b> It wrote a table's <c>tab:results</c>
/// as <c>\label{tbl:tab:results}</c> — prefixing the author's full key with a
/// type prefix of its own (<c>eq:</c>, <c>fig:</c>, <c>tbl:</c>, <c>alg:</c>,
/// <c>thm:</c>). Every other part of the system treats a label as the full
/// key: LaTeX import, the table tool, the derived label chip, the reference
/// index, and the other renderer. So the label the PDF defined never matched
/// the one every reference named.</para>
///
/// <para><b>It escaped <c>\cref</c>.</b> Its passthrough knew <c>\ref</c> and
/// <c>\eqref</c> only, so <c>\cref</c> — what the @ picker writes — became
/// <c>\textbackslash{}cref\{…\}</c>, though cleveref is in the preamble.</para>
/// </summary>
public class LatexExportReferencesTests
{
    private static int _order;

    private static Block B(string type, string json) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(json),
    };

    private static string Export(params Block[] blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(
                new Document { Id = Guid.NewGuid(), Title = "Refs", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 },
                blocks.ToList(), [], new LaTeXExportOptions());

    [Theory]
    [InlineData(BlockTypes.Table, """{"label":"tab:results","caption":"R","headers":["A"],"rows":[["1"]]}""", "tab:results")]
    [InlineData(BlockTypes.Figure, """{"label":"fig:arch","caption":"A","src":"a.png"}""", "fig:arch")]
    [InlineData(BlockTypes.Equation, """{"label":"eq:loss","latex":"x=1","equationMode":"display"}""", "eq:loss")]
    [InlineData(BlockTypes.Algorithm, """{"label":"alg:main","caption":"M","code":"x"}""", "alg:main")]
    [InlineData(BlockTypes.Theorem, """{"label":"thm:main","theoremType":"theorem","text":"T"}""", "thm:main")]
    public void Writes_the_authors_label_as_it_is(string type, string json, string key)
    {
        var tex = Export(B(type, json));

        tex.Should().Contain($@"\label{{{key}}}");
        // Not renamed with a type prefix of the exporter's own.
        tex.Should().NotMatchRegex(@"\\label\{(tbl|fig|eq|alg|thm):" + System.Text.RegularExpressions.Regex.Escape(key) + @"\}");
    }

    [Fact]
    public void Writes_a_subfigures_label_as_it_is()
    {
        var tex = Export(B(BlockTypes.Figure,
            """{"label":"fig:all","caption":"All","subfigures":[{"src":"a.png","caption":"Left","label":"fig:left"}]}"""));

        tex.Should().Contain(@"\label{fig:left}");
        tex.Should().NotContain(@"\label{fig:fig:left}");
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("eqref")]
    [InlineData("cref")]
    [InlineData("Cref")]
    [InlineData("autoref")]
    [InlineData("pageref")]
    [InlineData("nameref")]
    public void Passes_every_reference_command_through_unescaped(string form)
    {
        var tex = Export(B(BlockTypes.Paragraph, JsonSerializer.Serialize(new { text = $@"See \{form}{{tab:results}} here." })));

        tex.Should().Contain($@"\{form}{{tab:results}}");
        tex.Should().NotContain($@"\textbackslash{{}}{form}");
    }

    [Fact]
    public void A_table_and_the_reference_to_it_name_the_same_key()
    {
        // The whole defect in one assertion: what the document defines and what
        // it points at must be the same string.
        var tex = Export(
            B(BlockTypes.Table, """{"label":"tab:results","caption":"R","headers":["A"],"rows":[["1"]]}"""),
            B(BlockTypes.Paragraph, JsonSerializer.Serialize(new { text = @"As \cref{tab:results} shows." })));

        tex.Should().Contain(@"\label{tab:results}").And.Contain(@"\cref{tab:results}");
    }
}

/// <summary>
/// The one rule for a label's compiled key, shared by the exporter and the
/// reference index. Both conventions in the codebase must keep resolving.
/// </summary>
public class LabelKeyTests
{
    [Theory]
    [InlineData("equation", "main", "eq:main")]      // the tested bare convention
    [InlineData("table", "results", "tbl:results")]  // bare keeps the exporter's historic prefix
    [InlineData("figure", "arch", "fig:arch")]
    [InlineData("algorithm", "main", "alg:main")]
    [InlineData("theorem", "main", "thm:main")]
    public void A_bare_name_keeps_its_kind_prefix(string type, string label, string expected) =>
        Lilia.Engines.LabelKey.Effective(type, label).Should().Be(expected);

    [Theory]
    [InlineData("table", "tab:results")]
    [InlineData("equation", "eq:loss")]
    [InlineData("figure", "fig:arch")]
    [InlineData("table", "tbl:already")]
    public void A_full_key_is_written_as_it_is(string type, string label) =>
        Lilia.Engines.LabelKey.Effective(type, label).Should().Be(label);

    [Fact]
    public void A_heading_is_never_prefixed() =>
        Lilia.Engines.LabelKey.Effective("heading", "intro").Should().Be("intro");

    [Fact]
    public void No_label_defines_nothing()
    {
        Lilia.Engines.LabelKey.Effective("table", "").Should().BeEmpty();
        Lilia.Engines.LabelKey.Effective("table", null).Should().BeEmpty();
    }

    [Fact]
    public void The_index_and_the_exporter_name_a_bare_label_the_same_way()
    {
        // If they disagreed, the panel would call a reference dangling while
        // the PDF resolved it — or the reverse.
        var eq = new Block
        {
            Id = Guid.NewGuid(), Type = BlockTypes.Equation, SortOrder = 0,
            Content = JsonDocument.Parse("""{"label":"main","latex":"x=1","equationMode":"display"}"""),
        };
        var para = new Block
        {
            Id = Guid.NewGuid(), Type = BlockTypes.Paragraph, SortOrder = 1,
            Content = JsonDocument.Parse("""{"text":"See @ref{eq:main}."}"""),
        };

        var report = Lilia.Engines.ReferenceIndex.Build([eq, para]);
        report.Targets.Single().Key.Should().Be("eq:main");
        report.Problems.Should().NotContain(p => p.Kind == "dangling");

        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(new Document { Id = Guid.NewGuid(), Title = "t", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 },
                [eq, para], [], new LaTeXExportOptions());
        tex.Should().Contain(@"\label{eq:main}").And.Contain(@"\ref{eq:main}");
    }
}
