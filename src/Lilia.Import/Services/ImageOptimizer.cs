using System.Security.Cryptography;

namespace Lilia.Import.Services;

/// <summary>
/// Image optimization service for reducing file sizes during import.
/// Uses SkiaSharp for cross-platform image processing.
/// </summary>
public class ImageOptimizer
{
    private readonly ImageOptimizationOptions _options;

    public ImageOptimizer(ImageOptimizationOptions? options = null)
    {
        _options = options ?? new ImageOptimizationOptions();
    }

    /// <summary>
    /// Optimizes an image based on the configured options.
    /// Returns the optimized image data and metadata about the optimization.
    /// </summary>
    public ImageOptimizationResult Optimize(byte[] imageData, string mimeType, string? filename = null)
    {
        var result = new ImageOptimizationResult
        {
            OriginalSize = imageData.Length,
            OriginalMimeType = mimeType
        };

        // Skip optimization if disabled or below threshold
        if (!_options.EnableOptimization || imageData.Length < _options.OptimizeThresholdBytes)
        {
            result.OptimizedData = imageData;
            result.OptimizedSize = imageData.Length;
            result.OptimizedMimeType = mimeType;
            result.WasOptimized = false;
            result.Reason = imageData.Length < _options.OptimizeThresholdBytes
                ? "Below size threshold"
                : "Optimization disabled";
            return result;
        }

        try
        {
            // Use SkiaSharp for image processing
            using var inputStream = new MemoryStream(imageData);
            using var original = SkiaSharp.SKBitmap.Decode(inputStream);

            if (original == null)
            {
                result.OptimizedData = imageData;
                result.OptimizedSize = imageData.Length;
                result.OptimizedMimeType = mimeType;
                result.WasOptimized = false;
                result.Reason = "Could not decode image";
                return result;
            }

            result.OriginalWidth = original.Width;
            result.OriginalHeight = original.Height;

            // Determine if we need to resize
            var (targetWidth, targetHeight) = CalculateTargetDimensions(original.Width, original.Height);
            var needsResize = targetWidth != original.Width || targetHeight != original.Height;

            // Determine output format
            var outputFormat = DetermineOutputFormat(mimeType, original);
            var outputMimeType = outputFormat == SkiaSharp.SKEncodedImageFormat.Jpeg ? "image/jpeg" : mimeType;

            // Process the image
            SkiaSharp.SKBitmap processed = original;
            if (needsResize)
            {
                var samplingOptions = new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell);
                processed = original.Resize(new SkiaSharp.SKImageInfo(targetWidth, targetHeight), samplingOptions);
                result.OptimizedWidth = targetWidth;
                result.OptimizedHeight = targetHeight;
            }
            else
            {
                result.OptimizedWidth = original.Width;
                result.OptimizedHeight = original.Height;
            }

            // Encode with appropriate quality
            using var image = SkiaSharp.SKImage.FromBitmap(processed);
            var quality = outputFormat == SkiaSharp.SKEncodedImageFormat.Jpeg ? _options.JpegQuality : 100;
            using var encoded = image.Encode(outputFormat, quality);

            var optimizedData = encoded.ToArray();

            // Only use optimized version if it's actually smaller
            if (optimizedData.Length < imageData.Length * 0.95) // At least 5% savings
            {
                result.OptimizedData = optimizedData;
                result.OptimizedSize = optimizedData.Length;
                result.OptimizedMimeType = outputMimeType;
                result.WasOptimized = true;
                result.Reason = BuildOptimizationReason(needsResize, outputFormat != GetOriginalFormat(mimeType));
            }
            else
            {
                result.OptimizedData = imageData;
                result.OptimizedSize = imageData.Length;
                result.OptimizedMimeType = mimeType;
                result.WasOptimized = false;
                result.Reason = "Optimization did not reduce size significantly";
            }

            // Clean up resized bitmap if we created one
            if (needsResize && processed != original)
            {
                processed.Dispose();
            }
        }
        catch (Exception ex)
        {
            // If optimization fails, return original
            result.OptimizedData = imageData;
            result.OptimizedSize = imageData.Length;
            result.OptimizedMimeType = mimeType;
            result.WasOptimized = false;
            result.Reason = $"Optimization failed: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Analyzes an image without optimizing it.
    /// Useful for showing potential savings in the preview.
    /// </summary>
    public ImageAnalysis Analyze(byte[] imageData, string mimeType)
    {
        var analysis = new ImageAnalysis
        {
            CurrentSize = imageData.Length,
            MimeType = mimeType
        };

        try
        {
            using var inputStream = new MemoryStream(imageData);
            using var bitmap = SkiaSharp.SKBitmap.Decode(inputStream);

            if (bitmap != null)
            {
                analysis.Width = bitmap.Width;
                analysis.Height = bitmap.Height;
                analysis.IsPhoto = DetectIfPhoto(bitmap, mimeType);

                // Estimate optimized size
                var (targetWidth, targetHeight) = CalculateTargetDimensions(bitmap.Width, bitmap.Height);
                var dimensionRatio = (double)(targetWidth * targetHeight) / (bitmap.Width * bitmap.Height);

                // Rough estimation based on JPEG compression
                if (analysis.IsPhoto || mimeType == "image/jpeg")
                {
                    // Photos compress well with JPEG
                    var qualityFactor = _options.JpegQuality / 100.0;
                    analysis.EstimatedOptimizedSize = (long)(imageData.Length * dimensionRatio * qualityFactor * 0.7);
                }
                else
                {
                    // PNGs with graphics don't compress as well
                    analysis.EstimatedOptimizedSize = (long)(imageData.Length * dimensionRatio * 0.85);
                }

                analysis.PotentialSavings = imageData.Length - analysis.EstimatedOptimizedSize;
                analysis.PotentialSavingsPercent = (double)analysis.PotentialSavings / imageData.Length * 100;
                analysis.WouldBenefit = analysis.PotentialSavingsPercent > 10;
            }
        }
        catch
        {
            // If analysis fails, assume no optimization benefit
            analysis.EstimatedOptimizedSize = imageData.Length;
            analysis.WouldBenefit = false;
        }

        return analysis;
    }

    private (int width, int height) CalculateTargetDimensions(int originalWidth, int originalHeight)
    {
        if (_options.MaxDimension <= 0)
            return (originalWidth, originalHeight);

        var maxDim = Math.Max(originalWidth, originalHeight);
        if (maxDim <= _options.MaxDimension)
            return (originalWidth, originalHeight);

        var scale = (double)_options.MaxDimension / maxDim;
        return ((int)(originalWidth * scale), (int)(originalHeight * scale));
    }

    private SkiaSharp.SKEncodedImageFormat DetermineOutputFormat(string mimeType, SkiaSharp.SKBitmap bitmap)
    {
        // If already JPEG, keep as JPEG
        if (mimeType == "image/jpeg" || mimeType == "image/jpg")
            return SkiaSharp.SKEncodedImageFormat.Jpeg;

        // If PNG and user wants to convert photos to JPEG
        if (_options.ConvertPhotosToJpeg && mimeType == "image/png")
        {
            if (DetectIfPhoto(bitmap, mimeType))
                return SkiaSharp.SKEncodedImageFormat.Jpeg;
        }

        // Keep original format
        return GetOriginalFormat(mimeType);
    }

    private static SkiaSharp.SKEncodedImageFormat GetOriginalFormat(string mimeType)
    {
        return mimeType switch
        {
            "image/jpeg" or "image/jpg" => SkiaSharp.SKEncodedImageFormat.Jpeg,
            "image/png" => SkiaSharp.SKEncodedImageFormat.Png,
            "image/gif" => SkiaSharp.SKEncodedImageFormat.Gif,
            "image/webp" => SkiaSharp.SKEncodedImageFormat.Webp,
            "image/bmp" => SkiaSharp.SKEncodedImageFormat.Bmp,
            _ => SkiaSharp.SKEncodedImageFormat.Png
        };
    }

    private bool DetectIfPhoto(SkiaSharp.SKBitmap bitmap, string mimeType)
    {
        // JPEG is almost always a photo
        if (mimeType == "image/jpeg" || mimeType == "image/jpg")
            return true;

        // For PNG, analyze color variance to detect if it's a photo vs graphic
        // Photos have high color variance, graphics/screenshots have low variance
        try
        {
            var sampleSize = Math.Min(100, Math.Min(bitmap.Width, bitmap.Height));
            var stepX = bitmap.Width / sampleSize;
            var stepY = bitmap.Height / sampleSize;

            var colors = new HashSet<uint>();
            for (int y = 0; y < bitmap.Height && colors.Count < 1000; y += stepY)
            {
                for (int x = 0; x < bitmap.Width && colors.Count < 1000; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    // Quantize to reduce noise (group similar colors)
                    var quantized = ((uint)(pixel.Red / 16) << 8) |
                                   ((uint)(pixel.Green / 16) << 4) |
                                   (uint)(pixel.Blue / 16);
                    colors.Add(quantized);
                }
            }

            // Photos typically have many unique colors, graphics have few
            return colors.Count > 500;
        }
        catch
        {
            return false;
        }
    }

    private string BuildOptimizationReason(bool wasResized, bool wasConverted)
    {
        var reasons = new List<string>();
        if (wasResized) reasons.Add("resized");
        if (wasConverted) reasons.Add("converted to JPEG");
        reasons.Add("compressed");
        return string.Join(", ", reasons);
    }
}

/// <summary>
/// Configuration options for image optimization.
/// </summary>
public class ImageOptimizationOptions
{
    /// <summary>
    /// Whether to enable image optimization. Default: true
    /// </summary>
    public bool EnableOptimization { get; set; } = true;

