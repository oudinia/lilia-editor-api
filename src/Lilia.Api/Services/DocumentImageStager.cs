using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

/// <summary>
/// The images a document's figures point at, fetched for a compile.
///
/// <para>Typst reads images from disk. Blocks store a URL. Bridging that
/// gap is not a download: an image is resolved through this document's own
/// asset rows and read from storage by its <c>StorageKey</c> — a value the
/// server wrote and the author cannot influence. The URL in the block is only
/// ever a lookup key.</para>
///
/// <para>That rule is not new; it is what the Word and PDF exporters already
/// do, after the figure exporter was found issuing an HTTP GET to whatever a
/// document named — server-side request forgery with a pleasant user
/// interface. This is the third place needing it, which is why it now lives
/// somewhere shared.</para>
/// </summary>
public interface IDocumentImageStager
{
    /// <summary>
    /// Resolve every figure image in the document.
    /// </summary>
    /// <returns>
    /// <c>Paths</c> maps the src as stored to a relative path inside the
    /// compile directory; <c>Files</c> maps that path to its bytes. Both are
    /// empty when the document has no resolvable images, which is the common
    /// case and costs nothing.
    /// </returns>
    Task<StagedImages> StageAsync(Guid documentId, CancellationToken ct = default);
}

public sealed record StagedImages(
    IReadOnlyDictionary<string, string> Paths,
    IReadOnlyDictionary<string, byte[]> Files,
    IReadOnlyDictionary<string, TikzStaged>? Tikz = null)
{
    public static readonly StagedImages None = new(
        new Dictionary<string, string>(), new Dictionary<string, byte[]>());

    public bool Any => Files.Count > 0;
}

/// <summary>
/// A TikZ figure for the Typst preview, keyed by its source: the staged SVG's path inside the
/// compile directory, or why it did not draw (shown in place of the drawing).
/// </summary>
public sealed record TikzStaged(string? Path, string? Error);

public sealed class DocumentImageStager(
    LiliaDbContext db,
    IStorageService storage,
    ILogger<DocumentImageStager> logger,
    ITikzFigureService? tikz = null) : IDocumentImageStager
{
    /// <summary>The upload limit an asset already passed to exist.</summary>
    private const long MaxBytes = 10L * 1024 * 1024;

    public async Task<StagedImages> StageAsync(Guid documentId, CancellationToken ct = default)
    {
        var images = await StageImagesAsync(documentId, ct);
        if (tikz is null) return images;

        // TikZ figures: drawn server-side through the same cache as the figure.svg route,
        // placed by Typst as SVG images, so a document with TikZ keeps the fast preview.
        var tikzBlocks = await db.Blocks.AsNoTracking()
            .Where(b => b.DocumentId == documentId && (b.Type == "figure" || b.Type == "image"))
            .ToListAsync(ct);
        tikzBlocks = tikzBlocks.Where(b => Lilia.Core.Blocks.TikzFigure.IsTikz(b.Content.RootElement)).ToList();
        if (tikzBlocks.Count == 0) return images;

        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return images;

        // Each figure's theme (Index: its chapter), for the figures that name a theme colour.
        var themes = await TikzFigureThemes.LoadAsync(db, doc, ct);
        var paths = new Dictionary<string, string>(images.Paths, StringComparer.Ordinal);
        var files = new Dictionary<string, byte[]>(images.Files, StringComparer.Ordinal);
        var staged = new Dictionary<string, TikzStaged>(StringComparer.Ordinal);
        foreach (var block in tikzBlocks)
        {
            var source = Lilia.Core.Blocks.TikzFigure.Source(block.Content.RootElement);
            if (staged.ContainsKey(source)) continue;
            try
            {
                // One at a time: the service records each outcome through this scope's DbContext.
                var result = await tikz.RenderAsync(doc, block, $"preview:{documentId}", ct, themes.For(block.Id));
                if (result.Svg is { } svg)
                {
                    var local = $"figures/tikz-{TikzFigureService.Hash(source, "", "")[..16]}.svg";
                    files[local] = svg;
                    staged[source] = new TikzStaged(local, null);
                }
                else
                {
                    staged[source] = new TikzStaged(null, result.Error?.Message ?? "unknown error");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A figure that cannot be drawn costs that figure, not the preview.
                logger.LogWarning(ex, "[Stage] Could not draw TikZ figure {BlockId}", block.Id);
                staged[source] = new TikzStaged(null, "the server could not draw it");
            }
        }
        return new StagedImages(paths, files, staged);
    }

    private async Task<StagedImages> StageImagesAsync(Guid documentId, CancellationToken ct)
    {
        var assets = await db.Assets.AsNoTracking()
            .Where(a => a.DocumentId == documentId)
            .ToListAsync(ct);

        if (assets.Count == 0) return StagedImages.None;

        // Only the images a figure actually references. A document can carry
        // assets nothing points at any more, and staging those would cost the
        // compile time and memory for nothing.
        var referenced = await db.Blocks.AsNoTracking()
            .Where(b => b.DocumentId == documentId)
            .Where(b => b.Type == "figure" || b.Type == "image")
            .Select(b => b.Content)
            .ToListAsync(ct);

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var content in referenced)
        {
            if (content.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            if (content.RootElement.TryGetProperty("src", out var src)
                && src.ValueKind == System.Text.Json.JsonValueKind.String
                && src.GetString() is { Length: > 0 } value)
            {
                wanted.Add(value);
            }
        }

        if (wanted.Count == 0) return StagedImages.None;

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var src in wanted)
        {
            var asset = Match(assets, src);
            if (asset is null)
            {
                logger.LogInformation(
                    "[Stage] Figure src on document {DocId} matches no asset of that document; "
                    + "it will render as a placeholder. Remote URLs are not fetched.", documentId);
                continue;
            }

            if (asset.FileSize > MaxBytes) continue;

            var bytes = await ReadAsync(asset.StorageKey, ct);
            if (bytes is null || bytes.Length == 0) continue;

            // Extension matters: Typst picks its decoder from it.
            var ext = Path.GetExtension(asset.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ExtensionFor(asset.FileType);
            var local = $"figures/{asset.Id}{ext}";

            paths[src] = local;
            files[local] = bytes;
        }

        return new StagedImages(paths, files);
    }

    /// <summary>
    /// The asset a src refers to, matched on the stored URL and then on the
    /// storage key appearing inside it — a public URL can be rewritten (CDN
    /// host, signature) after the block was written, but the key does not
    /// move.
    /// </summary>
    private static Asset? Match(List<Asset> assets, string src) =>
        assets.FirstOrDefault(a => string.Equals(a.Url, src, StringComparison.Ordinal))
        ?? assets.FirstOrDefault(a => !string.IsNullOrEmpty(a.StorageKey)
                                      && src.Contains(a.StorageKey, StringComparison.Ordinal));

    private async Task<byte[]?> ReadAsync(string storageKey, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(storageKey)) return null;
        try
        {
            await using var stream = await storage.DownloadAsync(storageKey);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.Length is > 0 and <= MaxBytes ? buffer.ToArray() : null;
        }
        catch (Exception ex)
        {
            // Storage being unavailable costs a figure, not the document.
            logger.LogWarning(ex, "[Stage] Could not read asset {Key}; skipping the image.", storageKey);
            return null;
        }
    }

    private static string ExtensionFor(string? mime) => mime switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/svg+xml" => ".svg",
        "image/webp" => ".webp",
        _ => ".png",
    };
}
