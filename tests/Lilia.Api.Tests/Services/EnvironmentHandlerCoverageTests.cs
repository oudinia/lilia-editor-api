using System.Reflection;
using FluentAssertions;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Layer 2 of the leak-prevention strategy: every name in
/// <c>LatexParser.KnownEnvironments</c> must have a working handler.
///
/// Pre-SG-117, <c>tabular</c> sat in the set with no <c>case "tabular":</c>
/// in the switch — bare \begin{tabular} silently fell into the
/// unknown-env catch-all. This test fires a telemetry sink while
/// parsing one minimal doc per env and asserts no <c>unknown_env</c>
/// event was recorded.
/// </summary>
public class EnvironmentHandlerCoverageTests
{
    /// <summary>
    /// Envs where minimal body templates would need package-specific
    /// scaffolding the parser can't produce alone. They have handlers
    /// but require richer fixtures than this gate provides.
    /// </summary>
    private static readonly HashSet<string> SkipForGate = new(StringComparer.OrdinalIgnoreCase)
    {
        "document",       // wrapper, never user content
        "subfigure",      // needs caption/label scaffolding
        "thebibliography",// needs {99}\bibitem{...} body
        "minted",         // needs language argument
        "algorithm2e",    // distinct package; main `algorithm` covers the gate
        "algorithmic",    // inner block; covered by `algorithm`
    };

    /// <summary>Per-env body templates that satisfy the parser without errors.</summary>
    private static readonly Dictionary<string, string> EnvBody = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abstract"] = "Abstract text.",
        ["equation"] = "x = 1",
        ["align"] = "x &= 1 \\\\ y &= 2",
        ["gather"] = "x = 1 \\\\ y = 2",
        ["multline"] = "x = 1",
        ["eqnarray"] = "x &= 1",
        ["lstlisting"] = "code line",
        ["verbatim"] = "verbatim text",
        ["figure"] = "\\includegraphics{img}\n\\caption{caption}",
        ["table"] = "\\begin{tabular}{l}cell\\end{tabular}",
        ["tabular"] = "{l}cell",      // col spec is part of the body for our wrapper
        ["itemize"] = "\\item foo \\item bar",
        ["enumerate"] = "\\item foo \\item bar",
        ["description"] = "\\item[Term] description",
        ["quote"] = "Quoted text.",
        ["quotation"] = "Quotation text.",
        ["verse"] = "Verse line.",
        ["center"] = "Centered text.",
        ["flushleft"] = "Left-aligned text.",
        ["flushright"] = "Right-aligned text.",
        ["algorithm"] = "\\begin{algorithmic}\\State $x = 1$\\end{algorithmic}",
    };

    public static IEnumerable<object[]> KnownEnvNames()
    {
        var field = typeof(LatexParser).GetField("KnownEnvironments",
            BindingFlags.NonPublic | BindingFlags.Static);
        var set = (HashSet<string>)field!.GetValue(null)!;
        foreach (var name in set.Where(n => !SkipForGate.Contains(n)))
        {
            yield return new object[] { name };
        }
    }

    [Theory]
    [MemberData(nameof(KnownEnvNames))]
    public async Task Every_known_env_has_a_handler(string envName)
    {
        var sink = new RecordingSink();
        var parser = new LatexParser(NullTokenRouter.Instance, telemetry: sink);

        // The `tabular` row in EnvBody starts with `{l}` (the col spec),
        // so the wrapping below produces a complete \begin{tabular}{l}cell\end{tabular}.
        // Every other env wraps body verbatim.
        var body = EnvBody.GetValueOrDefault(envName, "body content");
        var src = $"\\documentclass{{article}}\n\\begin{{document}}\n\\begin{{{envName}}}{body}\\end{{{envName}}}\n\\end{{document}}";

        await parser.ParseTextAsync(src);

        var unknownEnvHits = sink.Events
            .Where(e => e.EventKind == "unknown_env" && e.TokenOrEnv == envName)
            .ToList();

        unknownEnvHits.Should().BeEmpty(
            $"env '{envName}' is in KnownEnvironments but emitted unknown_env telemetry — " +
            "the matcher regex isn't producing a match the switch can dispatch on, " +
            "or the case branch is missing. See SG-117 for the canonical bug shape.");
    }

    private sealed class RecordingSink : IImportTelemetrySink
    {
        public List<ImportTelemetryRecord> Events { get; } = new();
        public void Record(ImportTelemetryRecord evt) => Events.Add(evt);
    }
}
