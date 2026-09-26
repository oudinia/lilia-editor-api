using System.Text;
using System.Text.Json;

namespace Lilia.Api.E2E.Tests.LatexCoverage;

/// <summary>
/// Appends one JSON line per feature × layer to <c>$LATEX_COVERAGE_OUT</c>
/// (no-op when unset). The coverage report is built from that file.
///
/// <para>Parallel-safe: xUnit runs the per-category classes concurrently, so
/// writes are serialised in-process by a lock, and each line goes out as a
/// single append-mode write (O_APPEND) with shared access, so a second process
/// appending to the same file cannot interleave inside a line.</para>
/// </summary>
public static class CoverageResultWriter
{
    private static readonly object Gate = new();
    private static readonly string? OutPath = Environment.GetEnvironmentVariable("LATEX_COVERAGE_OUT");

    public static void Write(string feature, string layer, string status, string detail)
    {
        if (string.IsNullOrWhiteSpace(OutPath)) return;

        var line = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["suite"] = "api",
            ["feature"] = feature,
            ["layer"] = layer,
            ["status"] = status,
            ["detail"] = detail,
        }) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        lock (Gate)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(OutPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = new FileStream(OutPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            fs.Write(bytes, 0, bytes.Length);
        }
    }
}
