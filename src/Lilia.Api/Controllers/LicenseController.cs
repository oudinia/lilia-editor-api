using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LicenseController : ControllerBase
{
    private readonly ILicenseService _licenseService;
    private readonly IHostEnvironment _environment;

    public LicenseController(ILicenseService licenseService, IHostEnvironment environment)
    {
        _licenseService = licenseService;
        _environment = environment;
    }

    private string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Get license status for the authenticated user.
    /// In development mode, returns "pro" edition with all features.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<LicenseStatusDto>> GetStatus()
    {
        if (_environment.IsDevelopment())
        {
            return Ok(new LicenseStatusDto("pro", [
                "documentEditing", "importDocx", "importPdf", "importMarkdown", "importLatex",
                "exportPdf", "exportLatex", "exportDocx", "exportHtml", "exportMarkdown",
                "exportTypst", "allThemes", "cloudSync", "collaboration", "unlimitedSnapshots",
                "prioritySupport", "formulaLibrary", "formulaEditor",
                "formulaCopy", "formulaCreate"
            ], -1));
        }

        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var result = await _licenseService.GetLicenseStatusAsync(userId);
        return Ok(result);
    }
}
