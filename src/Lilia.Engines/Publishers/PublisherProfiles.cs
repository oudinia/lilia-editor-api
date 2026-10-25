namespace Lilia.Engines.Publishers;

/// <summary>
/// One venue at one stage: the class, its options, the bibliography style and what the publisher
/// requires. The "Publish with…" picker offers these; a document that has one prints with them.
/// </summary>
/// <param name="Id">Stable id stored on the document (the documents table's <c>PublisherProfile</c> column, beside <c>LatexDocumentClass</c>).</param>
/// <param name="Stage"><c>review</c> or <c>final</c> (camera-ready).</param>
/// <param name="RequiredMetadata">What the compliance check asks for: title, authors, affiliations,
/// country, abstract, keywords.</param>
/// <param name="TemplateVersion">The class version installed in TeX Live, the one this profile was
/// compiled against (requirement NFR-5: traceable to the official source).</param>
public sealed record PublisherProfile(
    string Id,
    string Publisher,
    string Venue,
    string VenueType,
    string Stage,
    string DocumentClass,
    string ClassOptions,
    string BibliographyStyle,
    string TemplateVersion,
    IReadOnlyList<string> RequiredMetadata,
    string GuidelinesUrl);

/// <summary>
/// The publisher profiles (publisher templates phase 1, 25 Oct 2026), versioned with the code: changing
/// one changes <see cref="CatalogVersion"/>. Phase 2 adds revtex4-2, iopart, amsart, siamart and makes
/// the catalogue updatable without a release.
///
/// <para>Review stages follow each publisher's own practice: ACM reviews with <c>review,anonymous</c>
/// (line numbers, no authors), journals in <c>manuscript</c>; Elsevier's <c>review</c> is double-spaced;
/// IEEE and LNCS review in the final layout.</para>
/// </summary>
public static class PublisherProfiles
{
    public const string CatalogVersion = "2026-10-25.3";

    private static readonly string[] Full = ["title", "authors", "affiliations", "abstract", "keywords"];

