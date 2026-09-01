namespace Lilia.Api.Tests.Services;

/// <summary>
/// Locates the typst binary for the tests that need a real compile, and
/// refuses to let their absence pass quietly.
///
/// <para>Both Typst suites used to guard with <c>if (!TypstAvailable()) return;</c>.
/// A bare <c>return</c> is not a skip — xUnit counts it as a <b>pass</b> — so on a
/// machine without typst, 78 combinatorial fixtures and 5 compile facts reported
/// green while executing nothing. That is exactly the failure mode this suite
/// exists to catch: plausible output, something not done, nobody told.</para>
///
/// <para>It was found the only way it could be: installing typst on a new
/// machine turned the fixtures on for the first time, and two of them failed
/// immediately. Until then the migration handoff's baseline of "2626 passed"
/// counted 83 tests that never ran.</para>
///
/// <para><see cref="Require"/> therefore <b>fails loudly</b>, matching the
/// decision already taken for the LaTeX integration tests: xUnit 2.9 has no
/// runtime skip, and a skip would hide these on the one surface where they are
/// visible, since no workflow runs <c>dotnet test</c>. Revisit when CI exists
/// and can guarantee the binary.</para>
/// </summary>
internal static class TypstEnvironment
{
    /// <summary>Places a typst binary is expected, in order of preference.</summary>
    private static readonly string[] ProbeDescriptions =
    [
        "$TYPST_BINARY",
        "~/.local/bin/typst",
        "/usr/local/bin/typst",
        "/usr/bin/typst",
        "anything named 'typst' on $PATH",
    ];

    /// <summary>True when a typst binary can be found.</summary>
    internal static bool IsAvailable() => Locate() is not null;

    /// <summary>
    /// Throws unless typst is installed. Named for what it does — the test
    /// cannot run without it, so it does not pretend otherwise.
    /// </summary>
    internal static void Require()
    {
        if (IsAvailable()) return;

        throw new TestEnvironmentUnavailableException(
            "FAILED, not skipped: typst is not installed, so this test could not run. Looked for "
            + string.Join(", ", ProbeDescriptions)
            + ". Install it with `curl -sSL https://github.com/typst/typst/releases/latest/download/"
            + "typst-x86_64-unknown-linux-musl.tar.xz | tar -xJ` and place the binary at "
            + "~/.local/bin/typst, or set TYPST_BINARY to one you have.");
    }

    /// <summary>Full path to the typst binary, or null when there is none.</summary>
    private static string? Locate()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        string[] candidates =
        [
            Environment.GetEnvironmentVariable("TYPST_BINARY") ?? "",
            string.IsNullOrEmpty(home) ? "" : Path.Combine(home, ".local", "bin", "typst"),
            "/usr/local/bin/typst",
            "/usr/bin/typst",
        ];

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return candidate;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        return pathEnv.Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir, "typst"))
            .FirstOrDefault(File.Exists);
    }
}
