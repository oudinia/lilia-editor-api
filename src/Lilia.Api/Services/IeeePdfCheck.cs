using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Lilia.Engines.Publishers;

namespace Lilia.Api.Services;

/// <summary>
/// The IEEE PDF check (publisher templates phase 3, 25 Oct 2026): a compiled PDF against "PDF
/// Specification for IEEE Xplore®" (IEEE Content Engineering, version 4.02, July 2008, the rules IEEE
/// PDF eXpress applies). Each PDF must be PDF 1.4 or later, embed all its fonts, have no password or
/// security, and contain no bookmarks or links; images at least 300 dpi (600 dpi for black and white).
/// Type 3 (bitmap) fonts are not searchable. The page size is not specified; it is reported.
///
/// <para>Links and bookmarks: Lilia writes hyperref in draft mode for IEEE (PublisherClasses.Packages),
/// which makes neither; poppler has no tool that lists them, so this check trusts that, and says so.</para>
///
/// <para>Read with poppler's pdfinfo, pdffonts and pdfimages, on a PDF Lilia itself compiled.</para>
/// </summary>
public static partial class IeeePdfCheck
{
    public const string Spec = "PDF Specification for IEEE Xplore, version 4.02 (July 2008)";

    public sealed record PdfFacts(string Version, int Pages, string PageSize, bool Encrypted,
        IReadOnlyList<(string Name, string Type, bool Embedded)> Fonts,
        IReadOnlyList<(int Page, int Width, int Height, string Color, int Bits, int XPpi, int YPpi)> Images);

    /// <summary>The findings for a PDF's facts, in the compliance check's terms.</summary>
    public static ComplianceReport Judge(PdfFacts pdf, string? profile)
    {
        var findings = new List<ComplianceFinding>();
        if (decimal.TryParse(pdf.Version, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v < 1.4m)
            findings.Add(new("error", "pdf_version", $"The PDF is version {pdf.Version}; IEEE Xplore needs 1.4 or later."));
        if (pdf.Encrypted)
            findings.Add(new("error", "pdf_security", "The PDF has a password or security settings; IEEE Xplore accepts none."));
        foreach (var f in pdf.Fonts.Where(f => !f.Embedded))
            findings.Add(new("error", "pdf_font_embedded", $"The font {f.Name} is not embedded; IEEE Xplore needs every font embedded."));
        foreach (var f in pdf.Fonts.Where(f => f.Type.StartsWith("Type 3", StringComparison.Ordinal)).DistinctBy(f => f.Name))
            findings.Add(new("warning", "pdf_type3_font", $"The font {f.Name} is a Type 3 (bitmap) font: text in it can't be searched, which IEEE's checks flag."));
        foreach (var im in pdf.Images)
        {
            var mono = im.Bits == 1;
            var need = mono ? 600 : 300;
            var ppi = Math.Min(im.XPpi, im.YPpi);
            if (ppi > 0 && ppi < need)
                findings.Add(new("warning", "pdf_image_resolution",
                    $"An image on page {im.Page} prints at {ppi} dpi; IEEE asks for at least {need} dpi{(mono ? " for black and white" : "")}. Upload a larger image or print it smaller."));
        }
        findings.Add(new("info", "pdf_page_size", $"{pdf.Pages} page{(pdf.Pages == 1 ? "" : "s")}, {pdf.PageSize}. Check your conference's page limit and paper size."));
        findings.Add(new("info", "pdf_links", "No links or bookmarks: Lilia writes them off for IEEE, as IEEE Xplore requires."));
        var status = findings.Any(f => f.Severity == "error") ? "errors" : findings.Any(f => f.Severity == "warning") ? "warnings" : "passed";
        return new ComplianceReport(profile, status, findings, "https://www.ieee.org/conferences/publishing/pdfexpress.html");
    }

    /// <summary>The facts of <paramref name="pdf"/>, read with poppler.</summary>
    public static async Task<PdfFacts> ReadAsync(byte[] pdf, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lilia-pdfcheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "document.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, pdf, ct);
            var info = await RunAsync("pdfinfo", path, ct);
            var fonts = await RunAsync("pdffonts", path, ct);
            var images = await RunAsync("pdfimages", $"-list {path}", ct);
            return Parse(info, fonts, images);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    /// <summary>Parses the three tools' output (public for tests).</summary>
    public static PdfFacts Parse(string info, string fonts, string images)
    {
        string Field(string name) => Regex.Match(info, $@"^{name}:\s*(.+)$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value.Trim() : "";
        var version = Field("PDF version");
        int.TryParse(Field("Pages"), out var pages);
        var encrypted = Field("Encrypted").StartsWith("yes", StringComparison.OrdinalIgnoreCase);
        var size = Field("Page size");
        var pageSize = PageSizeName().Match(size) is { Success: true } sm ? $"{sm.Groups["name"].Value}" : size;

        // pdffonts: name, type (one or two words), encoding, emb, sub, uni, object, ID; read from the right.
        var fontRows = new List<(string, string, bool)>();
        foreach (var line in fonts.Split('\n').Skip(2).Where(l => l.Trim().Length > 0))
        {
            var cols = Regex.Split(line.Trim(), @"\s+");
            if (cols.Length < 7) continue;
            var emb = cols[^5];
            var type = FontType().Match(line) is { Success: true } tm ? tm.Value : cols[1];
            fontRows.Add((cols[0], type, emb == "yes"));
        }

        // pdfimages -list: page num type width height color comp bpc enc interp object ID x-ppi y-ppi size ratio.
        var imageRows = new List<(int, int, int, string, int, int, int)>();
        foreach (var line in images.Split('\n').Skip(2).Where(l => l.Trim().Length > 0))
        {
            var c = Regex.Split(line.Trim(), @"\s+");
            if (c.Length < 14 || c[2] != "image") continue;
            int I(int i) => int.TryParse(c[i], out var n) ? n : 0;
            imageRows.Add((I(0), I(3), I(4), c[5], I(7), I(12), I(13)));
        }
        return new PdfFacts(version, pages, pageSize, encrypted, fontRows, imageRows);
    }

    [GeneratedRegex(@"\((?<name>[^)]+)\)\s*$")]
    private static partial Regex PageSizeName();

    [GeneratedRegex(@"Type 1C|Type 1|Type 3|TrueType|CID Type 0C|CID Type 0|CID TrueType|Type 1 \(OT\)|TrueType \(OT\)")]
    private static partial Regex FontType();

    private static async Task<string> RunAsync(string tool, string args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(tool, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })
            ?? throw new InvalidOperationException($"{tool} did not start");
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return output;
    }
}
