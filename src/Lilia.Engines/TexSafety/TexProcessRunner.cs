using System.Diagnostics;
using System.Text;

namespace Lilia.Engines.TexSafety;

/// <summary>
/// The one place a TeX engine is started. It replaces two identical copies of
/// <c>RunProcessAsync</c> (one in the API's LaTeXRenderService, one in CompilationQueueService)
/// that started the engine as the API's own user with the API's whole environment.
///
/// <list type="bullet">
/// <item><b>Shell escape off, explicitly.</b> <c>-no-shell-escape</c> is added to every TeX engine
/// when missing. The main compile path relied on TeX Live's default of "restricted", which runs a
/// whitelist of commands.</item>
/// <item><b>A scrubbed environment.</b> The engine gets only what it needs (PATH, a HOME in the
/// work directory, TEXMF cache directories), never the API's configuration, keys or connection
/// strings.</item>
/// <item><b>Optional: a different user.</b> With <c>LILIA_TEX_RUN_AS</c> set (for example
/// <c>nobody</c>) and the API running as root, the engine is started through
/// <c>setpriv --reuid --regid --clear-groups</c>. That is what keeps a document from reading the
/// API's own files and <c>/proc/&lt;api pid&gt;/environ</c>, which a scan of the source cannot
/// guarantee (see <see cref="TexSourceGuard"/>). The work directory is made writable for that
/// user. Off by default: it needs root and must be enabled and tried in the deployment.</item>
/// </list>
/// </summary>
public static class TexProcessRunner
{
    private static readonly HashSet<string> TexEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdflatex", "xelatex", "lualatex", "latex", "pdftex", "xetex", "luatex", "lualatex-dev", "platex", "uplatex",
    };

    /// <summary>"nobody", "65534:65534", "texrun": empty = off.</summary>
    public static string? RunAs => Environment.GetEnvironmentVariable("LILIA_TEX_RUN_AS") is { Length: > 0 } v ? v : null;

    public static ProcessStartInfo Build(string command, string arguments, string workingDir, string? runAs = null, bool isRoot = false)
    {
        var name = Path.GetFileNameWithoutExtension(command);
        if (TexEngines.Contains(name) && !arguments.Contains("shell-escape", StringComparison.Ordinal))
            arguments = "-no-shell-escape " + arguments;

        var file = command;
        var args = arguments;
        if (!string.IsNullOrEmpty(runAs) && isRoot)
        {
            var (uid, gid) = ResolveRunAs(runAs);
            file = "setpriv";
            // --no-new-privs: the engine (and anything it starts) can never gain privileges back.
            // --inh-caps=-all: no inheritable capabilities survive the switch.
            args = $"--reuid={uid} --regid={gid} --clear-groups --no-new-privs --inh-caps=-all -- {command} {arguments}";
        }

        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.Environment.Clear();
        // PATH is kept as it is for finding the engine; nothing else of the parent's is passed on.
        psi.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        psi.Environment["HOME"] = workingDir;
        psi.Environment["TEXMFVAR"] = Path.Combine(workingDir, ".texmf-var");
        psi.Environment["TEXMFCONFIG"] = Path.Combine(workingDir, ".texmf-config");
        psi.Environment["TEXMFOUTPUT"] = workingDir;
        psi.Environment["LANG"] = "C.UTF-8";
        // TeX Live installed under a user's home (a development machine) finds its tree through these.
        foreach (var keep in new[] { "TEXMFHOME", "TEXMFDIST", "TEXMFLOCAL", "TEXMFSYSVAR", "TEXMFSYSCONFIG", "TEXINPUTS", "TEXFONTS", "TEXMFCNF" })
            if (Environment.GetEnvironmentVariable(keep) is { Length: > 0 } v) psi.Environment[keep] = v;
        return psi;
    }

    /// <summary>
    /// "uid:gid" as given, or a user name resolved to its uid and primary gid from the passwd file.
    /// The first version used the user name as the group too, and Debian and Ubuntu have no group
    /// called "nobody" (it is "nogroup", gid 65534), so <c>LILIA_TEX_RUN_AS=nobody</c> made setpriv,
    /// and so every compile, fail (5 Oct review). A name that cannot be resolved is kept as the user
    /// with the nobody group (65534), which exists on every Linux.
    /// </summary>
    public static (string Uid, string Gid) ResolveRunAs(string runAs, string passwdPath = "/etc/passwd")
    {
        var parts = runAs.Split(':');
        if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0) return (parts[0], parts[1]);
        var user = parts[0];
        try
        {
            foreach (var line in File.ReadLines(passwdPath))
            {
                var f = line.Split(':');
                if (f.Length >= 4 && (f[0] == user || f[2] == user)) return (f[2], f[3]);
            }
        }
        catch { /* unreadable passwd: fall through */ }
        return (user, "65534");
    }

    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string command, string arguments, string workingDir, int timeoutSeconds)
    {
        var runAs = RunAs;
        var isRoot = Environment.UserName == "root";
        if (!string.IsNullOrEmpty(runAs) && isRoot)
            MakeWritable(workingDir);

        using var process = new Process { StartInfo = Build(command, arguments, workingDir, runAs, isRoot) };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException($"Process timed out after {timeoutSeconds}s");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    // The work directory belongs to the API (root); the engine runs as someone else and has to write there.
    private static void MakeWritable(string dir)
    {
        try
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                      UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetUnixFileMode(f, File.GetUnixFileMode(f) | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
                File.SetUnixFileMode(d, File.GetUnixFileMode(d) | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
        }
        catch { /* best effort: the compile will say so if it cannot write */ }
    }
}
