using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Lilia.Import.Services;

/// <summary>
/// One scanned page (a JPEG or PNG) as a one-page PDF, for Mathpix's <c>POST /v3/pdf</c> (figures in imported scans,
/// 20 Nov 2026). <c>/v3/text</c>, the image route until then, reads a page's text and maths but "does not crop sub-regions
/// or upload them to our CDN" (docs.mathpix.com/reference/post-v3-text): the two sketches of the launch bot's handwritten
/// page were lost. <c>/v3/pdf</c> crops each figure, into the tex.zip and the Markdown alike, so a scan goes the PDF's way.
///
/// <para>A JPEG is embedded as it is (DCTDecode): no re-encoding. A PNG is decoded, its transparency flattened onto white,
/// and embedded losslessly (FlateDecode RGB). The page is the image at 96 dpi, as img2pdf makes it by default.</para>
/// </summary>
public static class ImagePdf
{
    private const double Dpi = 96;

    /// <summary>The PDF, or null when the image can't be read (a damaged file).</summary>
    public static byte[]? Wrap(byte[] image, string contentType) => contentType switch
    {
        "image/jpeg" => JpegSize(image) is { } jpeg ? Write(jpeg.Width, jpeg.Height, ColourSpace(jpeg.Components), "/DCTDecode", image,
            jpeg.Components == 4 ? " /Decode [1 0 1 0 1 0 1 0]" : "") : null,
        "image/png" => PngRgb(image) is { } png ? Write(png.Width, png.Height, "/DeviceRGB", "/FlateDecode", Deflate(png.Rgb), "") : null,
        _ => null,
    };

    private static string ColourSpace(int components) => components switch
    {
        1 => "/DeviceGray",
        4 => "/DeviceCMYK",
        _ => "/DeviceRGB",
    };

    /// <summary>A JPEG's size and colour components, from its frame header (SOF0 to SOF15, not DHT, JPG or DAC).</summary>
    internal static (int Width, int Height, int Components)? JpegSize(byte[] jpeg)
    {
        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF) { i++; continue; }
            var marker = jpeg[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
            var length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                var height = (jpeg[i + 5] << 8) | jpeg[i + 6];
                var width = (jpeg[i + 7] << 8) | jpeg[i + 8];
                var components = jpeg[i + 9];
                return width > 0 && height > 0 && components is 1 or 3 or 4 ? (width, height, components) : null;
            }
            if (length < 2) return null;
            i += 2 + length;
        }
        return null;
    }

    /// <summary>A PNG's pixels as RGB, transparency flattened onto white.</summary>
    private static (int Width, int Height, byte[] Rgb)? PngRgb(byte[] png)
    {
        // A file Skia can't open at all has no codec, and Decode(byte[]) throws rather than answering null.
        using var codec = SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(png));
        if (codec is null) return null;
        using var decoded = SkiaSharp.SKBitmap.Decode(codec);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0) return null;
        using var flat = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(decoded.Width, decoded.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
        using (var canvas = new SkiaSharp.SKCanvas(flat))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.DrawBitmap(decoded, 0, 0);
        }
        var rgba = flat.Bytes;
        var rgb = new byte[decoded.Width * decoded.Height * 3];
        for (int p = 0, q = 0; q < rgb.Length; p += 4, q += 3)
        {
            rgb[q] = rgba[p];
            rgb[q + 1] = rgba[p + 1];
            rgb[q + 2] = rgba[p + 2];
        }
        return (decoded.Width, decoded.Height, rgb);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return output.ToArray();
    }

    private static byte[] Write(int width, int height, string colourSpace, string filter, byte[] data, string extra)
    {
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var w = width * 72 / Dpi;
        var h = height * 72 / Dpi;
        var content = Encoding.ASCII.GetBytes($"q {F(w)} 0 0 {F(h)} 0 0 cm /Im0 Do Q\n");

        using var pdf = new MemoryStream();
        var offsets = new long[6];
        void Ascii(string s) => pdf.Write(Encoding.ASCII.GetBytes(s));
        void Obj(int n, string dictionary, byte[]? stream = null)
        {
            offsets[n] = pdf.Position;
            Ascii($"{n} 0 obj\n{dictionary}\n");
            if (stream is not null)
            {
                Ascii("stream\n");
                pdf.Write(stream);
                Ascii("\nendstream\n");
            }
            Ascii("endobj\n");
        }

        Ascii("%PDF-1.4\n");
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(w)} {F(h)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
        Obj(4, $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace {colourSpace} /BitsPerComponent 8 /Filter {filter}{extra} /Length {data.Length} >>", data);
        Obj(5, $"<< /Length {content.Length} >>", content);
        var xref = pdf.Position;
        Ascii("xref\n0 6\n0000000000 65535 f \n");
        for (var n = 1; n <= 5; n++) Ascii($"{offsets[n]:D10} 00000 n \n");
        Ascii($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }
}
