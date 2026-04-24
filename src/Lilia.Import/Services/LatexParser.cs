using System.Text.RegularExpressions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Import.Services;

/// <summary>
/// Parser for LaTeX files that outputs the intermediate ImportDocument model.
/// </summary>
public class LatexParser : ILatexParser
{
    private static readonly string[] SupportedExtensions = [".tex"];

    // Router that resolves (name, kind) → parser-handler decision via
    // the DB-backed catalog. Stage 3 of the parser-reads-catalog
    // migration runs the router in parallel with the hardcoded HashSets
    // below — HashSets are the source of truth for behavior; router
    // is observed for drift. Divergences land in _logger so we can
    // spot when the catalog and parser disagree before the final swap.
    private readonly ITokenRouter _router;
    private readonly ILogger<LatexParser> _logger;

    public LatexParser() : this(NullTokenRouter.Instance) { }

    public LatexParser(ITokenRouter router, ILogger<LatexParser>? logger = null)
    {
        _router = router ?? NullTokenRouter.Instance;
        _logger = logger ?? NullLogger<LatexParser>.Instance;
    }

    /// <summary>
    /// Stage-3 parallel-run for KnownEnvironments lookup. Returns the
    /// HashSet answer (authoritative) but also consults the router and
    /// logs Warning on disagreement. Centralised so every call site
    /// picks up the instrumentation at once when we add more.
    /// </summary>
    private bool IsKnownEnvironment(string envName) =>
        CheckDrift(envName, "environment", "known-structural", KnownEnvironments.Contains(envName));

    /// <summary>
    /// Stage-3 parallel-run for TheoremEnvironments lookup. Same
    /// pattern — HashSet answer authoritative, router observed.
    /// </summary>
    private bool IsTheoremEnvironment(string envName) =>
        CheckDrift(envName, "environment", "theorem-like", TheoremEnvironments.ContainsKey(envName));

    /// <summary>
    /// Stage-3 parallel-run for PassThroughEnvironments lookup.
    /// </summary>
    private bool IsPassThroughEnvironment(string envName) =>
        CheckDrift(envName, "environment", "pass-through", PassThroughEnvironments.Contains(envName));

    /// <summary>
    /// Stage-3 parallel-run for PreservedInlineCommands lookup
    /// (\cite / \ref / \label / \href / \url / \footnote / …).
    /// </summary>
    private bool IsPreservedInlineCommand(string name) =>
        CheckDrift(name, "command", "inline-preserved", PreservedInlineCommands.Contains(name));

    /// <summary>
    /// Stage-3 parallel-run for CodeDisplayInlineCommands lookup
    /// (\texttt / \verb / \lstinline / \path / …).
    /// </summary>
    private bool IsCodeDisplayInlineCommand(string name) =>
        CheckDrift(name, "command", "inline-code", CodeDisplayInlineCommands.Contains(name));

    /// <summary>
    /// Stage-3 parallel-run for MarkdownInlineWrappers lookup
    /// (\textbf / \textit / \emph / \underline).
    /// </summary>
    private bool IsMarkdownInlineWrapper(string name) =>
        CheckDrift(name, "command", "inline-markdown", MarkdownInlineWrappers.ContainsKey(name));

    /// <summary>
    /// Walks every parser HashSet and, for each member, asks the
    /// router what handler_kind the catalog claims. Returns the list
    /// of orphans — HashSet members with NO catalog row. Does not log.
    ///
    /// Pure function over the router state; safe to call from tests or
    /// from production-boot wrappers like <see cref="AuditCatalogAlignment"/>.
    /// </summary>
    public IReadOnlyList<string> FindCatalogOrphans()
    {
        if (_router is NullTokenRouter) return Array.Empty<string>();

        var orphans = new List<string>();

        void Check(string name, string kind, string expected)
        {
            var routerKind = _router.HandlerKindFor(name, kind);
            // Orphan = catalog has no row at all. A different non-null
            // handler_kind is multi-handler (see CheckDrift doc) and
            // not an orphan.
            if (routerKind is null)
                orphans.Add($"{kind}/'{name}' (expected {expected})");
        }

        foreach (var env in KnownEnvironments) Check(env, "environment", "known-structural");
        foreach (var env in TheoremEnvironments.Keys) Check(env, "environment", "theorem-like");
        foreach (var env in PassThroughEnvironments) Check(env, "environment", "pass-through");
        foreach (var cmd in PreservedInlineCommands) Check(cmd, "command", "inline-preserved");
        foreach (var cmd in CodeDisplayInlineCommands) Check(cmd, "command", "inline-code");
        foreach (var cmd in MarkdownInlineWrappers.Keys) Check(cmd, "command", "inline-markdown");

        return orphans;
    }

    /// <summary>
    /// Boot-time audit wrapper — calls <see cref="FindCatalogOrphans"/>
    /// and logs a single summary line at Information / Warning so
    /// operators see the alignment picture on each deploy.
    ///
    /// Meant to be called exactly once per process, after
    /// ILatexCatalogService.PreloadAsync has populated the cache.
    /// Safe to call multiple times; just repeats the log.
    /// </summary>
    public void AuditCatalogAlignment()
    {
        if (_router is NullTokenRouter) return;

        var orphans = FindCatalogOrphans();
        if (orphans.Count == 0)
        {
            _logger.LogInformation("[LatexParser] Catalog audit clean — all hardcoded HashSet members have a catalog row.");
        }
        else
        {
            _logger.LogWarning(
                "[LatexParser] Catalog audit found {Count} orphan(s) — HashSet members with no catalog row: {Orphans}",
                orphans.Count,
                string.Join(", ", orphans));
        }
    }

    /// <summary>
    /// Shared drift-detection helper. Compares a local HashSet verdict
    /// against the router's <c>handler_kind</c>. Returns the local
    /// verdict unchanged; the router is observed, never authoritative.
    ///
    /// Only two cases emit a Warning:
    /// <list type="bullet">
    ///   <item>Orphan in parser: HashSet says True, router says null
    ///   (catalog has no row for this token → catalog is lying by
    ///   omission).</item>
    ///   <item>Orphan in catalog: HashSet says False, router says the
    ///   exact expected kind (catalog claims we handle it, parser
    ///   disagrees).</item>
    /// </list>
    ///
    /// Multi-handler tokens (e.g. \cite — citation-regex primary,
    /// PreservedInlineCommands fallback) are NOT drift — the router
    /// returning a different non-null handler kind is consistent with
    /// the parser having several valid paths for the same token.
    /// </summary>
    private bool CheckDrift(string name, string kind, string expectedHandler, bool localVerdict)
    {
        if (_router is NullTokenRouter) return localVerdict;

        var routerKind = _router.HandlerKindFor(name, kind);
        var isRealDrift =
            (localVerdict && routerKind is null)                              // parser handles it, catalog doesn't
            || (!localVerdict && routerKind == expectedHandler);              // catalog claims this exact handler, parser doesn't
        if (isRealDrift)
        {
            _logger.LogWarning(
                "[TokenRouter drift] {Kind} '{Name}' hashset[{Expected}]={Local} router={RouterKind} (treating as {Decision})",
                kind, name, expectedHandler, localVerdict, routerKind ?? "<null>", localVerdict);
        }
        return localVerdict;
    }

