using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Lilia.Core.Entities;

namespace Lilia.Engines.Publishers;

/// <summary>
/// <c>lilia-export-manifest.json</c>, in a submission ZIP (publisher templates step 6, requirement
/// FR-9): the publisher, venue, class and version, the stage, how to compile, the Lilia version, and a
/// SHA-256 for every other file in the ZIP, so what was sent can be checked against what was built,
/// and the compliance check's verdict.
/// </summary>
public static class SubmissionManifest
{
    public const string FileName = "lilia-export-manifest.json";

    /// <summary>The manifest's own shape; a field added later bumps it.</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] Build(Document doc, string className, IEnumerable<(string Path, byte[] Bytes)> files,
        ComplianceReport? validation = null, DateTimeOffset? at = null, string? installedClassVersion = null)
    {
        var profile = PublisherProfiles.Find(doc.PublisherProfile);
        var list = files.Where(f => f.Path != FileName).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var engine = string.IsNullOrWhiteSpace(doc.LatexEngine) ? "pdflatex" : doc.LatexEngine;
        var hasBbl = list.Any(f => f.Path == "main.bbl");
        var manifest = new
        {
            manifestVersion = Version,
            generator = new { name = "Lilia", version = LiliaVersion },
            createdAt = (at ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            document = new { id = doc.Id, title = doc.Title },
            publisher = new
            {
                profile = profile?.Id,
                publisher = profile?.Publisher,
                venue = profile?.Venue,
                stage = profile?.Stage,
                documentClass = profile?.DocumentClass ?? className,
                classOptions = profile?.ClassOptions ?? doc.LatexDocumentClassOptions ?? "",
                templateVersion = profile?.TemplateVersion,
                // The class as installed when this was built, and as it was when the venue was chosen.
                installedVersion = installedClassVersion,
                chosenWith = doc.PublisherTemplateVersion,
                bibliographyStyle = PublisherProfiles.BibliographyStyleFor(doc.PublisherProfile, className),
                catalogVersion = PublisherProfiles.CatalogVersion,
            },
            compile = new
            {
                engine,
                // With main.bbl in the ZIP, BibTeX needn't run; a system that runs it gets the same.
                sequence = hasBbl
                    ? new[] { $"{engine} main", $"{engine} main" }
                    : new[] { $"{engine} main", "bibtex main", $"{engine} main", $"{engine} main" },
            },
            packagePolicyVersion = PublisherClasses.PackagePolicyVersion,
            // The compliance check when the ZIP was built (PublisherCompliance): the verdict and its findings.
            validation = validation is null ? null : new
            {
                status = validation.Status,
                errors = validation.Findings.Count(f => f.Severity == "error"),
                warnings = validation.Findings.Count(f => f.Severity == "warning"),
                findings = validation.Findings.Select(f => new { severity = f.Severity, code = f.Code, message = f.Message }),
            },
            files = list.Select(f => new { path = f.Path, bytes = f.Bytes.LongLength, sha256 = Convert.ToHexStringLower(SHA256.HashData(f.Bytes)) }),
        };
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
    }

    private static string LiliaVersion { get; } =
        typeof(SubmissionManifest).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
