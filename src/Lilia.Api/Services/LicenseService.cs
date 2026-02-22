using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class LicenseService : ILicenseService
{
    private readonly LiliaDbContext _context;

    private static readonly string[] ExpressFeatures =
    [
        "documentEditing", "importMarkdown", "importLatex", "exportPdf",
        "exportLatex", "basicVersionHistory", "lightTheme",
        "formulaBrowse", "formulaCopy", "formulaEditor"
    ];

    private static readonly string[] LatexLabFeatures =
    [
        "formulaLibrary", "formulaEditor", "formulaCopy",
        "formulaCreate", "allThemes"
    ];

    private static readonly string[] ConverterFeatures =
    [
        "importDocx", "importPdf", "importMarkdown", "importLatex", "exportPdf",
        "exportLatex", "exportDocx", "exportHtml", "exportMarkdown", "exportTypst",
        "allThemes", "viewOnly"
    ];

    private static readonly string[] ProFeatures =
    [
        "documentEditing", "importDocx", "importPdf", "importMarkdown", "importLatex",
        "exportPdf", "exportLatex", "exportDocx", "exportHtml", "exportMarkdown",
        "exportTypst", "allThemes", "cloudSync", "collaboration", "unlimitedSnapshots",
        "prioritySupport", "formulaLibrary", "formulaEditor",
        "formulaCopy", "formulaCreate"
    ];

    public LicenseService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<LicenseStatusDto> GetLicenseStatusAsync(string userId)
    {
        var purchases = await _context.Purchases
            .Where(p => p.UserId == userId)
            .ToListAsync();

        // Priority: pro > latexlab > converter > express
        var hasProSub = purchases.Any(p =>
            p.Type == "SUBSCRIPTION" &&
            p.ProductId.Contains("pro", StringComparison.OrdinalIgnoreCase) &&
            (p.Status == "active" || p.Status == "trialing"));

        if (hasProSub)
            return new LicenseStatusDto("pro", ProFeatures, -1);

        var hasLatexLabSub = purchases.Any(p =>
            p.Type == "SUBSCRIPTION" &&
            p.ProductId.Contains("latexlab", StringComparison.OrdinalIgnoreCase) &&
            (p.Status == "active" || p.Status == "trialing"));

        if (hasLatexLabSub)
            return new LicenseStatusDto("latexlab", LatexLabFeatures, 3);

        var hasConverter = purchases.Any(p =>
            p.Type == "ONE_TIME" &&
            p.ProductId.Contains("converter", StringComparison.OrdinalIgnoreCase));

        if (hasConverter)
            return new LicenseStatusDto("converter", ConverterFeatures, 5);

        return new LicenseStatusDto("express", ExpressFeatures, 3);
    }
}
