using Microsoft.Extensions.Logging;

namespace Lilia.Engines;

/// <summary>
/// The outcome of actually compiling a LaTeX fragment, so a caller can state what
/// is known rather than assert what is hoped. <c>Status</c> is one of:
/// <c>verified</c> (compiled clean), <c>failed</c> (compiled with errors — the
/// findings say which), or <c>unchecked</c> (no compiler reachable; claim nothing).
/// </summary>
/// <param name="Engine">
/// The TeX binary the verdict is about — <c>pdflatex</c>, <c>xelatex</c> or
/// <c>lualatex</c>. "Compiles" is not a claim on its own: the same source can
/// pass under one engine and fail under another, so the verdict is incomplete
/// without saying which one produced it. Null when nothing was compiled.
/// </param>
/// <param name="EngineAuto">
/// Whether that engine was inferred from the content rather than asked for. The
/// UI distinguishes the two: a detected engine is a guess the author may want to
/// override, a chosen one is a requirement they stated.
/// </param>
public record LatexVerdict(string Status, string[] Findings, int DurationMs, string? Engine = null, bool EngineAuto = false)
{
    public static readonly LatexVerdict Unchecked = new("unchecked", [], 0);
}

/// <summary>
/// Compile a fragment and report what actually happened.
///
/// <para>This exists as its own service, rather than as a private method on the
/// caller that first needed it, because the claim it produces — <em>we compiled
/// this and it passed</em> — is the one thing every surface that emits LaTeX
/// wants to be able to make, and there must be exactly one implementation of it.
/// A second copy is a second set of engine-retry rules that can disagree with
/// this one about whether the same document compiles.</para>
/// </summary>
public interface ILatexVerifier
{
    /// <param name="requested">
    /// The engine the author asked for, or null to infer it from the content.
    /// </param>
    Task<LatexVerdict> VerifyAsync(string latexFragment, LatexEngine? requested = null);
}

/// <inheritdoc cref="ILatexVerifier"/>
public sealed class LatexVerifier : ILatexVerifier
{
    /// <summary>
    /// Verification has to finish inside a web request, so it gets a much shorter
    /// leash than the 30s default. A fragment that can't compile in this long is
    /// reported as unchecked rather than made to wait.
    /// </summary>
    private const int VerifyTimeoutSeconds = 15;

    private readonly ICompilationQueueService _compiler;
    private readonly IEngineResolver _engines;
    private readonly ILogger<LatexVerifier> _logger;

    public LatexVerifier(
        ICompilationQueueService compiler,
        IEngineResolver engines,
        ILogger<LatexVerifier> logger)
    {
        _compiler = compiler;
        _engines = engines;
        _logger = logger;
    }

    /// <summary>
    /// Compile the fragment and report what actually happened. Never throws: if the
    /// compiler is unreachable (no TeX on a dev box, queue saturated, timeout) the
    /// verdict is <c>unchecked</c>, because claiming a fragment compiles when nothing
    /// compiled it is the exact failure this exists to prevent.
    /// </summary>
    /// <param name="requested">
    /// The engine the author asked for, or null to infer it. An explicit choice is
    /// honoured even when detection disagrees: someone whose journal mandates
    /// pdflatex needs to know whether it compiles *there*, and a verdict from an
    /// engine they will never run is not an answer to their question.
    /// </param>
    public async Task<LatexVerdict> VerifyAsync(string latexFragment, LatexEngine? requested = null)
    {
        var auto = requested is null;
        // Detection is the default because most authors neither know nor should
        // have to care; a fragment using \setmainfont fails under pdflatex for
        // reasons that say nothing about the content.
        var engine = requested ?? _engines.Resolve(latexFragment);
        var name = engine.ToCli();

        try
        {
            var document = LaTeXPreamble.WrapForValidation(latexFragment, engine);
            var result = await _compiler.CompileLatexAsync(
                document, CompilationType.Validate, VerifyTimeoutSeconds, engine);
            var ms = (int)result.Duration.TotalMilliseconds;

            if (result.Success)
                return new LatexVerdict("verified", [], ms, name, auto);

            // Detection is a guess, and a guess that predicts the wrong engine
            // reports a fine document as broken. TeX itself knows the answer and
            // says so plainly, so when the engine we *chose for them* turns out
            // to be wrong, believe the compiler and run it again properly.
            //
            // Only for an inferred engine. If the author asked for pdflatex —
            // because their journal demands it — then "it does not compile under
            // pdflatex" is the answer to their question, not an error to route
            // around.
            if (auto && engine != LatexEngine.Lualatex && IndicatesWrongEngine(result))
            {
                var retryEngine = LatexEngine.Lualatex;
                var retryDoc = LaTeXPreamble.WrapForValidation(latexFragment, retryEngine);
                var retry = await _compiler.CompileLatexAsync(
                    retryDoc, CompilationType.Validate, VerifyTimeoutSeconds, retryEngine);
                var retryMs = ms + (int)retry.Duration.TotalMilliseconds;
                var retryName = retryEngine.ToCli();

                _logger.LogInformation(
                    "[Verify] {First} rejected the document as engine-mismatched; {Second} {Outcome}",
                    name, retryName, retry.Success ? "accepted it" : "did not");

                return retry.Success
                    ? new LatexVerdict("verified", [], retryMs, retryName, auto)
                    : new LatexVerdict("failed", ExtractFindings(retry), retryMs, retryName, auto);
            }

            return new LatexVerdict("failed", ExtractFindings(result), ms, name, auto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Verify] verification unavailable — reporting unchecked");
            return LatexVerdict.Unchecked;
        }
    }

    /// <summary>
    /// Whether a failure says "you ran the wrong engine" rather than "your document
    /// is wrong".
    ///
    /// <para>These are the phrases the packages themselves emit, verified against a
    /// real run: fontspec aborts with <c>Fatal Package fontspec Error: The fontspec
    /// package requires either XeTeX or LuaTeX</c>. Matching on the stated
    /// requirement rather than on a package name keeps it working for packages we
    /// have never heard of, which is the point — the compiler knows things our
    /// detector does not.</para>
    /// </summary>
    private static bool IndicatesWrongEngine(CompilationResult result)
    {
        var log = result.Error ?? string.Empty;
        return log.Contains("requires either XeTeX or LuaTeX", StringComparison.OrdinalIgnoreCase)
            || log.Contains("requires XeTeX or LuaTeX", StringComparison.OrdinalIgnoreCase)
            || log.Contains("only be used with", StringComparison.OrdinalIgnoreCase)
               && log.Contains("LuaTeX", StringComparison.OrdinalIgnoreCase)
            || log.Contains("requires LuaTeX", StringComparison.OrdinalIgnoreCase)
            || log.Contains("requires XeTeX", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pull the human-readable causes out of a failed compile. LaTeX errors are the
    /// lines starting with `!`; warnings are the fallback when the log has no error
    /// line (an overfull box fails validation without an `!`).
    /// </summary>
    private static string[] ExtractFindings(CompilationResult result)
    {
        var errors = (result.Error ?? string.Empty)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith('!'))
            .Select(l => l.TrimStart('!').Trim())
            .Where(l => l.Length > 0)
            .Take(3)
            .ToArray();

        if (errors.Length > 0) return errors;

        var warnings = result.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Take(3).ToArray();
        return warnings.Length > 0 ? warnings : ["The document did not compile, and LaTeX gave no reason."];
    }
}
