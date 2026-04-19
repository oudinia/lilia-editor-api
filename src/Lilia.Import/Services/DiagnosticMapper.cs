using Lilia.Core.Entities;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Translates parser-emitted <see cref="ImportWarning"/> into the richer
/// <see cref="ImportDiagnostic"/> schema. Centralises the stable code
/// dictionary, default severity, category, and documentation deeplink so the
/// paid review UI can filter / aggregate / link out without ad-hoc string
/// matching in the frontend.
/// </summary>
public static class DiagnosticMapper
{
    private record CodeSpec(string Category, string Severity, string Code, string? DocsUrl);

    // ImportWarningType → taxonomy row. Detail text in the warning further
    // refines the Code (e.g. UNSUPPORTED_PKG.TIKZ vs UNSUPPORTED_PKG.TITLESEC).
    private static readonly Dictionary<ImportWarningType, CodeSpec> BaseMap = new()
    {
        [ImportWarningType.UnsupportedElement]       = new("unsupported_package",     "warning", "LATEX.UNSUPPORTED_PKG",       "/latex/docs/unsupported-packages"),
        [ImportWarningType.UnknownStyle]             = new("unknown_macro",           "info",    "LATEX.UNKNOWN_MACRO",         "/latex/docs/unknown-macros"),
        [ImportWarningType.EquationConversionFailed] = new("parse_ambiguity",         "warning", "LATEX.EQUATION_FAILED",       "/latex/docs/equations"),
        [ImportWarningType.ImageExtractionFailed]    = new("missing_asset",           "error",   "LATEX.MISSING_ASSET.FIGURE",  "/latex/docs/figures"),
        [ImportWarningType.FormattingLost]           = new("parse_ambiguity",         "info",    "LATEX.FORMATTING_LOST",       null),
        [ImportWarningType.NestedTableSkipped]       = new("parse_ambiguity",         "warning", "LATEX.NESTED_TABLE",          "/latex/docs/tables"),
        [ImportWarningType.MergedCellsSimplified]    = new("parse_ambiguity",         "info",    "LATEX.MERGED_CELLS",          "/latex/docs/tables"),
        [ImportWarningType.ContentTruncated]         = new("size_truncated",          "warning", "LATEX.SIZE_TRUNCATED",        null),
    };

    // Keyword → specific category override. Matches parser Details/Message text
    // (LatexParser emits these as free text for load-order traps, preamble
    // conflicts, etc.). Keeps the warning type stream simple while letting us
    // slice the review UI by concrete issue.
    private static readonly (string Needle, string Category, string Severity, string Code, string? DocsUrl, string? Suggested)[] Specialisations =
    {
        ("hyperref",      "load_order",         "warning", "LATEX.LOAD_ORDER.HYPERREF_CLEVEREF", "/latex/docs/hyperref", "Load hyperref before cleveref"),
        ("cleveref",      "load_order",         "warning", "LATEX.LOAD_ORDER.HYPERREF_CLEVEREF", "/latex/docs/cleveref", "Load hyperref before cleveref"),
        ("subfig",        "load_order",         "warning", "LATEX.LOAD_ORDER.SUBFIG_SUBCAPTION", "/latex/docs/subcaption", "subfig and subcaption conflict — pick one"),
        ("subfigure",     "load_order",         "warning", "LATEX.LOAD_ORDER.SUBFIG_SUBCAPTION", "/latex/docs/subcaption", "subfigure and subcaption conflict — pick one"),
        ("csquotes",      "load_order",         "warning", "LATEX.LOAD_ORDER.CSQUOTES_BABEL",    "/latex/docs/csquotes", "Load csquotes after babel"),
        ("varioref",      "load_order",         "warning", "LATEX.LOAD_ORDER.VARIOREF",          "/latex/docs/varioref", "Load varioref before hyperref"),
        ("fontspec",      "preamble_conflict",  "warning", "LATEX.PREAMBLE.XELATEX_ONLY",        "/latex/docs/xelatex",  "fontspec requires XeLaTeX / LuaLaTeX — shimmed"),
        ("polyglossia",   "preamble_conflict",  "warning", "LATEX.PREAMBLE.POLYGLOSSIA",         "/latex/docs/xelatex",  "polyglossia requires XeLaTeX / LuaLaTeX — shimmed"),
        ("beamer",        "unsupported_class",  "warning", "LATEX.UNSUPPORTED_CLASS.BEAMER",     "/latex/docs/beamer",   "Beamer frames are shimmed to article — slide blocks coming"),
        ("algorithm2e",   "unsupported_package","warning", "LATEX.UNSUPPORTED_PKG.ALGORITHM2E",  "/latex/docs/algorithms", "We bundle algorithm + algorithmic instead"),
        ("titlesec",      "unsupported_package","warning", "LATEX.UNSUPPORTED_PKG.TITLESEC",     "/latex/docs/titlesec",  null),
        ("tikz",          "unsupported_package","info",    "LATEX.UNSUPPORTED_PKG.TIKZ",         "/latex/docs/tikz",      "TikZ content is preserved as passthrough"),
    };

    /// <summary>
    /// Map a single parser warning into a diagnostic row. Caller sets SessionId
    /// and BlockId before insert; everything else is populated here.
    /// </summary>
    public static ImportDiagnostic Map(ImportWarning w, Guid sessionId, string? blockId = null)
    {
        var spec = BaseMap.TryGetValue(w.Type, out var baseSpec)
            ? baseSpec
            : new CodeSpec("parse_ambiguity", "warning", "LATEX.UNKNOWN", null);

        var category = spec.Category;
        var severity = spec.Severity;
        var code = spec.Code;
        var docsUrl = spec.DocsUrl;
        string? suggested = null;

        // Look for a more specific specialisation in the details/message text.
        var haystack = ((w.Details ?? "") + " " + w.Message).ToLowerInvariant();
        foreach (var s in Specialisations)
        {
            if (haystack.Contains(s.Needle))
            {
                category = s.Category;
                severity = s.Severity;
                code = s.Code;
                docsUrl = s.DocsUrl ?? docsUrl;
                suggested = s.Suggested;
                break;
            }
        }

        return new ImportDiagnostic
        {
            SessionId = sessionId,
            BlockId = blockId,
            Category = category,
            Severity = severity,
            Code = code,
            Message = w.Message,
            SuggestedAction = suggested,
            DocsUrl = docsUrl,
            // The parser currently reports shimmed packages as warnings — mark
            // them so the UI can group "we handled it for you" separately.
            AutoFixApplied = code.StartsWith("LATEX.LOAD_ORDER") || code == "LATEX.PREAMBLE.XELATEX_ONLY" || code == "LATEX.UNSUPPORTED_CLASS.BEAMER",
        };
    }
}