    /// <summary>
    /// Maximum dimension (width or height) for images.
    /// Images larger than this will be resized proportionally.
    /// Set to 0 to disable resizing. Default: 1920 (Full HD)
    /// </summary>
    public int MaxDimension { get; set; } = 1920;

    /// <summary>
    /// JPEG quality (0-100). Higher = better quality, larger files.
    /// Default: 85 (good balance of quality and size)
    /// </summary>
    public int JpegQuality { get; set; } = 85;

    /// <summary>
    /// Only optimize images larger than this threshold in bytes.
    /// Default: 102400 (100 KB)
    /// </summary>
    public int OptimizeThresholdBytes { get; set; } = 100 * 1024;

    /// <summary>
    /// Whether to convert PNG photos to JPEG for better compression.
    /// Screenshots and graphics with transparency are kept as PNG.
    /// Default: true
    /// </summary>
    public bool ConvertPhotosToJpeg { get; set; } = true;

    /// <summary>
    /// Preset configurations for common use cases.
    /// </summary>
    public static ImageOptimizationOptions Original => new()
    {
        EnableOptimization = false
    };

    public static ImageOptimizationOptions High => new()
    {
        MaxDimension = 2560,
        JpegQuality = 92,
        OptimizeThresholdBytes = 200 * 1024
    };