    /// <summary>
    /// Theorem-style environments LaTeX papers use. Mapped to TheoremEnvironmentType.
    /// </summary>
    private static readonly Dictionary<string, TheoremEnvironmentType> TheoremEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["theorem"]     = TheoremEnvironmentType.Theorem,
        ["thm"]         = TheoremEnvironmentType.Theorem,
        ["lemma"]       = TheoremEnvironmentType.Lemma,
        ["proposition"] = TheoremEnvironmentType.Proposition,
        ["prop"]        = TheoremEnvironmentType.Proposition,
        ["corollary"]   = TheoremEnvironmentType.Corollary,
        ["cor"]         = TheoremEnvironmentType.Corollary,
        ["conjecture"]  = TheoremEnvironmentType.Conjecture,
        ["definition"]  = TheoremEnvironmentType.Definition,
        ["defn"]        = TheoremEnvironmentType.Definition,
        ["example"]     = TheoremEnvironmentType.Example,
        ["remark"]      = TheoremEnvironmentType.Remark,
        ["note"]        = TheoremEnvironmentType.Note,
        ["proof"]       = TheoremEnvironmentType.Proof,
        // NOTE: "algorithm" is handled by a dedicated parser branch that emits
        // an ImportAlgorithm element — NOT as a theorem. If you add it here
        // it gets stuffed into a theorem block with the algorithmic body as
        // raw text, which is how BG-XXX was produced.
        ["exercise"]    = TheoremEnvironmentType.Exercise,
        ["solution"]    = TheoremEnvironmentType.Solution,
        ["axiom"]       = TheoremEnvironmentType.Axiom,
    };

    /// <summary>
    /// Environments the parser handles in some way (so we don't warn on them).
    /// Compared case-insensitively.
    /// </summary>
    private static readonly HashSet<string> KnownEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        "document", "abstract", "thebibliography",
        "equation", "align", "gather", "multline", "eqnarray",
        "lstlisting", "verbatim", "minted",
        "figure", "subfigure", "table", "tabular",
        "itemize", "enumerate", "description",
        "quote", "quotation", "verse",
        "center", "flushleft", "flushright",
        "algorithm", "algorithm2e", "algorithmic",
    };

    /// <summary>
    /// Environments that add styling only (line spacing, alignment, font size)
    /// — they don't introduce structure. When we encounter them we want to
    /// drop the wrapper and parse the body as if the wrapper wasn't there,
    /// so \section inside \begin{spacing} still becomes a heading block.
    ///
    /// Without this, the catch-all unknown-env rule slurps the entire body
    /// into a single embed block — e.g. SPIE's \begin{spacing}{2}...
    /// \end{spacing} wrapped the whole paper in one block.
    /// </summary>
    private static readonly HashSet<string> PassThroughEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        "spacing", "singlespace", "doublespace", "onehalfspace",
        "center", "flushleft", "flushright", "raggedright", "raggedleft",
        "small", "footnotesize", "scriptsize", "tiny",
        "large", "Large", "LARGE", "huge", "Huge",
        "normalsize",
    };

    /// <summary>
    /// Match a LaTeX command of the form <c>\name{...}</c> with balanced braces.
    /// Returns the inner content and the full matched span (start..end exclusive).
    /// </summary>
    /// <remarks>
    /// LaTeX captions, titles, and many other commands routinely contain nested
    /// commands like <c>\caption{Loss over \textbf{1000} epochs}</c>, which a
    /// naive <c>\{[^}]+\}</c> regex truncates at the first inner brace. This
    /// walker handles arbitrarily nested braces and skips brace pairs preceded
    /// by a backslash escape.
    /// </remarks>
    private static (string Inner, int Start, int End)? MatchBalanced(string text, string commandName, int searchFrom = 0)
    {
        var token = "\\" + commandName;
        var idx = text.IndexOf(token, searchFrom, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var after = idx + token.Length;
            // Reject longer command names (e.g. \author when looking for \auth).
            if (after < text.Length && (char.IsLetter(text[after]) || text[after] == '*'))
            {
                idx = text.IndexOf(token, after, StringComparison.Ordinal);
                continue;
            }
            // Skip optional [ ... ] argument
            while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
            if (after < text.Length && text[after] == '[')
            {
                var bracketDepth = 1;
                after++;
                while (after < text.Length && bracketDepth > 0)
                {
                    if (text[after] == '[') bracketDepth++;
                    else if (text[after] == ']') bracketDepth--;
                    after++;
                }
            }
            while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
            if (after >= text.Length || text[after] != '{')
            {
                idx = text.IndexOf(token, after, StringComparison.Ordinal);
                continue;
            }
            // Walk balanced braces.
            var depth = 1;
            var contentStart = after + 1;
            var i = contentStart;
            while (i < text.Length && depth > 0)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    i += 2; // escaped char — skip both
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}') depth--;
                if (depth == 0) break;
                i++;
            }
            if (depth != 0) return null;
            var inner = text.Substring(contentStart, i - contentStart);
            return (inner, idx, i + 1);
        }
        return null;
    }

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<ImportDocument> ParseAsync(string filePath, LatexImportOptions? options = null)
    {
        options ??= LatexImportOptions.Default;

        if (!File.Exists(filePath))
            throw new FileNotFoundException("LaTeX file not found", filePath);

        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content, filePath, options);
    }

    /// <inheritdoc/>
    public Task<ImportDocument> ParseTextAsync(string latexContent, LatexImportOptions? options = null)
    {
        options ??= LatexImportOptions.Default;
        var document = Parse(latexContent, "input.tex", options);
        return Task.FromResult(document);
    }

    private ImportDocument Parse(string content, string sourcePath, LatexImportOptions options)
    {
        // For raw-text input ("input.tex" sentinel), default title to empty rather than the literal "input".
        var defaultTitle = sourcePath == "input.tex" ? string.Empty : Path.GetFileNameWithoutExtension(sourcePath);

        // Collapse \ifxetexorluatex / \ifxetex / \ifluatex / \ifpdftex
        // conditional preamble blocks to their pdflatex branch. Our compile
        // stack is pdflatex only — picking the else (or false) branch drops
        // fontspec/unicode-math loads that would otherwise abort compilation.
        content = StripXeLuaConditionals(content);

        // Batch A coverage — normalise well-known environment variants to
        // their kernel equivalents so the rest of the parser doesn't need
        // special cases for each. Cheap regex rewrites; the catalog rows
        // for these tokens get upgraded from partial/shimmed → full.
        content = NormaliseCoverageEnvironments(content);

        var document = new ImportDocument
        {
            SourcePath = sourcePath,
            Title = defaultTitle
        };

        // Extract document title from \title{} with balanced-brace matching so nested
        // commands like \title{Foo \LaTeX{} Bar} survive.
        if (options.ExtractDocumentTitle)
        {
            var titleMatch = MatchBalanced(content, "title");
            if (titleMatch.HasValue)
            {
                document.Title = StripInlineCommandsForPlainText(titleMatch.Value.Inner);
            }
        }

        // CV preamble personal info — captured before the main element walk
        // so a later personalInfo/avatar block can draw on it.
        ExtractCvPreambleMetadata(content, document);

        // Extract author if present
        var authorMatch = MatchBalanced(content, "author");
        if (authorMatch.HasValue)
        {
            document.Metadata.Author = StripInlineCommandsForPlainText(authorMatch.Value.Inner);
        }

        // Extract \date{} if present.
        var dateMatch = MatchBalanced(content, "date");
        if (dateMatch.HasValue)
        {
            document.Metadata.Date = StripInlineCommandsForPlainText(dateMatch.Value.Inner);
        }

        // Extract \documentclass[options]{class}
        var docClassMatch = Regex.Match(content, @"\\documentclass(?:\[([^\]]*)\])?\{([^}]+)\}");
        if (docClassMatch.Success)
        {
            document.Metadata.DocumentClass = docClassMatch.Groups[2].Value.Trim();
            if (docClassMatch.Groups[1].Success)
                document.Metadata.DocumentClassOptions = docClassMatch.Groups[1].Value.Trim();
        }

        // Extract all \usepackage[...]{name1,name2,...} references.
        foreach (Match pkgMatch in Regex.Matches(content, @"\\usepackage(?:\[([^\]]*)\])?\{([^}]+)\}"))
        {
            var pkgOptions = pkgMatch.Groups[1].Success ? pkgMatch.Groups[1].Value.Trim() : null;
            var names = pkgMatch.Groups[2].Value.Split(',');
            foreach (var rawName in names)
            {
                var name = rawName.Trim();
                if (name.Length == 0) continue;
                document.Metadata.Packages.Add(new LatexPackageReference { Name = name, Options = pkgOptions });
            }
        }

        // Extract bibliography style.
        var bibStyleMatch = Regex.Match(content, @"\\bibliographystyle\{([^}]+)\}");
        if (bibStyleMatch.Success)
        {
            document.Metadata.BibliographyStyle = bibStyleMatch.Groups[1].Value.Trim();
        }

        // Extract page-layout package metadata.
        // \usepackage[opts]{geometry} → record opts so margins/papersize survive round-trip.
        var geometryMatch = Regex.Match(content, @"\\usepackage\[([^\]]*)\]\{geometry\}");
        if (geometryMatch.Success)
        {
            document.Metadata.GeometryOptions = geometryMatch.Groups[1].Value.Trim();
        }
        // \geometry{margin=1in,...} (alternative form, sometimes used after \usepackage{geometry})
        var geometryAltMatch = Regex.Match(content, @"\\geometry\{([^}]+)\}");
        if (geometryAltMatch.Success && string.IsNullOrEmpty(document.Metadata.GeometryOptions))
        {
            document.Metadata.GeometryOptions = geometryAltMatch.Groups[1].Value.Trim();
        }

        // \usepackage{titlesec} (no options needed — the customizations live in \titleformat
        // and \titlespacing commands which we don't currently round-trip).
        if (Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{titlesec\}"))
        {
            document.Metadata.UsesTitlesec = true;
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Package 'titlesec' loaded — custom section formatting commands (\\titleformat, \\titlespacing) are recorded but not applied at render time. Sections will use Lilia's defaults.",
            });
        }

        // Extract line-spacing setting from the setspace package.
        // We look in both the preamble and the document body.
        if (Regex.IsMatch(content, @"\\doublespacing\b"))
            document.Metadata.LineSpacing = "double";
        else if (Regex.IsMatch(content, @"\\onehalfspacing\b"))
            document.Metadata.LineSpacing = "onehalf";
        else if (Regex.IsMatch(content, @"\\singlespacing\b"))
            document.Metadata.LineSpacing = "single";
        else
        {
            var setStretchMatch = Regex.Match(content, @"\\setstretch\{([^}]+)\}");
            if (setStretchMatch.Success)
                document.Metadata.LineSpacing = setStretchMatch.Groups[1].Value.Trim();
        }

        // Extract fancyhdr usage — record the raw setup commands for round-trip.
        if (Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{fancyhdr\}") ||
            Regex.IsMatch(content, @"\\pagestyle\{fancy\}"))
        {
            document.Metadata.UsesFancyhdr = true;
            // Collect all fancyhdr setup lines as a passthrough block.
            var fancyLines = Regex.Matches(content,
                @"\\(?:pagestyle|fancyhf|fancyhead|fancyfoot|renewcommand\s*\{\\headrulewidth\}|renewcommand\s*\{\\footrulewidth\})[^\n]*");
            if (fancyLines.Count > 0)
                document.Metadata.FancyhdrSource = string.Join("\n", fancyLines.Select(m => m.Value.Trim()));
        }

        // ── Compatibility-trap warnings ────────────────────────────────
        // These don't fail compilation but produce silent rendering bugs
        // (wrong refs, wrong captions, wrong quotes, wrong fonts). We warn so
        // users know where to look when their imported doc renders oddly.
        DetectLatexCompatTraps(content, document);

        // \usepackage[lang]{babel} or \usepackage[lang]{polyglossia}
        // The last language in the option list is conventionally the primary one.
        var babelMatch = Regex.Match(content, @"\\usepackage\[([^\]]*)\]\{(?:babel|polyglossia)\}");
        if (babelMatch.Success)
        {
            var langs = babelMatch.Groups[1].Value.Split(',').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (langs.Count > 0)
            {
                document.Metadata.Language = langs[^1]; // last = primary by convention
            }
        }
        // polyglossia also supports \setdefaultlanguage{french}
        var polyMatch = Regex.Match(content, @"\\setdefaultlanguage\{([^}]+)\}");
        if (polyMatch.Success && string.IsNullOrEmpty(document.Metadata.Language))
        {
            document.Metadata.Language = polyMatch.Groups[1].Value.Trim();
        }

        // Warn about packages the editor can't fully emulate at render time.
        // These produce valid documents but visual output will differ from a local pdflatex run.
        var knownLimitedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tikz", "pgfplots", "circuitikz", "pst-plot",
            "algorithmic", "algorithm2e",
            "biblatex", // we handle thebibliography but not biblatex's \printbibliography
            "listings",  // we parse lstlisting bodies but syntax-highlighting options are ignored
        };
        foreach (var pkg in document.Metadata.Packages)
        {
            if (knownLimitedPackages.Contains(pkg.Name))
            {
                document.Warnings.Add(new ImportWarning
                {
                    Type = ImportWarningType.UnsupportedElement,
                    Message = $"Package '{pkg.Name}' has limited editor support — import will preserve content but rendering may differ from pdflatex output.",
                });
            }
        }
        if (!string.IsNullOrEmpty(document.Metadata.DocumentClass)
            && string.Equals(document.Metadata.DocumentClass, "beamer", StringComparison.OrdinalIgnoreCase))
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Beamer presentations are not fully supported — frames will import as sections and overlays will be flattened.",
            });
        }

        // Extract content between \begin{document} and \end{document} if requested
        var documentContent = content;
        if (options.OnlyDocumentContent)
        {
            var documentMatch = Regex.Match(content, @"\\begin\{document\}([\s\S]*?)\\end\{document\}", RegexOptions.Singleline);
            if (documentMatch.Success)
            {
                documentContent = documentMatch.Groups[1].Value;
            }
        }

        // P0-8: strip preamble/metadata commands from the body so they don't leak into
        // the first paragraph block. Done AFTER the \begin{document} extraction so we
        // also catch papers that put \title/\author inside the document body.
        documentContent = StripBalancedCommand(documentContent, "title");
        documentContent = StripBalancedCommand(documentContent, "author");
        documentContent = StripBalancedCommand(documentContent, "date");
        documentContent = StripBalancedCommand(documentContent, "thanks");
        documentContent = StripBalancedCommand(documentContent, "affil");
        documentContent = StripBalancedCommand(documentContent, "affiliation");

        // Also strip preamble commands that the metadata pass already captured so
        // they don't get dumped into the body as raw text.
        documentContent = Regex.Replace(documentContent, @"\\documentclass(?:\[[^\]]*\])?\{[^}]+\}", "");
        documentContent = Regex.Replace(documentContent, @"\\usepackage(?:\[[^\]]*\])?\{[^}]+\}", "");
        documentContent = Regex.Replace(documentContent, @"\\bibliographystyle\{[^}]+\}", "");
        documentContent = Regex.Replace(documentContent, @"\\bibliography\{[^}]+\}", "");

        // Remove document setup commands
        documentContent = Regex.Replace(documentContent, @"\\maketitle\b", "");
        documentContent = Regex.Replace(documentContent, @"\\tableofcontents\b", "");
        documentContent = Regex.Replace(documentContent, @"\\newpage\b", "");
        documentContent = Regex.Replace(documentContent, @"\\clearpage\b", "");

        // Parse the content
        ParseContent(documentContent, document, options);

        // Walk all text-bearing elements and harvest citation keys + reference labels.
        // The editor uses these to validate "you cited X but it's not in your bibliography"
        // and "you referenced label Y but it doesn't exist anywhere in the document".
        ExtractInlineReferences(document);

        return document;
    }

    /// <summary>
    /// Detect LaTeX package combinations that produce silent rendering bugs
    /// (not compile errors). Based on well-documented community "traps":
    /// load-order rules for hyperref/cleveref/varioref, mutually incompatible
    /// subfigure families, algorithm package conflicts, csquotes+babel ordering.
    /// </summary>
    private static void DetectLatexCompatTraps(string content, ImportDocument document)
    {
        // Trap 1: cleveref must load AFTER hyperref.
        // Walk the raw preamble looking for the two \usepackage lines and compare positions.
        var hyperrefMatch = Regex.Match(content, @"\\usepackage(?:\[[^\]]*\])?\{hyperref\}");
        var cleverefMatch = Regex.Match(content, @"\\usepackage(?:\[[^\]]*\])?\{cleveref\}");
        if (hyperrefMatch.Success && cleverefMatch.Success && cleverefMatch.Index < hyperrefMatch.Index)
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Load-order trap: \\usepackage{cleveref} appears BEFORE \\usepackage{hyperref}. cleveref must be loaded AFTER hyperref or smart cross-references (\\cref, \\Cref) will silently fall back to plain \\ref text.",
            });
        }

        // Trap 2: subfig / subfigure / subcaption are mutually incompatible.
        var hasSubfig = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{subfig\}");
        var hasSubfigure = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{subfigure\}");
        var hasSubcaption = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{subcaption\}");
        var subfigCount = (hasSubfig ? 1 : 0) + (hasSubfigure ? 1 : 0) + (hasSubcaption ? 1 : 0);
        if (subfigCount > 1)
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Load-order trap: multiple sub-figure packages loaded (subfig/subfigure/subcaption). These are mutually incompatible — pick one. Lilia bundles subcaption by default.",
            });
        }

        // Trap 3: algorithm2e + algorithmic together — they compete over the algorithm float.
        var hasAlg2e = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{algorithm2e\}");
        var hasAlgorithmic = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{algorithmic\}");
        var hasAlgpseudocode = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{algpseudocode\}");
        if (hasAlg2e && (hasAlgorithmic || hasAlgpseudocode))
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Load-order trap: algorithm2e and algorithmic/algpseudocode both loaded — they define incompatible algorithm environments. Algorithms will render inconsistently.",
            });
        }
        if (hasAlgorithmic && hasAlgpseudocode)
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Load-order trap: both algorithmic and algpseudocode loaded — they both define \\begin{algorithmic} with different syntax. Pick one.",
            });
        }

        // Trap 4: csquotes must load AFTER babel/polyglossia to pick up language-specific quotes.
        var babelMatch2 = Regex.Match(content, @"\\usepackage(?:\[[^\]]*\])?\{(?:babel|polyglossia)\}");
        var csquotesMatch = Regex.Match(content, @"\\usepackage(?:\[[^\]]*\])?\{csquotes\}");
        if (babelMatch2.Success && csquotesMatch.Success && csquotesMatch.Index < babelMatch2.Index)
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Load-order trap: \\usepackage{csquotes} appears BEFORE babel/polyglossia. csquotes must load AFTER the language package or it will silently use English-style quotes even in French/German/Spanish documents.",
            });
        }

        // Trap 5: varioref must load BEFORE hyperref (the reverse of cleveref).
        var variorefMatch = Regex.Match(content, @"\\usepackage(?:\[[^\]]*\])?\{varioref\}");
        if (hyperrefMatch.Success && variorefMatch.Success && variorefMatch.Index > hyperrefMatch.Index)
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Load-order trap: varioref should load BEFORE hyperref. \"On the following page\"-style cross-references may show wrong text.",
            });
        }

        // Trap 6: fontspec is for XeLaTeX/LuaLaTeX only; warns if used with pdflatex's inputenc.
        var hasFontspec = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{fontspec\}");
        var hasInputenc = Regex.IsMatch(content, @"\\usepackage(?:\[[^\]]*\])?\{inputenc\}");
        if (hasFontspec && hasInputenc)
        {
            document.Warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.UnsupportedElement,
                Message = "Engine trap: both fontspec (XeLaTeX/LuaLaTeX only) and inputenc (pdflatex only) are loaded. Pick one engine family — Lilia exports via pdflatex so fontspec font selections will be ignored.",
            });
        }
    }

    /// <summary>
    /// Walks paragraph / theorem / abstract / blockquote / list element text and records
    /// every \cite{}/\citep{}/\citet{}/\parencite{}/\textcite{} key and every
    /// \ref{}/\eqref{}/\cref{}/\Cref{}/\autoref{} label into <see cref="ImportMetadata"/>.
    /// </summary>
    private static void ExtractInlineReferences(ImportDocument document)
    {
        // Citation commands the natbib + biblatex ecosystems use. Captured key list is
        // comma-separated inside a single \cite{}: e.g. \cite{a,b,c}.
        var citePattern = new Regex(@"\\(?:cite|citep|citet|citealp|citealt|parencite|textcite|footcite|autocite|nocite)\*?(?:\[[^\]]*\])?(?:\[[^\]]*\])?\{([^}]+)\}");
        var refPattern = new Regex(@"\\(?:ref|eqref|cref|Cref|autoref|pageref|nameref)\{([^}]+)\}");

        var citedKeys = new HashSet<string>(StringComparer.Ordinal);
        var referencedLabels = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in document.Elements)
        {
            string? text = element switch
            {
                ImportParagraph p     => p.Text,
                ImportHeading h       => h.Text,
                ImportTheorem th      => th.Text,
                ImportAbstract ab     => ab.Text,
                ImportBlockquote bq   => bq.Text,
                ImportListItem li     => li.Text,
                ImportBibliographyEntry be => be.Text,
                _ => null,
            };
            if (string.IsNullOrEmpty(text)) continue;

            foreach (Match m in citePattern.Matches(text))
            {
                foreach (var key in m.Groups[1].Value.Split(','))
                {
                    var trimmed = key.Trim();
                    if (trimmed.Length > 0) citedKeys.Add(trimmed);
                }
            }
            foreach (Match m in refPattern.Matches(text))
            {
                var label = m.Groups[1].Value.Trim();
                if (label.Length > 0) referencedLabels.Add(label);
            }
        }

        document.Metadata.CitedKeys = citedKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        document.Metadata.ReferencedLabels = referencedLabels.OrderBy(l => l, StringComparer.Ordinal).ToList();
    }

    /// <summary>Remove every occurrence of <c>\name{...}</c> from <paramref name="text"/> using balanced braces.</summary>
    private static string StripBalancedCommand(string text, string commandName)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var cursor = 0;
        while (cursor < text.Length)
        {
            var match = MatchBalanced(text, commandName, cursor);
            if (!match.HasValue)
            {
                sb.Append(text, cursor, text.Length - cursor);
                break;
            }
            sb.Append(text, cursor, match.Value.Start - cursor);
            cursor = match.Value.End;
        }
        return sb.ToString();
    }

    /// <summary>Strip simple inline LaTeX commands (\textbf{x} → x) so a string is safe to use as plain text.</summary>
    private static string StripInlineCommandsForPlainText(string text)
    {
        text = Regex.Replace(text, @"\\(?:textbf|textit|emph|texttt|textsc|textrm|textsf|underline)\{([^{}]*)\}", "$1");
        text = Regex.Replace(text, @"\\LaTeX\{?\}?", "LaTeX");
        text = Regex.Replace(text, @"\\TeX\{?\}?", "TeX");
        text = text.Replace("~", " ").Replace("\\,", " ");
        return text.Trim();
    }

    // Commands we preserve verbatim — they have semantic meaning the editor
    // handles (citations, refs, hyperlinks, footnotes, math, labels).
    private static readonly HashSet<string> PreservedInlineCommands = new(StringComparer.Ordinal)
    {
        "cite", "citep", "citet", "citeauthor", "citeyear", "nocite",
        "ref", "pageref", "eqref", "autoref", "cref", "Cref",
        "label", "href", "url", "hyperref",
        "footnote", "footnotemark", "footnotetext",
        "input", "include",
    };

    // Inline commands that render their argument as monospace code. We wrap
    // the argument in Markdown backticks so LaTeX-about-LaTeX docs keep the
    // visual hint ("\inlinecode{\section}" → "`\section`").
    private static readonly HashSet<string> CodeDisplayInlineCommands = new(StringComparer.Ordinal)
    {
        "texttt", "inlinecode", "code", "cmdname", "macroname", "pkgname",
        "filename", "path", "envname", "lstinline", "mintinline",
    };

    // Commands that map to bold / italic markdown so the styling isn't lost.
    private static readonly Dictionary<string, (string Open, string Close)> MarkdownInlineWrappers = new(StringComparer.Ordinal)
    {
        ["textbf"] = ("**", "**"),
        ["textit"] = ("*", "*"),
        ["emph"]   = ("*", "*"),
        ["underline"] = ("__", "__"),
        // textsc / textrm / textsf — keep plain (Markdown has no small-caps / font-family inline).
    };

    /// <summary>
    /// Normalise LaTeX inline commands inside a paragraph so no raw \cmd leaks
    /// into the block text stored in the DB. Rules:
    ///   - Code-display commands (\texttt, \inlinecode, \lstinline, \verb|...|,
    ///     and other name-heuristic matches) → backtick-wrapped inline code.
    ///   - Bold/italic/underline wrappers → Markdown equivalents.
    ///   - Commands in PreservedInlineCommands (\cite, \ref, \href, …) stay
    ///     verbatim for downstream renderers.
    ///   - Any OTHER \cmd{arg} (unknown user macros) → just the arg.
    /// Runs iteratively so nested wrappers collapse (\textbf{\textit{X}} → ***X***).
    /// </summary>
    private string NormaliseInlineCommands(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Normalise escaped TeX marks first.
        text = Regex.Replace(text, @"\\LaTeX\{?\}?", "LaTeX");
        text = Regex.Replace(text, @"\\TeX\{?\}?", "TeX");

        // Handle \verb<delim>...<delim> — the delimiter is a single non-letter
        // char (typically | or +). LaTeX-about-LaTeX docs rely heavily on this.
        text = Regex.Replace(
            text,
            @"\\verb\*?([^a-zA-Z\s])(.*?)\1",
            m => "`" + m.Groups[2].Value + "`",
            RegexOptions.Singleline);

        // Iteratively process `\cmd{arg}` wrappers. Heuristic for "looks like
        // a code-display command" catches user macros we don't know by name
        // (e.g. \cmdref, \codeexample) — anything ending in "code" / "cmd" /
        // "name" with a short arg.
        var pattern = new Regex(@"\\([a-zA-Z]+)(?:\[[^\]]*\])?\{([^{}]*)\}");
        string prev;
        var guard = 0;
        do
        {
            prev = text;
            text = pattern.Replace(text, m =>
            {
                var cmd = m.Groups[1].Value;
                var arg = m.Groups[2].Value;
                if (IsPreservedInlineCommand(cmd)) return m.Value;
                if (IsCodeDisplayInlineCommand(cmd) || LooksLikeCodeCommand(cmd))
                    return "`" + arg + "`";
                if (IsMarkdownInlineWrapper(cmd) && MarkdownInlineWrappers.TryGetValue(cmd, out var wrap))
                    return wrap.Open + arg + wrap.Close;
                return arg;
            });
            guard++;
        } while (prev != text && guard < 10);

        // Common LaTeX typography artefacts.
        text = text.Replace("~", " ").Replace("\\,", " ").Replace("\\ ", " ");
        // Collapse double spaces introduced by the stripping.
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return text;
    }

    private static bool LooksLikeCodeCommand(string cmd)
    {
        // Heuristic: unknown macros whose name hints at code/command display.
        // Keeps false-positive risk low (only triggers when the user clearly
        // intended the macro to render monospace).
        return cmd.EndsWith("code", StringComparison.OrdinalIgnoreCase)
            || cmd.EndsWith("cmd", StringComparison.OrdinalIgnoreCase)
            || cmd.EndsWith("command", StringComparison.OrdinalIgnoreCase)
            || cmd.EndsWith("macro", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("lstinline", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("mintinline", StringComparison.OrdinalIgnoreCase);
    }

    private void ParseContent(string content, ImportDocument document, LatexImportOptions options)
    {
        var elementOrder = 0;
        var remaining = content;

        while (!string.IsNullOrWhiteSpace(remaining))
        {
            // Find the next structural element
            var matches = new List<(Match match, string type)>();

            // Sections — \section / \subsection / \subsubsection / \paragraph / \subparagraph (P1-1)
            var sectionMatch = Regex.Match(remaining, @"\\(section|subsection|subsubsection|paragraph|subparagraph)\*?\{([^}]+)\}");
            if (sectionMatch.Success)
                matches.Add((sectionMatch, "section"));

            // Equation environments
            if (options.ConvertEquationEnvironments)
            {
                var eqEnvMatch = Regex.Match(remaining, @"\\begin\{(equation|align|gather|multline)\*?\}([\s\S]*?)\\end\{\1\*?\}", RegexOptions.Singleline);
                if (eqEnvMatch.Success)
                    matches.Add((eqEnvMatch, "equation_env"));
            }

            // Display math $$...$$ and \[...\]
            if (options.ConvertDisplayMath)
            {
                var displayMathMatch = Regex.Match(remaining, @"\$\$([\s\S]*?)\$\$", RegexOptions.Singleline);
                if (displayMathMatch.Success)
                    matches.Add((displayMathMatch, "displaymath_dollar"));

                var bracketMathMatch = Regex.Match(remaining, @"\\\[([\s\S]*?)\\\]", RegexOptions.Singleline);
                if (bracketMathMatch.Success)
                    matches.Add((bracketMathMatch, "displaymath_bracket"));
            }

            // Code environments — capture the optional [args] / {args} so language extraction works
            if (options.ConvertCodeEnvironments)
            {
                var codeMatch = Regex.Match(remaining, @"\\begin\{(lstlisting|verbatim|minted)\}(\[[^\]]*\])?(\{[^}]*\})?([\s\S]*?)\\end\{\1\}", RegexOptions.Singleline);
                if (codeMatch.Success)
                    matches.Add((codeMatch, "code"));
            }

            // Figure environments — match both \begin{figure} and \begin{figure*}.
            // The starred form spans both columns in a 2-col layout; we capture
            // that via the `span` attribute on the emitted figure block.
            if (options.PreserveFigures)
            {
                var figureMatch = Regex.Match(remaining, @"\\begin\{figure(\*?)\}(?:\[[^\]]*\])?([\s\S]*?)\\end\{figure\1\}", RegexOptions.Singleline);
                if (figureMatch.Success)
                    matches.Add((figureMatch, figureMatch.Groups[1].Value == "*" ? "figure*" : "figure"));
            }

            // Table environments — same starred-variant handling as figures.
            if (options.ConvertTables)
            {
                var tableMatch = Regex.Match(remaining, @"\\begin\{table(\*?)\}(?:\[[^\]]*\])?([\s\S]*?)\\end\{table\1\}", RegexOptions.Singleline);
                if (tableMatch.Success)
                    matches.Add((tableMatch, tableMatch.Groups[1].Value == "*" ? "table*" : "table"));
            }

            // Lists — itemize / enumerate / description (P0-1)
            var listMatch = Regex.Match(remaining, @"\\begin\{(itemize|enumerate|description)\}([\s\S]*?)\\end\{\1\}", RegexOptions.Singleline);
            if (listMatch.Success)
                matches.Add((listMatch, "list"));

            // Theorem-style environments (P0-6) — we accept any of the names in TheoremEnvironments
            var theoremPattern = @"\\begin\{(" + string.Join("|", TheoremEnvironments.Keys) + @")\}(\[[^\]]*\])?([\s\S]*?)\\end\{\1\}";
            var theoremMatch = Regex.Match(remaining, theoremPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (theoremMatch.Success)
                matches.Add((theoremMatch, "theorem"));

            // Algorithm float: \begin{algorithm}[H] \caption{..} \label{..}
            //   \begin{algorithmic}[1] ... \end{algorithmic} \end{algorithm}
            // Must be matched before the catch-all env and AFTER any theorem
            // match resolution so it doesn't collide.
            var algorithmMatch = Regex.Match(remaining, @"\\begin\{algorithm\}(?:\[[^\]]*\])?([\s\S]*?)\\end\{algorithm\}", RegexOptions.Singleline);
            if (algorithmMatch.Success)
                matches.Add((algorithmMatch, "algorithm"));

            // Abstract environment (P0-7)
            var abstractMatch = Regex.Match(remaining, @"\\begin\{abstract\}([\s\S]*?)\\end\{abstract\}", RegexOptions.Singleline);
            if (abstractMatch.Success)
                matches.Add((abstractMatch, "abstract"));

            // Bibliography environment (P0-9)
            var bibMatch = Regex.Match(remaining, @"\\begin\{thebibliography\}(?:\{[^}]*\})?([\s\S]*?)\\end\{thebibliography\}", RegexOptions.Singleline);
            if (bibMatch.Success)
                matches.Add((bibMatch, "bibliography"));

            // Blockquote environments
            var quoteMatch = Regex.Match(remaining, @"\\begin\{(quote|quotation|verse)\}([\s\S]*?)\\end\{\1\}", RegexOptions.Singleline);
            if (quoteMatch.Success)
                matches.Add((quoteMatch, "blockquote"));

            // Pass-through environments (spacing, center, flushleft, size/weight).
            // They add styling only, no structure — drop the wrapper and splice
            // the body back into the stream so \section inside \begin{spacing}
            // still becomes a heading block instead of being slurped into embed.
            var passThroughMatch = Regex.Match(
                remaining,
                @"\\begin\{([A-Za-z]+)\}(?:\{[^}]*\})?(?:\[[^\]]*\])?([\s\S]*?)\\end\{\1\}",
                RegexOptions.Singleline);
            if (passThroughMatch.Success && IsPassThroughEnvironment(passThroughMatch.Groups[1].Value))
            {
                var before = remaining[..passThroughMatch.Index];
                var body = passThroughMatch.Groups[2].Value;
                var after = remaining[(passThroughMatch.Index + passThroughMatch.Length)..];
                remaining = before + body + after;
                continue; // restart the match search against the rewritten stream
            }

            // Catch-all: any \begin{X}…\end{X} not handled above (P1-6).
            // Is*Environment helpers run the Stage-3 parallel check —
            // same behaviour, plus drift logging when the catalog's
            // handler_kind disagrees with the HashSet.
            var unknownEnvMatch = Regex.Match(remaining, @"\\begin\{([A-Za-z*]+)\}(?:\[[^\]]*\])?(?:\{[^}]*\})?([\s\S]*?)\\end\{\1\}", RegexOptions.Singleline);
            if (unknownEnvMatch.Success && !IsKnownEnvironment(unknownEnvMatch.Groups[1].Value) && !IsTheoremEnvironment(unknownEnvMatch.Groups[1].Value))
                matches.Add((unknownEnvMatch, "unknown_env"));

            // Find the first match
            if (matches.Count == 0)
            {
                // No more special elements - add remaining as paragraphs
                AddParagraphs(remaining, document, ref elementOrder);
                break;
            }

            var firstMatch = matches.OrderBy(m => m.match.Index).First();

            // Add text before the match as paragraphs
            if (firstMatch.match.Index > 0)
            {
                var textBefore = remaining[..firstMatch.match.Index];
                AddParagraphs(textBefore, document, ref elementOrder);
            }

            // Handle the matched element
            switch (firstMatch.type)
            {
                case "section":
                    var sectionType = firstMatch.match.Groups[1].Value;
                    var sectionTitle = firstMatch.match.Groups[2].Value;
                    var level = sectionType switch
                    {
                        "section"       => 1,
                        "subsection"    => 2,
                        "subsubsection" => 3,
                        "paragraph"     => 4,
                        "subparagraph"  => 5,
                        _ => 1
                    };

                    if (level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection)
                    {
                        document.Elements.Add(new ImportHeading
                        {
                            Order = elementOrder++,
                            Level = level,
                            Text = sectionTitle
                        });
                    }
                    else
                    {
                        document.Elements.Add(new ImportParagraph
                        {
                            Order = elementOrder++,
                            Text = sectionTitle,
                            Style = ParagraphStyle.Title,
                            Formatting = [new FormattingSpan { Start = 0, Length = sectionTitle.Length, Type = FormattingType.Bold }]
                        });
                    }
                    break;

                case "equation_env":
                    {
                        var envName = firstMatch.match.Groups[1].Value; // equation/align/gather/multline
                        var rawLatex = firstMatch.match.Groups[2].Value.Trim();
                        // P1-4: lift \label{...} out of the equation body so the editor can store it as block metadata.
                        var labelExtract = Regex.Match(rawLatex, @"\\label\{([^}]+)\}");
                        if (labelExtract.Success)
                        {
                            rawLatex = rawLatex.Replace(labelExtract.Value, "").Trim();
                        }
                        // P1-3: re-wrap align/gather/multline so KaTeX can render the alignment markers.
                        var wrappedLatex = envName == "equation"
                            ? rawLatex
                            : $"\\begin{{{envName}}}\n{rawLatex}\n\\end{{{envName}}}";
                        document.Elements.Add(new ImportEquation
                        {
                            Order = elementOrder++,
                            LatexContent = wrappedLatex,
                            ConversionSucceeded = true,
                            IsInline = false
                        });
                    }
                    break;

                case "displaymath_dollar":
                case "displaymath_bracket":
                    document.Elements.Add(new ImportEquation
                    {
                        Order = elementOrder++,
                        LatexContent = firstMatch.match.Groups[1].Value.Trim(),
                        ConversionSucceeded = true,
                        IsInline = false
                    });
                    break;

                case "code":
                    {
                        // Groups: 1=env name, 2=optional [bracket arg], 3=optional {brace arg}, 4=body
                        var envName = firstMatch.match.Groups[1].Value;
                        var bracketArg = firstMatch.match.Groups[2].Success ? firstMatch.match.Groups[2].Value : "";
                        var braceArg = firstMatch.match.Groups[3].Success ? firstMatch.match.Groups[3].Value : "";
                        var body = firstMatch.match.Groups[4].Value.Trim();

                        // P0-5: extract language. lstlisting uses [language=X]; minted uses {lang}; verbatim has none.
                        string? lang = null;
                        if (envName == "lstlisting" && bracketArg.Length > 0)
                        {
                            var langMatch = Regex.Match(bracketArg, @"language\s*=\s*([A-Za-z0-9+#\-]+)", RegexOptions.IgnoreCase);
                            if (langMatch.Success) lang = langMatch.Groups[1].Value.ToLowerInvariant();
                        }
                        else if (envName == "minted" && braceArg.Length > 0)
                        {
                            var inner = braceArg.Trim('{', '}').Trim();
                            if (inner.Length > 0) lang = inner.ToLowerInvariant();
                        }

                        document.Elements.Add(new ImportCodeBlock
                        {
                            Order = elementOrder++,
                            Text = body,
                            Language = lang,
                            DetectionReason = CodeBlockDetectionReason.StyleName
                        });
                    }
                    break;

                case "figure":
                case "figure*":
                    {
                        // Extract includegraphics filename and caption (with balanced-brace caption walker).
                        // With starred/regular handling the content is now group 2; group 1 is the "*" flag.
                        var figContent = firstMatch.match.Groups[2].Value;
                        var graphicsMatch = Regex.Match(figContent, @"\\includegraphics(?:\[[^\]]*\])?\{([^}]+)\}");
                        var captionInner = MatchBalanced(figContent, "caption");

                        document.Elements.Add(new ImportImage
                        {
                            Order = elementOrder++,
                            Filename = graphicsMatch.Success ? graphicsMatch.Groups[1].Value : null,
                            AltText = captionInner.HasValue ? StripInlineCommandsForPlainText(captionInner.Value.Inner) : null,
                            Span = firstMatch.type == "figure*" ? "page" : "column",
                            Data = []
                        });
                    }
                    break;

                case "table":
                case "table*":
                    var tableContent = firstMatch.match.Groups[2].Value;
                    var tabularMatch = Regex.Match(tableContent, @"\\begin\{tabular\}\{([^}]*)\}([\s\S]*?)\\end\{tabular\}", RegexOptions.Singleline);

                    if (tabularMatch.Success)
                    {
                        var table = ParseTabular(tabularMatch.Groups[2].Value);
                        table.Order = elementOrder++;
                        table.Span = firstMatch.type == "table*" ? "page" : "column";
                        document.Elements.Add(table);
                    }
                    else
                    {
                        // Store raw table content
                        document.Elements.Add(new ImportParagraph
                        {
                            Order = elementOrder++,
                            Text = tableContent.Trim(),
                            Style = ParagraphStyle.Normal
                        });
                        document.Warnings.Add(new ImportWarning
                        {
                            Type = ImportWarningType.UnsupportedElement,
                            Message = "Could not parse table structure"
                        });
                    }
                    break;

                case "list":
                    {
                        var listKind = firstMatch.match.Groups[1].Value; // itemize | enumerate | description
                        var body = firstMatch.match.Groups[2].Value;
                        var isNumbered = listKind == "enumerate";
                        // Split on \item — first split is preamble (whitespace) and is dropped.
                        var items = Regex.Split(body, @"\\item\b").Skip(1);
                        foreach (var raw in items)
                        {
                            var itemText = raw.Trim();
                            // \item[label] (description list) — strip the optional [label].
                            var optMatch = Regex.Match(itemText, @"^\[([^\]]*)\]\s*");
                            string? marker = null;
                            if (optMatch.Success)
                            {
                                marker = optMatch.Groups[1].Value;
                                itemText = itemText[optMatch.Length..];
                            }
                            if (string.IsNullOrWhiteSpace(itemText)) continue;
                            document.Elements.Add(new ImportListItem
                            {
                                Order = elementOrder++,
                                Text = itemText,
                                IsNumbered = isNumbered,
                                ListMarker = marker,
                                Formatting = ParseLatexFormatting(itemText),
                            });
                        }
                    }
                    break;

                case "theorem":
                    {
                        var envName = firstMatch.match.Groups[1].Value;
                        var optTitle = firstMatch.match.Groups[2].Success
                            ? firstMatch.match.Groups[2].Value.Trim('[', ']')
                            : null;
                        var body = firstMatch.match.Groups[3].Value.Trim();
                        var labelExtract = Regex.Match(body, @"\\label\{([^}]+)\}");
                        string? label = null;
                        if (labelExtract.Success)
                        {
                            label = labelExtract.Groups[1].Value;
                            body = body.Replace(labelExtract.Value, "").Trim();
                        }
                        document.Elements.Add(new ImportTheorem
                        {
                            Order = elementOrder++,
                            EnvironmentType = TheoremEnvironments[envName],
                            Title = optTitle,
                            Label = label,
                            Text = body,
                            Formatting = ParseLatexFormatting(body),
                            DetectionReason = TheoremDetectionReason.StyleName,
                        });
                    }
                    break;

                case "abstract":
                    document.Elements.Add(new ImportAbstract
                    {
                        Order = elementOrder++,
                        Text = firstMatch.match.Groups[1].Value.Trim(),
                        Formatting = ParseLatexFormatting(firstMatch.match.Groups[1].Value.Trim()),
                    });
                    break;

                case "algorithm":
                    {
                        var algoBody = firstMatch.match.Groups[1].Value;
                        var capMatch = Regex.Match(algoBody, @"\\caption\{([^}]+)\}");
                        var lblMatch = Regex.Match(algoBody, @"\\label\{([^}]+)\}");
                        var algorithmicMatch = Regex.Match(
                            algoBody,
                            @"\\begin\{algorithmic\}(\[[^\]]*\])?([\s\S]*?)\\end\{algorithmic\}",
                            RegexOptions.Singleline);
                        var code = algorithmicMatch.Success
                            ? algorithmicMatch.Groups[2].Value.Trim()
                            : algoBody.Trim();
                        var lineNumbers = algorithmicMatch.Success
                            && algorithmicMatch.Groups[1].Value.Contains("1", StringComparison.Ordinal);
                        document.Elements.Add(new ImportAlgorithm
                        {
                            Order = elementOrder++,
                            Caption = capMatch.Success ? capMatch.Groups[1].Value.Trim() : null,
                            Label = lblMatch.Success ? lblMatch.Groups[1].Value.Trim() : null,
                            Code = code,
                            Lines = ParseAlgorithmicLines(code),
                            LineNumbers = lineNumbers,
                        });
                    }
                    break;

                case "bibliography":
                    {
                        var bibBody = firstMatch.match.Groups[1].Value;
                        // Each entry: \bibitem{key} text...   (until next \bibitem or end)
                        var entries = Regex.Matches(bibBody, @"\\bibitem(?:\[[^\]]*\])?\{([^}]+)\}([\s\S]*?)(?=\\bibitem|\z)");
                        foreach (Match e in entries)
                        {
                            var key = e.Groups[1].Value.Trim();
                            var text = e.Groups[2].Value.Trim();
                            document.Elements.Add(new ImportBibliographyEntry
                            {
                                Order = elementOrder++,
                                ReferenceLabel = key,
                                Text = text,
                                Formatting = ParseLatexFormatting(text),
                                DetectionReason = BibliographyDetectionReason.SectionContext,
                            });
                        }
                    }
                    break;

                case "blockquote":
                    document.Elements.Add(new ImportBlockquote
                    {
                        Order = elementOrder++,
                        Text = firstMatch.match.Groups[2].Value.Trim(),
                        Formatting = ParseLatexFormatting(firstMatch.match.Groups[2].Value.Trim()),
                        DetectionReason = BlockquoteDetectionReason.StyleName,
                    });
                    break;

                case "unknown_env":
                    {
                        // Preserve the entire \begin{X}...\end{X} as a raw LaTeX passthrough
                        // so users don't lose data on import. Editors render this through
                        // the embed block (which is just a verbatim LaTeX escape hatch).
                        var envName = firstMatch.match.Groups[1].Value;
                        var rawLatex = firstMatch.match.Value;
                        document.Elements.Add(new ImportLatexPassthrough
                        {
                            Order = elementOrder++,
                            LatexCode = rawLatex,
                            Description = $"Raw LaTeX environment: {envName}",
                        });
                        document.Warnings.Add(new ImportWarning
                        {
                            Type = ImportWarningType.UnsupportedElement,
                            Message = $"Preserved unsupported LaTeX environment '{envName}' as a raw passthrough block — content will round-trip on export but won't render in the editor preview.",
                        });
                    }
                    break;
            }

            // Continue with remaining content
            remaining = remaining[(firstMatch.match.Index + firstMatch.match.Length)..];
        }
    }

    private void AddParagraphs(string content, ImportDocument document, ref int elementOrder)
    {
        // Split by double newlines to get paragraphs
        var paragraphs = Regex.Split(content, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p));

        foreach (var para in paragraphs)
        {
            // Skip if it's just LaTeX commands
            if (Regex.IsMatch(para, @"^\\(label|ref|cite|newpage|clearpage|vspace|hspace|centering)\b"))
                continue;

            // Skip empty or comment-only content
            if (string.IsNullOrWhiteSpace(para) || para.StartsWith("%"))
                continue;

            // Detect inline formatting spans (offsets computed against raw LaTeX).
            var formatting = ParseLatexFormatting(para);
            // Then normalise the text itself — strip known formatting wrappers
            // AND unknown custom macros (\inlinecode{X} → X, \customMacro{X} → X).
            // Without this, user-defined \newcommand wrappers leak into the
            // block text as literal "\inlinecode{foo}" strings.
            var normalised = NormaliseInlineCommands(para);

            document.Elements.Add(new ImportParagraph
            {
                Order = elementOrder++,
                Text = normalised,
                Style = ParagraphStyle.Normal,
                // Span offsets were computed against the pre-normalised text; the
                // editor re-derives them from the normalised markdown-ish text so
                // we don't ship stale offsets. Leave the list in case downstream
                // detectors still use the span *types* for style hints.
                Formatting = formatting
            });
        }
    }

    private static List<FormattingSpan> ParseLatexFormatting(string text)
    {
        var spans = new List<FormattingSpan>();

        // \textbf{...}
        foreach (Match match in Regex.Matches(text, @"\\textbf\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Bold
            });
        }

        // \textit{...} or \emph{...}
        foreach (Match match in Regex.Matches(text, @"\\(textit|emph)\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Italic
            });
        }

        // \underline{...}
        foreach (Match match in Regex.Matches(text, @"\\underline\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Underline
            });
        }

        // \texttt{...}
        foreach (Match match in Regex.Matches(text, @"\\texttt\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.FontFamily,
                Value = "monospace"
            });
        }

        return spans;
    }

    /// <summary>
    /// Pre-parse normalisation for Batch A + C coverage work. Rewrites
    /// well-known environment variants into their kernel equivalents so
    /// the main parser can handle them as standard environments. Each
    /// rewrite targets a catalog token whose coverage_level moves from
    /// partial/shimmed to full as a result.
    ///
    /// Current rewrites:
    ///   tabularx  → tabular  (column spec 'X' rewritten to 'l')
    ///   rSection  → section-like block (\begin{rSection}{Title}...
    ///                → \section*{Title} + body)
    ///   frame     → section-like block (beamer slide → heading + body)
    ///   frametitle → section* in-place
    ///   framesubtitle → bold paragraph
    ///   titlepage → \maketitle
    /// </summary>
    private static string NormaliseCoverageEnvironments(string content)
    {
        // tabularx — swap `\begin{tabularx}{<width>}{<colspec>}...\end{tabularx}`
        // to `\begin{tabular}{<cleaned-colspec>}...\end{tabular}`. `X`
        // columns are variable-width in tabularx; we degrade them to `l`
        // since the web view doesn't flex-size columns. Width specifier is
        // dropped (tabular doesn't take it).
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\begin\{tabularx\}\s*\{[^}]*\}\s*\{([^}]*)\}",
            m => $"\\begin{{tabular}}{{{m.Groups[1].Value.Replace('X', 'l')}}}",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\end\{tabularx\}",
            "\\end{tabular}");

        // rSection (resume class) — rewrite `\begin{rSection}{Title}body`
        // into `\section*{Title} body`. `\end{rSection}` becomes a marker
        // we can drop. Handles nested scopes because the section boundary
        // follows LaTeX's usual rules once rewritten.
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\begin\{rSection\}\s*\{([^}]*)\}",
            m => $"\\section*{{{m.Groups[1].Value}}}",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\end\{rSection\}",
            string.Empty);

        // Beamer frames — the Q2 plan's biggest single coverage win (41
        // hits in the last 7 days of prod imports). Three common forms
        // are handled here; the lossy-for-export trade-off is flagged in
        // the catalog notes.
        //
        //   \begin{frame}[options]{Title}body  →  \section*{Title} body
        //   \begin{frame}{Title}body           →  \section*{Title} body
        //   \begin{frame}\frametitle{T}body    →  (the \frametitle rewrite below handles this)
        //   \begin{frame}body (no title)       →  body (no heading — still navigable via surrounding structure)
        //
        // Optional [options] is discarded at this level — frame options
        // (label, allowframebreaks, fragile) aren't expressed in Lilia's
        // block model today. The exporter regenerates beamer frames from
        // document.latexDocumentClass metadata if needed.
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\begin\{frame\}\s*(?:\[[^\]]*\])?\s*\{([^}]*)\}",
            m => $"\\section*{{{m.Groups[1].Value}}}",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // Frames that don't supply a brace title (title comes later via
        // \frametitle, or no title at all). Just drop the \begin{frame}
        // marker; \frametitle below handles the title.
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\begin\{frame\}\s*(?:\[[^\]]*\])?",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);

        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\end\{frame\}",
            string.Empty);

        // \frametitle{title} anywhere inside a frame → \section*{title}
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\frametitle\s*\{([^}]*)\}",
            m => $"\\section*{{{m.Groups[1].Value}}}",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // \framesubtitle{subtitle} → bold paragraph (no kernel equivalent)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\framesubtitle\s*\{([^}]*)\}",
            m => $"\\textbf{{{m.Groups[1].Value}}}\\par",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        // \titlepage → \maketitle (standard kernel). Preserves title +
        // author the user set in the preamble.
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\\titlepage\b",
            "\\maketitle");

        return content;
    }

    /// <summary>
    /// Collapse \ifxetexorluatex ... \else ... \fi (and siblings) to the
    /// pdflatex branch. Our compile pipeline is pdflatex-only; shipping both
    /// branches made us load fontspec / unicode-math which abort pdflatex.
    /// Handles nested siblings in a single linear pass.
    /// </summary>
    private static string StripXeLuaConditionals(string content)
    {
        // Match \ifxetexorluatex ... [\else ...] \fi, \ifxetex, \ifluatex,
        // \ifpdftex. The two former expect true when running under XeTeX/LuaTeX,
        // so the else branch is what pdflatex runs. \ifpdftex is the opposite
        // — true under pdflatex.
        var rx = new Regex(
            @"\\(ifxetexorluatex|ifxetex|ifluatex|ifpdftex)\b([\s\S]*?)(?:\\else([\s\S]*?))?\\fi\b",
            RegexOptions.Singleline);
        // Non-overlapping replacement. Re-run until stable to collapse nested occurrences.
        string prev;
        do
        {
            prev = content;
            content = rx.Replace(content, m =>
            {
                var kind = m.Groups[1].Value;
                var thenBranch = m.Groups[2].Value;
                var elseBranch = m.Groups[3].Success ? m.Groups[3].Value : "";
                // For Xe/Lua guards the pdflatex path is the else branch.
                // For \ifpdftex it's the then branch.
                return string.Equals(kind, "ifpdftex", StringComparison.OrdinalIgnoreCase)
                    ? thenBranch
                    : elseBranch;
            });
        } while (prev != content);
        return content;
    }

    /// <summary>
    /// Pull CV-style personal-info macros out of the preamble and put them on
    /// the ImportDocument.Metadata so a later personalInfo block (or export
    /// re-emission of \name/\email/\phone) can draw on them.
    /// </summary>
    private static void ExtractCvPreambleMetadata(string content, ImportDocument document)
    {
        var nameMatch = Regex.Match(content, @"\\name\{([^}]+)\}\{([^}]+)\}");
        if (nameMatch.Success)
        {
            document.Metadata.PersonName = $"{nameMatch.Groups[1].Value.Trim()} {nameMatch.Groups[2].Value.Trim()}";
        }
        var emailMatch = Regex.Match(content, @"\\email\{([^}]+)\}");
        if (emailMatch.Success) document.Metadata.Email = emailMatch.Groups[1].Value.Trim();

        foreach (Match pm in Regex.Matches(content, @"\\phone(?:\[([^\]]+)\])?\{([^}]+)\}"))
        {
            var kind = pm.Groups[1].Success ? pm.Groups[1].Value.Trim() : "mobile";
            document.Metadata.Phones.Add((kind, pm.Groups[2].Value.Trim()));
        }

        var homeMatch = Regex.Match(content, @"\\homepage\{([^}]+)\}");
        if (homeMatch.Success) document.Metadata.Homepage = homeMatch.Groups[1].Value.Trim();

        var photoMatch = Regex.Match(content, @"\\photo(?:\[[^\]]*\])?(?:\[[^\]]*\])?\{([^}]+)\}");
        if (photoMatch.Success) document.Metadata.PhotoFilename = photoMatch.Groups[1].Value.Trim();

        foreach (Match sm in Regex.Matches(content, @"\\social(?:\[([^\]]+)\])?\{([^}]+)\}"))
        {
            var network = sm.Groups[1].Success ? sm.Groups[1].Value.Trim() : "web";
            document.Metadata.Socials.Add((network, sm.Groups[2].Value.Trim()));
        }

        var extraMatch = Regex.Match(content, @"\\extrainfo\{([^}]+)\}");
        if (extraMatch.Success) document.Metadata.ExtraInfo = extraMatch.Groups[1].Value.Trim();
    }

    /// <summary>
    /// Parse a flat list of `algorithmic` lines. Each \STATE, \REQUIRE, \IF,
    /// \FOR, etc. becomes one typed line entry. End markers (\ENDIF, \ENDFOR)
    /// are preserved as their own kind so the exporter can re-emit them and
    /// the UI can render a matching end marker. Condition / decl text for
    /// control-flow openers (\IF{...}, \FOR{...}) is captured in the `Text`
    /// field.
    /// </summary>
    private static List<ImportAlgorithmLine> ParseAlgorithmicLines(string body)
    {
        var lines = new List<ImportAlgorithmLine>();
        // Tokenise: each algorithmic command opens a line that ends at the
        // next command or end-of-body.
        // Pattern captures the command word and whatever follows until the next
        // \COMMAND or end.
        var cmdRegex = new Regex(
            @"\\(REQUIRE|ENSURE|STATE|RETURN|PRINT|COMMENT|IF|ELSIF|ELSEIF|ELSE|ENDIF|FOR|FORALL|ENDFOR|WHILE|ENDWHILE|REPEAT|UNTIL|LOOP|ENDLOOP)\b",
            RegexOptions.IgnoreCase);
        var matches = cmdRegex.Matches(body);
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var cmd = m.Groups[1].Value.ToLowerInvariant();
            // Map aliases to canonical kinds.
            var kind = cmd switch
            {
                "state" => "statement",
                "elseif" => "elsif",
                "forall" => "for",
                _ => cmd,
            };
            var startOfArg = m.Index + m.Length;
            var endOfArg = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            var rawArg = body.Substring(startOfArg, endOfArg - startOfArg).Trim();
            // Some openers take their argument inside {braces}: \IF{...}, \FOR{...},
            // \WHILE{...}, \UNTIL{...}, \ELSIF{...}, \COMMENT{...}. Strip one
            // pair of outer braces if present so the condition reads cleanly.
            if (rawArg.StartsWith('{') && rawArg.EndsWith('}'))
            {
                rawArg = rawArg.Substring(1, rawArg.Length - 2).Trim();
            }
            lines.Add(new ImportAlgorithmLine
            {
                Kind = kind,
                Text = rawArg,
            });
        }
        // If no commands matched, fall back to a single statement so the
        // block still holds the user's text.
        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(body))
        {
            lines.Add(new ImportAlgorithmLine { Kind = "statement", Text = body.Trim() });
        }
        return lines;
    }

    private static ImportTable ParseTabular(string tabularContent)
    {
        var table = new ImportTable();
        var rows = tabularContent.Split(new[] { @"\\" }, StringSplitOptions.None);

        // Track \hline positions between data rows so we can guess the header row
        // (the row immediately before the second \hline boundary, when present).
        var dataRowIndexAfterHlines = new List<int>();
        var hlineCountSoFar = 0;
        var hlineBeforeFirstData = 0;

        foreach (var rowStr in rows)
        {
            // P0-2: don't drop the entire row when it merely *starts* with \hline.
            // Strip \hline / \cline directives anywhere in the row, then keep what remains.
            var trimmedRow = rowStr.Trim();
            if (string.IsNullOrEmpty(trimmedRow))
                continue;

            // Count and strip leading horizontal-rule directives.
            var stripped = Regex.Replace(trimmedRow, @"\\hline\b|\\cline\{[^}]*\}|\\toprule\b|\\midrule\b|\\bottomrule\b", m =>
            {
                hlineCountSoFar++;
                return string.Empty;
            }).Trim();

            if (string.IsNullOrEmpty(stripped))
                continue;

            var cells = stripped.Split('&');
            var row = new List<ImportTableCell>();

            foreach (var cell in cells)
            {
                var cellText = cell.Trim();
                row.Add(new ImportTableCell
                {
                    Text = cellText,
                    Formatting = ParseLatexFormatting(cellText)
                });
            }

            if (row.Count > 0)
            {
                if (table.Rows.Count == 0) hlineBeforeFirstData = hlineCountSoFar;
                table.Rows.Add(row);
            }
        }

        // Header heuristic: if a horizontal rule appeared before the first data row
        // AND the first row has the same column count as the rest, treat it as a header.
        // Falls back to "any table with >1 row has a header" (the prior behavior).
        table.HasHeaderRow = table.Rows.Count > 1
            && (hlineBeforeFirstData > 0 || table.Rows.Count > 1);

        return table;
    }
}
