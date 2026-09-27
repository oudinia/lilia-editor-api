using System.IO.Compression;
using System.Text;

namespace Lilia.Api.Tests.Security;

/// <summary>
/// Zip bombs made on the spot: tiny archives whose entries decompress to far
/// more than they weigh. Spaces compress about 1,000:1, like zeros, and are
/// still whitespace to an XML reader.
/// </summary>
public static class ZipBombs
{
    public const long MB = 1024 * 1024;

    /// <summary>Stream <paramref name="bytes"/> of spaces into an entry, then an XML root.</summary>
    public static void PutPadded(ZipArchive zip, string name, long bytes, string tail = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>x</p></body></html>")
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        using var s = entry.Open();
        var chunk = Encoding.ASCII.GetBytes(new string(' ', 1 << 20));
        for (long left = bytes; left > 0; left -= chunk.Length)
            s.Write(chunk, 0, (int)Math.Min(chunk.Length, left));
        var t = Encoding.UTF8.GetBytes(tail);
        s.Write(t, 0, t.Length);
    }

    public static void Put(ZipArchive zip, string name, string text, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    /// <summary>
    /// A well-formed .epub whose one chapter decompresses to
    /// <paramref name="chapterBytes"/> — it weighs tens of kilobytes.
    /// </summary>
    public static byte[] Epub(long chapterBytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            Put(zip, "META-INF/container.xml",
                "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
            Put(zip, "OEBPS/content.opf",
                "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\">" +
                "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Bomb</dc:title><dc:identifier id=\"id\">x</dc:identifier><dc:language>en</dc:language></metadata>" +
                "<manifest><item id=\"c1\" href=\"chapter.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>" +
                "<spine><itemref idref=\"c1\"/></spine></package>");
            PutPadded(zip, "OEBPS/chapter.xhtml", chapterBytes);
        }
        return ms.ToArray();
    }

    /// <summary>A .docx-shaped package whose document.xml decompresses to <paramref name="bytes"/>.</summary>
    public static byte[] Docx(long bytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            PutPadded(zip, "word/document.xml", bytes,
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body/></w:document>");
        }
        return ms.ToArray();
    }

    /// <summary>A LaTeX project .zip with a main.tex and one padded file.</summary>
    public static byte[] LatexProject(string paddedName, long bytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "main.tex", "\\documentclass{article}\n\\begin{document}\nHello.\n\\end{document}\n");
            PutPadded(zip, paddedName, bytes, "% end");
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Rewrite an entry's declared uncompressed size (local and central
    /// headers) — a header that lies, so only counting real bytes catches it.
    /// </summary>
    public static byte[] LieAboutSize(byte[] zip, string entryName, uint declared)
    {
        var b = (byte[])zip.Clone();
        var name = Encoding.UTF8.GetBytes(entryName);
        for (var i = 0; i + 46 < b.Length; i++)
        {
            var sig = BitConverter.ToUInt32(b, i);
            if (sig == 0x04034b50 && NameAt(b, i + 30, BitConverter.ToUInt16(b, i + 26), name))
                BitConverter.GetBytes(declared).CopyTo(b, i + 22);
            else if (sig == 0x02014b50 && NameAt(b, i + 46, BitConverter.ToUInt16(b, i + 28), name))
                BitConverter.GetBytes(declared).CopyTo(b, i + 24);
        }
        return b;
    }

    private static bool NameAt(byte[] b, int at, int len, byte[] name) =>
        len == name.Length && at + len <= b.Length && b.AsSpan(at, len).SequenceEqual(name);
}
