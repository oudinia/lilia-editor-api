using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines.Publishers;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// The submission ZIP (publisher templates step 6, 25 Oct 2026): GET …/export/latex?submission=true
/// gives one main.tex, references.bib, main.bbl, the publisher's .bst and the figures, with
/// lilia-export-manifest.json and a SHA-256 per file; no README; a figure without its image stops it.
/// </summary>
[Collection("Integration")]
public class SubmissionZipTests : IntegrationTestBase
{
    private readonly string _userId = $"subzip-{Guid.NewGuid():N}"[..28];
    public SubmissionZipTests(TestDatabaseFixture fixture) : base(fixture) { }
    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private async Task<(HttpClient Api, Guid DocId)> AcmPaperAsync(bool cite)
    {
        var api = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "A Paper");
        await SeedBlockAsync(doc.Id, "title", """{"title":"A Paper","author":"Ada Lovelace\\\\Analytical Engines\\\\UK","keywords":["LaTeX"]}""", 0);
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"We show it works."}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", cite ? """{"text":"As @cite{smith2024} shows."}""" : """{"text":"Hello."}""", 2);
        if (cite)
        {
            await SeedBibliographyEntryAsync(doc.Id);
        }
        (await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "acm-sigconf-review" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return (api, doc.Id);
    }

    private static async Task<Dictionary<string, byte[]>> EntriesAsync(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        using var zip = new ZipArchive(await res.Content.ReadAsStreamAsync());
        var entries = new Dictionary<string, byte[]>();
        // These papers are ACM's: TAPS takes everything under source/ (the TAPS test below checks it).
        zip.Entries.Should().OnlyContain(e => e.FullName.StartsWith("source/"));
        foreach (var e in zip.Entries)
        {
            using var ms = new MemoryStream();
            await e.Open().CopyToAsync(ms);
            entries[e.FullName["source/".Length..]] = ms.ToArray();
        }
        return entries;
    }

    private static void ManifestMatches(Dictionary<string, byte[]> entries)
    {
        var manifest = JsonDocument.Parse(entries[SubmissionManifest.FileName]).RootElement;
        manifest.GetProperty("manifestVersion").GetInt32().Should().Be(SubmissionManifest.Version);
        var pub = manifest.GetProperty("publisher");
        pub.GetProperty("profile").GetString().Should().Be("acm-sigconf-review");
        pub.GetProperty("documentClass").GetString().Should().Be("acmart");
        pub.GetProperty("classOptions").GetString().Should().Be("sigconf,review,anonymous");
        pub.GetProperty("stage").GetString().Should().Be("review");
        pub.GetProperty("templateVersion").GetString().Should().Be("acmart 2.20");
        manifest.GetProperty("packagePolicyVersion").GetString().Should().Be(PublisherClasses.PackagePolicyVersion);

        var listed = manifest.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("sha256").GetString()!);
        listed.Keys.Should().BeEquivalentTo(entries.Keys.Where(k => k != SubmissionManifest.FileName), "every other file is listed");
        foreach (var (path, sha) in listed)
            sha.Should().Be(Convert.ToHexStringLower(SHA256.HashData(entries[path])), path);
    }

    [Fact]
    public async Task Only_what_compiling_needs_with_a_manifest_of_checksums()
    {
        var (api, id) = await AcmPaperAsync(cite: false);
        var entries = await EntriesAsync(await api.GetAsync($"/api/documents/{id}/export/latex?submission=true&structure=multi"));

        entries.Keys.Should().Contain("main.tex").And.Contain(SubmissionManifest.FileName);
        entries.Keys.Should().NotContain("README.txt");
        entries.Keys.Should().NotContain(k => k.StartsWith("chapters/") || k.StartsWith("sections/") || k.StartsWith("frontmatter/"),
            "a submission is one main.tex, whatever layout was asked for");
        Encoding.UTF8.GetString(entries["main.tex"]).Should().Contain(@"\documentclass[sigconf,review,anonymous]{acmart}");
        ManifestMatches(entries);
        JsonDocument.Parse(entries[SubmissionManifest.FileName]).RootElement.GetProperty("validation").GetProperty("status")
            .GetString().Should().Be("passed", "the paper has everything ACM asks for");
    }

    [Fact]
    public async Task The_check_is_there_for_the_export_page_and_only_for_who_can_read()
    {
        var (api, id) = await AcmPaperAsync(cite: false);
        var report = await api.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/export/publisher-check");
        report.GetProperty("profile").GetString().Should().Be("acm-sigconf-review");
        report.GetProperty("status").GetString().Should().Be("passed");

        var stranger = $"subzip-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        (await CreateClientAs(stranger).GetAsync($"/api/documents/{id}/export/publisher-check"))
            .IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task A_figure_without_its_image_stops_the_submission_naming_it()
    {
        var (api, id) = await AcmPaperAsync(cite: false);
        await SeedBlockAsync(id, "figure", """{"src":"/uploads/nowhere/system-overview.png","caption":"Overview"}""", 5);

        var res = await api.GetAsync($"/api/documents/{id}/export/latex?submission=true");
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("missing_figures");
        body.GetProperty("figures").EnumerateArray().Select(f => f.GetString()).Should().Equal("system-overview.png");

        // An ordinary project still downloads, the figure printed as a frame.
        (await api.GetAsync($"/api/documents/{id}/export/latex")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task References_come_with_the_bbl_and_the_publishers_bst()
    {
        var (api, id) = await AcmPaperAsync(cite: true);
        var entries = await EntriesAsync(await api.GetAsync($"/api/documents/{id}/export/latex?submission=true"));

        entries.Keys.Should().Contain(["main.tex", "references.bib", "main.bbl", "ACM-Reference-Format.bst"]);
        Encoding.UTF8.GetString(entries["main.bbl"]).Should().Contain("smith2024");
        Encoding.UTF8.GetString(entries["main.tex"]).Should().Contain(@"\bibliographystyle{ACM-Reference-Format}");
        ManifestMatches(entries);
        JsonDocument.Parse(entries[SubmissionManifest.FileName]).RootElement.GetProperty("compile").GetProperty("sequence")
            .GetArrayLength().Should().Be(2, "with main.bbl, BibTeX needn't run");

        // Unzipped on its own, it compiles as the manifest says: pdflatex twice, no BibTeX (FR-9's
        // acceptance: the ZIP compiles independently).
        var dir = Directory.CreateTempSubdirectory("lilia-submission-").FullName;
        try
        {
            foreach (var (path, bytes) in entries) await File.WriteAllBytesAsync(Path.Combine(dir, path), bytes);
            for (var pass = 0; pass < 2; pass++)
                await Lilia.Engines.TexSafety.TexProcessRunner.RunAsync("pdflatex", "-interaction=nonstopmode main.tex", dir, 180);
            var log = File.ReadAllText(Path.Combine(dir, "main.log"));
            File.Exists(Path.Combine(dir, "main.pdf")).Should().BeTrue(log[Math.Max(0, log.Length - 2000)..]);
            log.Should().NotContain("Citation `smith2024' undefined");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task An_ACM_submission_is_laid_out_for_TAPS_and_named_by_acronym_and_paper_id()
    {
        var (api, id) = await AcmPaperAsync(cite: false);
        var res = await api.GetAsync($"/api/documents/{id}/export/latex?submission=true&tapsAcronym=CHI%20'27&tapsPaperId=1234");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be("CHI'27-1234.zip", "TAPS: <Proceeding Acronym>-<Paper ID>.zip");
        using var zip = new ZipArchive(await res.Content.ReadAsStreamAsync());
        zip.Entries.Select(e => e.FullName).Should().Contain(["source/main.tex", $"source/{SubmissionManifest.FileName}"]);

        // Without them, the usual name.
        var plain = await api.GetAsync($"/api/documents/{id}/export/latex?submission=true");
        plain.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().EndWith("-submission.zip");
    }
}
