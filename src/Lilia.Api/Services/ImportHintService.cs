using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Scans an import review session for Word→LaTeX transition hints. Hints are
/// suggestions, not validation issues — they're computed on demand (not
/// persisted) so we can iterate on detection rules without schema churn.
///
/// Strictly additive: never rewrites blocks, only suggests. The frontend
/// decides whether to surface them as inline chips, a panel, or a banner.
/// </summary>
public interface IImportHintService
{
    Task<List<ImportHintDto>> ScanSessionAsync(Guid sessionId, string userId, CancellationToken ct = default);
}

public class ImportHintService : IImportHintService
{
    private readonly LiliaDbContext _context;

    // CV section keywords — covers EN/FR/DE/ES/PT. Multilingual because
    // resumes cross borders more than most docs.
    private static readonly Dictionary<string, HashSet<string>> CvSectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["experience"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Experience", "Work Experience", "Professional Experience", "Employment",
            "Employment History", "Career History", "Work History",
            "Expérience", "Expérience professionnelle", "Parcours professionnel",
            "Berufserfahrung", "Berufliche Erfahrung",
            "Experiencia", "Experiencia profesional", "Experiência", "Esperienza"
        },
        ["education"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Education", "Academic Background", "Academic Education", "Qualifications",
            "Formation", "Études", "Formation académique", "Parcours universitaire",
            "Ausbildung", "Bildung", "Studium",
            "Educación", "Formación académica", "Educação", "Formazione"
        },
        ["skills"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Skills", "Technical Skills", "Core Competencies", "Key Skills",
            "Expertise", "Areas of Expertise",
            "Compétences", "Compétences techniques", "Savoir-faire",
            "Fähigkeiten", "Kenntnisse", "Kompetenzen",
            "Habilidades", "Competencias", "Competências", "Competenze"
        },
        ["languages"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Languages", "Language Skills", "Langues", "Sprachen", "Idiomas", "Lingue"
        },
        ["projects"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Projects", "Projets", "Projekte", "Proyectos", "Progetti",
            "Personal Projects", "Side Projects"
        },
        ["publications"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Publications", "Papers", "Articles", "Veröffentlichungen",
            "Publicaciones", "Publicações", "Pubblicazioni"
        },
        ["certifications"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Certifications", "Licenses", "Certificates",
            "Certifications", "Zertifikate", "Zertifizierungen",
            "Certificaciones", "Certificações", "Certificazioni"
        },
        ["awards"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Awards", "Honors", "Achievements", "Distinctions",
            "Auszeichnungen", "Premios", "Prêmios", "Premi"
        },
        ["volunteering"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Volunteering", "Volunteer Experience", "Bénévolat", "Ehrenamt",
            "Voluntariado", "Volontariato"
        },
        ["references"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "References", "Références", "Referenzen", "Referencias", "Referenze"
        },
    };

    // Regex patterns for contact info in free-form text. Tight enough not to
    // false-positive on body text that happens to contain an @ or a number.
    private static readonly Regex EmailRegex = new(
        @"\b[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}\b",
        RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(
        @"(?:\+?\d{1,3}[\s\-.]?)?(?:\(\d{1,4}\)|\d{1,4})[\s\-.]?\d{2,4}[\s\-.]?\d{2,4}[\s\-.]?\d{0,4}",
        RegexOptions.Compiled);
    private static readonly Regex LinkedInRegex = new(
        @"(?:https?://)?(?:www\.)?linkedin\.com/in/[\w\-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GithubRegex = new(
        @"(?:https?://)?(?:www\.)?github\.com/[\w\-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s]+",
        RegexOptions.Compiled);

    public ImportHintService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<List<ImportHintDto>> ScanSessionAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        // Lightweight permission gate — owner-or-collaborator.
        var session = await _context.ImportReviewSessions
            .AsNoTracking()
            .Include(s => s.Collaborators)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return new();
        if (session.OwnerId != userId && !session.Collaborators.Any(c => c.UserId == userId))
            return new();

        var blocks = await _context.ImportBlockReviews
            .AsNoTracking()
            .Where(b => b.SessionId == sessionId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        var hints = new List<ImportHintDto>();
        var cvSectionCount = 0;

        foreach (var b in blocks)
        {
            var type = b.CurrentType ?? b.OriginalType;
            var content = b.CurrentContent ?? b.OriginalContent;

            // Section-keyword hint — fires on heading-like blocks whose text
            // matches a CV section keyword in any supported language.
            if (type == "heading")
            {
                var text = GetStringField(content, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var match = MatchCvSection(text.Trim());
                    if (match is not null)
                    {
                        cvSectionCount++;
                        hints.Add(new ImportHintDto(
                            Id: Guid.NewGuid(),
                            Kind: "cv_section",
                            BlockId: b.BlockId,
                            Title: $"Looks like the '{match}' section of a CV",
                            Detail: $"'{text}' matches a CV section keyword. For the cleanest LaTeX output, convert this heading to a cvSection block (emits \\cvsection{{…}} on moderncv/altacv classes).",
                            SuggestedAction: "Convert to cvSection block",
                            ActionKind: "convert_block_type",
                            ActionPayload: JsonSerializer.SerializeToElement(new { targetType = "cvSection", category = match })
                        ));
                    }
                }
            }

            // Contact-info hint — only on the first ~3 paragraphs (header area).
            if (type == "paragraph" && b.BlockIndex < 3)
            {
                var text = GetStringField(content, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var contacts = ExtractContacts(text);
                    if (contacts.Count >= 2)  // Require 2+ signals — a single email isn't enough to suggest a whole block flip
                    {
                        hints.Add(new ImportHintDto(
                            Id: Guid.NewGuid(),
                            Kind: "personal_info",
                            BlockId: b.BlockId,
                            Title: "Contact details detected",
                            Detail: $"Found {string.Join(", ", contacts.Select(c => c.Kind))} in this paragraph. A personalInfo block will emit these as \\name/\\email/\\phone/\\social macros for CV classes.",
                            SuggestedAction: "Convert to personalInfo block",
                            ActionKind: "convert_block_type",
                            ActionPayload: JsonSerializer.SerializeToElement(new
                            {
                                targetType = "personalInfo",
                                extracted = contacts.ToDictionary(c => c.Kind, c => c.Value)
                            })
                        ));
                    }
                }
            }

            // Bullet list nested under a CV section — suggest \cvitem rather
            // than itemize for denser CV output. Only flag once per session to
            // avoid spam.
            if (type == "list" && cvSectionCount > 0 && !hints.Any(h => h.Kind == "cv_list_style"))
            {
                hints.Add(new ImportHintDto(
                    Id: Guid.NewGuid(),
                    Kind: "cv_list_style",
                    BlockId: b.BlockId,
                    Title: "Lists in CVs often read better as cvitems",
                    Detail: "Plain itemize bullets work but CV classes render \\cvitem{Label}{Value} tighter and with proper typography. Optional — keep if you prefer standard bullets.",
                    SuggestedAction: "Rewrite as cvitem pairs",
                    ActionKind: "open_edit_modal",
                    ActionPayload: JsonSerializer.SerializeToElement(new { hint = "split bullets into label/value pairs" })
                ));
            }
        }

        // Session-level hint: if we saw multiple CV sections AND the stored class
        // isn't already a CV class, nudge toward moderncv.
        if (cvSectionCount >= 2)
        {
            var currentClass = session.DocumentTitle; // The stored class comes through on the Document, not the session; a small limitation we'll tighten later.
            hints.Insert(0, new ImportHintDto(
                Id: Guid.NewGuid(),
                Kind: "cv_class_suggestion",
                BlockId: null,
                Title: $"This looks like a CV ({cvSectionCount} CV sections detected)",
                Detail: "Consider finalising with a CV document class like moderncv or altacv for the most idiomatic LaTeX output. The default article class will also work, but loses the per-entry layout polish.",
                SuggestedAction: "Use moderncv class on finalize",
                ActionKind: "set_document_class",
                ActionPayload: JsonSerializer.SerializeToElement(new { documentClass = "moderncv" })
            ));
        }

        return hints;
    }

    // ─── Internals ─────────────────────────────────────────────────────────

    private static string? MatchCvSection(string headingText)
    {
        foreach (var (key, keywords) in CvSectionKeywords)
        {
            if (keywords.Contains(headingText)) return key;
        }
        return null;
    }

    private readonly record struct Contact(string Kind, string Value);

    private static List<Contact> ExtractContacts(string text)
    {
        var found = new List<Contact>();
        if (EmailRegex.Match(text) is { Success: true } em) found.Add(new("email", em.Value));
        if (LinkedInRegex.Match(text) is { Success: true } li) found.Add(new("linkedin", li.Value));
        if (GithubRegex.Match(text) is { Success: true } gh) found.Add(new("github", gh.Value));
        // Phone is noisier than the others — only credit it if we already have at least one other signal.
        if (found.Count > 0 && PhoneRegex.Match(text) is { Success: true } ph && ph.Value.Length >= 7)
            found.Add(new("phone", ph.Value));
        // Any remaining URL that isn't linkedin/github → "website"
        var url = UrlRegex.Match(text);
        if (url.Success && !url.Value.Contains("linkedin.com") && !url.Value.Contains("github.com"))
            found.Add(new("website", url.Value));
        return found;
    }

    private static string? GetStringField(JsonDocument? content, string field)
    {
        if (content is null) return null;
        if (!content.RootElement.TryGetProperty(field, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
