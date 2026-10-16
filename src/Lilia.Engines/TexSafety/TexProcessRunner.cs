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
/// <item><b>Optional: a sandbox, no root needed.</b> With <c>LILIA_TEX_SANDBOX=bwrap</c> every process
/// runs in bubblewrap (16 Oct 2026): no network, its own process list, and a file system of only
/// /usr, the font configuration, the TeX tree (read-only), our precompiled formats (read-only) and
/// its own work directory. A document can then read nothing of the API's: not its home, its
/// secrets, nor <c>/proc/&lt;api pid&gt;/environ</c>. TeX's own <c>openin_any=p</c> was tried first
/// and does not stop reads by absolute path in TeX Live 2026. On Ubuntu 24.04 bubblewrap needs an
/// AppArmor profile allowing user namespaces (/etc/apparmor.d/bwrap).</item>
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

    /// <summary>LILIA_TEX_SANDBOX=bwrap: every process runs in a bubblewrap sandbox. Empty = off.</summary>
    public static bool Sandboxed =>
        string.Equals(Environment.GetEnvironmentVariable("LILIA_TEX_SANDBOX"), "bwrap", StringComparison.OrdinalIgnoreCase);

    /// <summary>The TeX tree's folders outside /usr, found once from kpathsea, plus LILIA_TEX_SANDBOX_RO (colon-separated).</summary>
    public static IReadOnlyList<string> SandboxReadOnly => TexTree.Value;
    private static readonly Lazy<IReadOnlyList<string>> TexTree = new(FindTexTree);

    public static ProcessStartInfo Build(string command, string arguments, string workingDir, string? runAs = null, bool isRoot = false,
        bool sandbox = false, IReadOnlyList<string>? readOnly = null)
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
        else if (sandbox)
        {
            file = "bwrap";
            args = SandboxArguments(workingDir, arguments, readOnly ?? []) + $" -- {command} {arguments}";
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
    /// bubblewrap's options: a new namespace for everything (network included), /tmp empty, and only
    /// what a compile needs bound in. The work directory is the one writable place; a format passed
    /// with -fmt= lives in our own cache and is bound read-only; an -output-directory elsewhere is
    /// bound writable.
    /// </summary>
    public static string SandboxArguments(string workingDir, string arguments, IReadOnlyList<string> readOnly)
    {
        static string Q(string p) => "\"" + p.Replace("\"", "\\\"") + "\"";
        static bool Under(string path, string dir) =>
            path == dir || path.StartsWith(dir.TrimEnd('/') + "/", StringComparison.Ordinal);

        var b = new List<string>
        {
            "--ro-bind /usr /usr", "--symlink usr/lib /lib", "--symlink usr/lib64 /lib64",
            "--symlink usr/bin /bin", "--symlink usr/sbin /sbin",
            "--ro-bind-try /etc/fonts /etc/fonts", "--ro-bind-try /etc/texmf /etc/texmf",
            "--proc /proc", "--dev /dev", "--tmpfs /tmp",
        };
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in readOnly)
            if (dir.StartsWith('/') && !Under(dir, "/usr") && bound.Add(dir)) b.Add($"--ro-bind-try {Q(dir)} {Q(dir)}");
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(arguments, "-fmt=([^\"\\s]+)"))
            if (Path.GetDirectoryName(m.Groups[1].Value) is { Length: > 0 } fmtDir && fmtDir.StartsWith('/')
                && !Under(fmtDir, workingDir) && bound.Add(fmtDir))
                b.Add($"--ro-bind-try {Q(fmtDir)} {Q(fmtDir)}");
        b.Add($"--bind {Q(workingDir)} {Q(workingDir)}");
        var outDir = System.Text.RegularExpressions.Regex.Match(arguments, "-output-directory[= ]\"?([^\"\\s]+)");
        if (outDir.Success && outDir.Groups[1].Value.StartsWith('/') && !Under(outDir.Groups[1].Value, workingDir))
            b.Add($"--bind {Q(outDir.Groups[1].Value)} {Q(outDir.Groups[1].Value)}");
        // LuaLaTeX looks for its font-name database only in the first writable cache, which in the
        // sandbox is the empty .texmf-var of the work directory: it rebuilt the database on every
        // compile (20 s instead of 2). The tree's database is shown there, read-only.
        foreach (var dir in readOnly)
        {
            var names = Path.Combine(dir, "luatex-cache", "generic", "names");
            if (dir.StartsWith('/') && Directory.Exists(names))
            {
                b.Add($"--ro-bind {Q(names)} {Q(Path.Combine(workingDir, ".texmf-var", "luatex-cache", "generic", "names"))}");
                break;
            }
        }
        b.Add($"--chdir {Q(workingDir)}");
        b.Add("--unshare-all --die-with-parent --new-session --cap-drop ALL");
        return string.Join(' ', b);
    }

    /// <summary>
    /// With the sandbox on: can bubblewrap start, and does the TeX tree resolve inside it? A failure
    /// here means every compile fails (an upgrade that drops the AppArmor profile, say), so the API
    /// says so at boot instead of leaving it to the first document.
    /// </summary>
    public static async Task<(bool Ok, string Detail)> SandboxSelfTestAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lilia-sandbox-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (exit, stdout, stderr) = await RunAsync("kpsewhich", "article.cls", dir, 20);
            return exit == 0 && stdout.Contains("article.cls", StringComparison.Ordinal)
                ? (true, stdout.Trim())
                : (false, $"exit {exit}: {stderr.Trim()}");
        }
        catch (Exception ex) { return (false, ex.Message); }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    private static IReadOnlyList<string> FindTexTree()
    {
        var dirs = new List<string>();
        foreach (var v in new[] { "TEXMFROOT", "TEXMFLOCAL", "TEXMFSYSVAR", "TEXMFSYSCONFIG", "TEXMFDIST" })
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("kpsewhich", $"-var-value={v}")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                if (p is null) continue;
                var value = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                if (value.StartsWith('/') && Directory.Exists(value)) dirs.Add(value);
            }
            catch { /* no kpsewhich: only /usr and the extras */ }
        }
        if (Environment.GetEnvironmentVariable("LILIA_TEX_SANDBOX_RO") is { Length: > 0 } extra)
            dirs.AddRange(extra.Split(':', StringSplitOptions.RemoveEmptyEntries));
        return dirs;
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

    /// <summary>
    /// Run the command; a timeout kills the process tree and throws <see cref="TimeoutException"/>.
    /// <paramref name="ct"/> (a request the caller gave up on, a newer draft) kills it too and
    /// throws <see cref="OperationCanceledException"/>, so an abandoned compile stops using the CPU.
    /// </summary>
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string command, string arguments, string workingDir, int timeoutSeconds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var runAs = RunAs;
        var isRoot = Environment.UserName == "root";
        if (!string.IsNullOrEmpty(runAs) && isRoot)
            MakeWritable(workingDir);

        var sandbox = Sandboxed && !(isRoot && !string.IsNullOrEmpty(runAs));
        using var process = new Process { StartInfo = Build(command, arguments, workingDir, runAs, isRoot, sandbox, sandbox ? SandboxReadOnly : null) };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var either = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, ct);
        try
        {
            await process.WaitForExitAsync(either.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { /* already gone */ }
            if (ct.IsCancellationRequested) throw new OperationCanceledException("The caller cancelled the process.", ct);
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
