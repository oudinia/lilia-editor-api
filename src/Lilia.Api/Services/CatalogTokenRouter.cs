using Lilia.Import.Services;

namespace Lilia.Api.Services;

/// <summary>
/// <see cref="ITokenRouter"/> implementation backed by the in-memory
/// catalog cache. Lives in Lilia.Api because that's where
/// <see cref="ILatexCatalogService"/> lives — the interface lives down
/// in Lilia.Import so LatexParser can consume it without taking a
/// dependency on the outer layer.
///
/// Lookups are O(1) — the catalog is preloaded at startup into a
/// dictionary keyed by (name, kind, package_slug); this router uses
/// the public <see cref="ILatexCatalogService.LookupToken"/> which
/// already does the kernel-fallback when a package-scoped lookup
/// misses.
/// </summary>
public sealed class CatalogTokenRouter : ITokenRouter
{
    private readonly ILatexCatalogService _catalog;

    public CatalogTokenRouter(ILatexCatalogService catalog)
    {
        _catalog = catalog;
    }

    public string? HandlerKindFor(string name, string kind)
    {
        var entry = _catalog.LookupToken(name, kind);
        if (entry is null) return null;
        // Only surface the handler_kind for rows we actually claim to
        // handle. 'unsupported' / 'none' rows exist only for tracking
        // purposes; they carry no handler_kind and the parser should
        // fall through to its unknown-env / inline catch-all paths.
        return entry.CoverageLevel is "full" or "partial" or "shimmed"
            ? entry.HandlerKind
            : null;
    }

    public string? MapsToBlockTypeFor(string name, string kind)
    {
        var entry = _catalog.LookupToken(name, kind);
        return entry?.MapsToBlockType;
    }
}
