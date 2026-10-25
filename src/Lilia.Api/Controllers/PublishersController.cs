using Lilia.Engines.Publishers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// The publisher profiles the "Publish with…" picker offers (publisher templates phase 1, 25 Oct 2026):
/// venue, stage, class, options, bibliography style, template version, required metadata and the
/// publisher's guidelines. Set one on a document with PUT /api/documents/{id} { publisherProfile }.
/// </summary>
[ApiController]
[Route("api/publishers")]
[Authorize]
public class PublishersController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(new { catalogVersion = PublisherProfiles.CatalogVersion, profiles = PublisherProfiles.All });
}
