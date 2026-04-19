using SkiaSharp;

namespace Lilia.Api.Services;

public interface IAssetOptimizer
{
    /// <summary>
    /// Inspect + optionally resize and re-encode an uploaded asset. Returns
    /// the bytes to actually store. Non-images and already-small images are
    /// passed through. A diagnostic result accompanies the decision.
    /// </summary>
    AssetOptimizationResult Optimize(byte[] input, string contentType);
}

public record AssetOptimizationResult(
    byte[] Bytes,
    string ContentType,
    int? Width,
    int? Height,
    bool WasResized,
    bool WasRecompressed,
    long OriginalSize,
    long OptimizedSize,
    string? Notice);

/// <summary>
/// Validation + dynamic optimisation for uploaded asset bytes.
///
/// Caps (see AssetLimits):
///   - RejectOverBytes: hard reject (413) if raw > this — stops zip bombs
///     and absurd uploads before we even decode.
///   - TargetBytes: optimiser aims to land at-or-below this.
///   - MaxDimension: any image wider/taller than this is downscaled
///     proportionally.
///
/// Strategy:
///   - JPEG / WEBP / HEIC / HEIF     → re-encode as JPEG q=85 (small + broad support).
///   - PNG with alpha                 → keep as PNG (lossless re-encode may shrink).
///   - PNG without alpha              → flatten to JPEG q=88 (much smaller).
///   - GIF                            → pass through (animations would break).
///   - Other MIME (pdf, svg, etc.)    → pass through but still size-check.
///
/// All decoding is via SkiaSharp (already used in Lilia.Import).
/// </summary>
public class AssetOptimizerService : IAssetOptimizer
{
    public static class AssetLimits
    {
        public const long RejectOverBytes = 20 * 1024 * 1024;   // 20 MB
        public const long TargetBytes     =  2 * 1024 * 1024;   //  2 MB
        public const int  MaxDimension    = 2400;               // px longest side
        public const int  JpegQuality     = 85;
    }

    private readonly ILogger<AssetOptimizerService> _logger;

    public AssetOptimizerService(ILogger<AssetOptimizerService> logger)
    {
        _logger = logger;
    }

    public AssetOptimizationResult Optimize(byte[] input, string contentType)
    {
        var originalSize = input.LongLength;

        if (originalSize > AssetLimits.RejectOverBytes)
        {
            throw new AssetTooLargeException(
                $"File exceeds {AssetLimits.RejectOverBytes / (1024 * 1024)} MB upload cap (was {originalSize / (1024 * 1024)} MB).");
        }

        var mime = (contentType ?? string.Empty).ToLowerInvariant();
        if (!IsImageMime(mime))
        {
            // Non-image pass-through. Still returns the original bytes + no
            // dimension metadata — upstream decides what to do.
            return new AssetOptimizationResult(
                input, contentType, null, null,
                WasResized: false, WasRecompressed: false,
                OriginalSize: originalSize, OptimizedSize: originalSize,
                Notice: null);
        }

        // GIF pass-through to preserve animation.
        if (mime == "image/gif")
        {
            return new AssetOptimizationResult(
                input, contentType, null, null,
                WasResized: false, WasRecompressed: false,
                OriginalSize: originalSize, OptimizedSize: originalSize,
                Notice: originalSize > AssetLimits.TargetBytes
                    ? $"GIF ({originalSize / 1024} KB) left unoptimized to preserve animation."
                    : null);
        }

        // Decode. SkiaSharp returns null on unsupported / corrupt data.
        using var input_data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(input_data);
        if (codec is null)
        {
            _logger.LogWarning("Asset optimizer: could not decode image of type {Mime}", mime);
            return new AssetOptimizationResult(
                input, contentType, null, null,
                WasResized: false, WasRecompressed: false,
                OriginalSize: originalSize, OptimizedSize: originalSize,
                Notice: "Image could not be decoded for optimisation — stored as-is.");
        }

        var info = codec.Info;
        var origW = info.Width;
        var origH = info.Height;
        var hasAlpha = info.AlphaType != SKAlphaType.Opaque;

        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
        {
            return new AssetOptimizationResult(
                input, contentType, origW, origH,
                WasResized: false, WasRecompressed: false,
                OriginalSize: originalSize, OptimizedSize: originalSize,
                Notice: "Decoder failed — stored as-is.");
        }

        var (newW, newH) = ClampDimensions(origW, origH, AssetLimits.MaxDimension);
        var wasResized = newW != origW || newH != origH;

        SKBitmap working = bitmap;
        SKBitmap? resized = null;
        try
        {
            if (wasResized)
            {
                resized = bitmap.Resize(new SKImageInfo(newW, newH), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized != null) working = resized;
            }

            // Decide output format.
            var outputJpeg = !(mime == "image/png" && hasAlpha);
            using var image = SKImage.FromBitmap(working);
            byte[] outBytes;
            string outMime;
            if (outputJpeg)
            {
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, AssetLimits.JpegQuality);
                outBytes = encoded.ToArray();
                outMime = "image/jpeg";
            }
            else
            {
                using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100);
                outBytes = encoded.ToArray();
                outMime = "image/png";
            }

            // Don't bloat the result. If optimisation made things bigger
            // (sometimes happens for already-compressed JPEGs at low
            // quality source), fall back to the original.
            if (outBytes.Length >= originalSize && !wasResized)
            {
                return new AssetOptimizationResult(
                    input, contentType, origW, origH,
                    WasResized: false, WasRecompressed: false,
                    OriginalSize: originalSize, OptimizedSize: originalSize,
                    Notice: null);
            }

            var notice = BuildNotice(origW, origH, newW, newH, originalSize, outBytes.LongLength,
                                     wasResized, recompressed: true, changedMime: outMime != mime);

            return new AssetOptimizationResult(
                outBytes, outMime, newW, newH,
                WasResized: wasResized,
                WasRecompressed: true,
                OriginalSize: originalSize,
                OptimizedSize: outBytes.LongLength,
                Notice: notice);
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static bool IsImageMime(string mime)
        => mime.StartsWith("image/", StringComparison.Ordinal);

    private static (int W, int H) ClampDimensions(int w, int h, int maxSide)
    {
        if (w <= maxSide && h <= maxSide) return (w, h);
        double scale = (double)maxSide / Math.Max(w, h);
        return ((int)Math.Round(w * scale), (int)Math.Round(h * scale));
    }

    private static string BuildNotice(
        int origW, int origH, int newW, int newH,
        long origSize, long newSize,
        bool resized, bool recompressed, bool changedMime)
    {
        var parts = new List<string>();
        if (resized) parts.Add($"resized {origW}×{origH} → {newW}×{newH}");
        if (recompressed) parts.Add($"re-encoded {origSize / 1024} KB → {newSize / 1024} KB");
        if (changedMime) parts.Add("converted to JPEG");
        return parts.Count == 0 ? "" : "Optimized: " + string.Join(", ", parts) + ".";
    }
}

public class AssetTooLargeException : Exception
{
    public AssetTooLargeException(string message) : base(message) { }
}