    public static ImageOptimizationOptions Balanced => new()
    {
        MaxDimension = 1920,
        JpegQuality = 85,
        OptimizeThresholdBytes = 100 * 1024
    };

    public static ImageOptimizationOptions Compact => new()
    {
        MaxDimension = 1280,
        JpegQuality = 75,
        OptimizeThresholdBytes = 50 * 1024
    };

    public static ImageOptimizationOptions Minimal => new()
    {
        MaxDimension = 800,
        JpegQuality = 65,
        OptimizeThresholdBytes = 20 * 1024
    };
}

/// <summary>
/// Result of an image optimization operation.
/// </summary>
public class ImageOptimizationResult
{
    public byte[] OptimizedData { get; set; } = [];
    public long OriginalSize { get; set; }
    public long OptimizedSize { get; set; }
    public string OriginalMimeType { get; set; } = "";
    public string OptimizedMimeType { get; set; } = "";
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int OptimizedWidth { get; set; }
    public int OptimizedHeight { get; set; }
    public bool WasOptimized { get; set; }
    public string Reason { get; set; } = "";

    public long BytesSaved => OriginalSize - OptimizedSize;
    public double PercentSaved => OriginalSize > 0 ? (double)BytesSaved / OriginalSize * 100 : 0;
    public string SizeSummary => $"{FormatSize(OriginalSize)} → {FormatSize(OptimizedSize)} ({PercentSaved:F0}% saved)";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}

/// <summary>
/// Analysis of an image's optimization potential.
/// </summary>
public class ImageAnalysis
{
    public long CurrentSize { get; set; }
    public long EstimatedOptimizedSize { get; set; }
    public long PotentialSavings { get; set; }
    public double PotentialSavingsPercent { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string MimeType { get; set; } = "";
    public bool IsPhoto { get; set; }
    public bool WouldBenefit { get; set; }
}