    public static IReadOnlyList<PublisherProfile> All { get; } =
    [
        new("acm-sigconf-review", "ACM", "SIGCONF (conference proceedings)", "conference", "review",
            "acmart", "sigconf,review,anonymous", "ACM-Reference-Format", "acmart 2.20",
            [.. Full, "country"], "https://www.acm.org/publications/proceedings-template"),
        new("acm-sigconf-final", "ACM", "SIGCONF (conference proceedings)", "conference", "final",
            "acmart", "sigconf", "ACM-Reference-Format", "acmart 2.20",
            [.. Full, "country"], "https://www.acm.org/publications/proceedings-template"),
        new("acm-journal-review", "ACM", "Journals and transactions (acmsmall)", "journal", "review",
            "acmart", "manuscript,review,anonymous", "ACM-Reference-Format", "acmart 2.20",
            [.. Full, "country"], "https://www.acm.org/publications/proceedings-template"),
        new("acm-journal-final", "ACM", "Journals and transactions (acmsmall)", "journal", "final",
            "acmart", "acmsmall", "ACM-Reference-Format", "acmart 2.20",
            [.. Full, "country"], "https://www.acm.org/publications/proceedings-template"),
        new("ieee-conference", "IEEE", "Conference proceedings", "conference", "final",
            "IEEEtran", "conference", "IEEEtran", "IEEEtran 1.8b",
            ["title", "authors", "abstract", "keywords"], "https://www.ieee.org/conferences/publishing/templates.html"),
        new("ieee-journal", "IEEE", "Journals and transactions", "journal", "final",
            "IEEEtran", "journal", "IEEEtran", "IEEEtran 1.8b",
            ["title", "authors", "abstract", "keywords"], "https://template-selector.ieee.org/"),
        new("elsevier-preprint", "Elsevier", "Journal (preprint)", "journal", "review",
            "elsarticle", "preprint,12pt", "elsarticle-num", "elsarticle 3.5",
            Full, "https://www.elsevier.com/researcher/author/policies-and-guidelines/latex-instructions"),
        new("elsevier-review", "Elsevier", "Journal (double-spaced review)", "journal", "review",
            "elsarticle", "review", "elsarticle-num", "elsarticle 3.5",
            Full, "https://www.elsevier.com/researcher/author/policies-and-guidelines/latex-instructions"),
        new("elsevier-final", "Elsevier", "Journal (two-column final)", "journal", "final",
            "elsarticle", "final,5p,times", "elsarticle-num", "elsarticle 3.5",
            Full, "https://www.elsevier.com/researcher/author/policies-and-guidelines/latex-instructions"),
        new("lncs-submission", "Springer", "LNCS (submission)", "proceedings", "review",
            "llncs", "", "splncs04", "llncs 2.26",
            Full, "https://www.springer.com/gp/computer-science/lncs/conference-proceedings-guidelines"),
        new("lncs-camera-ready", "Springer", "LNCS (camera-ready)", "proceedings", "final",
            "llncs", "", "splncs04", "llncs 2.26",
            Full, "https://www.springer.com/gp/computer-science/lncs/conference-proceedings-guidelines"),
        // Phase 2 (25 Oct 2026). APS and AIP submit in preprint (single column, wide spacing), publish
        // in reprint; the journal is an option (prl, prb…; jcp, apl…), here the family's default.
        new("aps-review", "APS", "Physical Review (submission)", "journal", "review",
            "revtex4-2", "aps,preprint", "apsrev4-2", "revtex4-2 4.2f",
            ["title", "authors", "affiliations", "abstract"], "https://journals.aps.org/authors/latex-guidelines"),
        new("aps-final", "APS", "Physical Review (reprint layout)", "journal", "final",
            "revtex4-2", "aps,reprint", "apsrev4-2", "revtex4-2 4.2f",
            ["title", "authors", "affiliations", "abstract"], "https://journals.aps.org/authors/latex-guidelines"),
        new("aip-review", "AIP", "AIP journals (submission)", "journal", "review",
            "revtex4-2", "aip,preprint", "aipnum4-2", "revtex4-2 4.2f",
            ["title", "authors", "affiliations", "abstract"], "https://publishing.aip.org/resources/researchers/author-instructions/"),
        new("aip-final", "AIP", "AIP journals (reprint layout)", "journal", "final",
            "revtex4-2", "aip,reprint", "aipnum4-2", "revtex4-2 4.2f",
            ["title", "authors", "affiliations", "abstract"], "https://publishing.aip.org/resources/researchers/author-instructions/"),
        new("ams-journal", "AMS", "AMS journals and proceedings", "journal", "final",
            "amsart", "", "amsplain", "amsart 2.20",
            ["title", "authors", "affiliations", "abstract", "keywords"], "https://www.ams.org/publications/authors/tex/amslatex"),
        // IOP and SIAM (25 Oct 2026): their classes come from the publishers (TEXMFLOCAL). IOP reviews
        // double-anonymous with [anonymous]; SIAM's [review] adds line numbers.
        new("iop-review", "IOP", "IOP journals (anonymous review)", "journal", "review",
            "iopjournal", "anonymous", "iopart-num", "iopjournal 2024/01/31",
            ["title", "authors", "affiliations", "abstract", "keywords"], "https://publishingsupport.iopscience.iop.org/?p=413"),
        new("iop-final", "IOP", "IOP journals", "journal", "final",
            "iopjournal", "", "iopart-num", "iopjournal 2024/01/31",
            ["title", "authors", "affiliations", "abstract", "keywords"], "https://publishingsupport.iopscience.iop.org/?p=413"),
        new("siam-review", "SIAM", "SIAM journals (review, line numbers)", "journal", "review",
            "siamart251216", "review", "siamplain", "siamart251216 v1.4.8",
            ["title", "authors", "affiliations", "abstract", "keywords"], "https://epubs.siam.org/journal-authors"),
        new("siam-final", "SIAM", "SIAM journals", "journal", "final",
            "siamart251216", "", "siamplain", "siamart251216 v1.4.8",
            ["title", "authors", "affiliations", "abstract", "keywords"], "https://epubs.siam.org/journal-authors"),
    ];

    public static PublisherProfile? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The bibliography style for a document: its profile's, else its publisher class's, else null.</summary>
    public static string? BibliographyStyleFor(string? profileId, string? className) =>
        Find(profileId)?.BibliographyStyle ?? PublisherClasses.BibliographyStyle(className);
}
