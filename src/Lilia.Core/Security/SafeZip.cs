using System.IO.Compression;

namespace Lilia.Core.Security;

/// <summary>
/// Limits for reading a zip someone uploaded — an .epub, a .docx, a LaTeX
/// project. Generous for real files; a "zip bomb" (a small upload whose
/// entries decompress to gigabytes) runs into them long before memory does.
/// Bound from configuration section <c>ZipLimits</c>.
/// </summary>
public sealed class ZipLimitsOptions
{
    public const string Section = "ZipLimits";

    /// <summary>Most entries an archive may hold.</summary>
    public int MaxEntries { get; set; } = 2_000;

    /// <summary>Most bytes one entry may decompress to.</summary>
    public long MaxEntryBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Most bytes all entries together may decompress to.</summary>
    public long MaxTotalBytes { get; set; } = 300L * 1024 * 1024;

    /// <summary>
    /// Highest uncompressed/compressed ratio allowed for an entry bigger than
    /// <see cref="RatioFloorBytes"/>. Book text and Word XML compress 3–20×; a
    /// bomb of zeros compresses about 1,000×.
    /// </summary>
    public double MaxCompressionRatio { get; set; } = 100;

    /// <summary>Entries smaller than this are not ratio-checked (tiny files compress wildly).</summary>
    public long RatioFloorBytes { get; set; } = 1L * 1024 * 1024;

    /// <summary>
    /// The limits in force. Program binds the <see cref="Section"/> config
    /// onto this instance at startup, so code without DI (the import
    /// library's parsers) reads the same values as injected services.
    /// </summary>
    public static ZipLimitsOptions Default { get; } = new();

    /// <summary>A copy with a smaller per-entry cap (e.g. a LaTeX project's 5 MB per file).</summary>
    public ZipLimitsOptions WithMaxEntryBytes(long maxEntryBytes) => new()
    {
        MaxEntries = MaxEntries,
        MaxEntryBytes = Math.Min(MaxEntryBytes, maxEntryBytes),
        MaxTotalBytes = MaxTotalBytes,
        MaxCompressionRatio = MaxCompressionRatio,
        RatioFloorBytes = RatioFloorBytes,
    };
}

/// <summary>
/// An uploaded archive broke a <see cref="ZipLimitsOptions"/> limit. Callers
/// answer it as bad input (400), with <see cref="UserMessage"/>; the detail
/// (which limit, which entry) is for the log.
/// </summary>
public sealed class UnsafeZipException(string detail) : Exception(detail)
{
    /// <summary>What an author is told: short, and the same whichever limit tripped.</summary>
    public const string UserMessage = "This file is too large or malformed to read.";

    /// <summary>The same, for an .epub.</summary>
    public const string BookMessage = "This book is too large or malformed to read.";
}

/// <summary>
/// Reads uploaded zips within <see cref="ZipLimitsOptions"/>: what the
/// central directory declares is checked up front (entry count, sizes,
/// compression ratio), and every entry is read through a stream that counts
/// what it actually decompresses.
/// </summary>
public static class SafeZip
{
    /// <summary>Check what the archive declares: entry count, sizes, ratios.</summary>
    public static void CheckHeaders(ZipArchive zip, ZipLimitsOptions? limits = null)
    {
        limits ??= ZipLimitsOptions.Default;
        CheckEntryCount(zip, limits);
        long total = 0;
        foreach (var e in zip.Entries)
        {
            if (e.Length > limits.MaxEntryBytes)
                throw new UnsafeZipException($"{e.FullName} declares {e.Length} bytes (limit {limits.MaxEntryBytes}).");
            total += e.Length;
            if (total > limits.MaxTotalBytes)
                throw new UnsafeZipException($"entries declare over {limits.MaxTotalBytes} bytes in total.");
            CheckRatio(e, limits);
        }
    }

    public static void CheckEntryCount(ZipArchive zip, ZipLimitsOptions limits)
    {
        if (zip.Entries.Count > limits.MaxEntries)
            throw new UnsafeZipException($"{zip.Entries.Count} entries (limit {limits.MaxEntries}).");
    }

    public static void CheckRatio(ZipArchiveEntry e, ZipLimitsOptions limits)
    {
        if (e.Length > limits.RatioFloorBytes && e.CompressedLength > 0
            && (double)e.Length / e.CompressedLength > limits.MaxCompressionRatio)
            throw new UnsafeZipException($"{e.FullName} compresses {e.Length / e.CompressedLength}:1 (limit {limits.MaxCompressionRatio}:1).");
    }

    /// <summary>
    /// Open an entry through a stream that stops at the per-entry limit and
    /// charges what it reads to <paramref name="budget"/>, the archive's total.
    /// </summary>
    /// <remarks>
    /// Defence in depth. .NET 10's entry streams already stop at the size the
    /// headers declare (a header claiming 1 KB yields 1 KB), so the declared
    /// sizes <see cref="CheckHeaders"/> checks are what bound a read today;
    /// this keeps holding if a runtime or library stops truncating.
    /// </remarks>
    public static Stream OpenBounded(ZipArchiveEntry entry, ZipBudget budget) =>
        Bound(entry.Open(), entry.FullName, budget);

    /// <summary>Wrap any decompressing stream in the entry and archive counters.</summary>
    public static Stream Bound(Stream inner, string name, ZipBudget budget) =>
        new BoundedReadStream(inner, name, budget);

    /// <summary>
    /// Read a whole archive through the counters without keeping anything —
    /// for handing a package to a library (the OpenXML SDK) that decompresses
    /// it itself. Leaves <paramref name="archive"/> at position 0.
    /// </summary>
    public static void Verify(Stream archive, ZipLimitsOptions? limits = null)
    {
        limits ??= ZipLimitsOptions.Default;
        var start = archive.CanSeek ? archive.Position : 0;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true))
        {
            CheckHeaders(zip, limits);
            var budget = new ZipBudget(limits);
            var sink = new byte[81_920];
            foreach (var e in zip.Entries)
            {
                using var s = OpenBounded(e, budget);
                while (s.Read(sink, 0, sink.Length) > 0) { }
            }
        }
        if (archive.CanSeek) archive.Position = start;
    }

    /// <inheritdoc cref="Verify(Stream, ZipLimitsOptions?)"/>
    public static void VerifyFile(string path, ZipLimitsOptions? limits = null)
    {
        using var fs = File.OpenRead(path);
        Verify(fs, limits);
    }
}

/// <summary>What one archive may still decompress, shared by its entries.</summary>
public sealed class ZipBudget(ZipLimitsOptions limits)
{
    public ZipLimitsOptions Limits { get; } = limits;
    public long Used { get; private set; }

    internal void Charge(long bytes, string entryName)
    {
        Used += bytes;
        if (Used > Limits.MaxTotalBytes)
            throw new UnsafeZipException($"decompressed past {Limits.MaxTotalBytes} bytes in total (at {entryName}).");
    }
}

/// <summary>Counts what it reads; throws past the entry or archive limit.</summary>
internal sealed class BoundedReadStream(Stream inner, string name, ZipBudget budget) : Stream
{
    private long _read;

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        Count(await inner.ReadAsync(buffer, ct));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), ct));

    private int Count(int n)
    {
        _read += n;
        if (_read > budget.Limits.MaxEntryBytes)
            throw new UnsafeZipException($"{name} decompressed past {budget.Limits.MaxEntryBytes} bytes.");
        budget.Charge(n, name);
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
