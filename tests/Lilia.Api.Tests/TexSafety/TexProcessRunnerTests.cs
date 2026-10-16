using FluentAssertions;
using Lilia.Engines.TexSafety;
using Xunit;

namespace Lilia.Api.Tests.TexSafety;

/// <summary>The one place a TeX engine is started: shell escape off, a scrubbed environment, optional other user.</summary>
public class TexProcessRunnerTests
{
    [Theory]
    [InlineData("pdflatex")]
    [InlineData("xelatex")]
    [InlineData("lualatex")]
    [InlineData("/home/x/texlive/bin/pdflatex")]
    public void Every_TeX_engine_gets_no_shell_escape_when_it_has_none(string engine)
    {
        var psi = TexProcessRunner.Build(engine, "-interaction=nonstopmode doc.tex", "/tmp/w");
        psi.Arguments.Should().StartWith("-no-shell-escape ").And.Contain("doc.tex");
    }

    [Fact]
    public void An_explicit_shell_escape_flag_is_not_doubled()
    {
        TexProcessRunner.Build("pdflatex", "--no-shell-escape doc.tex", "/tmp/w").Arguments.Should().Be("--no-shell-escape doc.tex");
    }

    [Fact]
    public void Other_programs_are_left_alone()
    {
        TexProcessRunner.Build("bibtex", "doc", "/tmp/w").Arguments.Should().Be("doc");
        TexProcessRunner.Build("pdftoppm", "-r 150 a.pdf out", "/tmp/w").Arguments.Should().Be("-r 150 a.pdf out");
    }

    [Fact]
    public void The_engine_does_not_inherit_the_API_environment()
    {
        Environment.SetEnvironmentVariable("AI__Anthropic__ApiKey", "sk-secret");
        Environment.SetEnvironmentVariable("ConnectionStrings__LiliaCore", "Host=prod;Password=secret");
        try
        {
            var psi = TexProcessRunner.Build("pdflatex", "doc.tex", "/tmp/w");
            psi.Environment.Keys.Should().NotContain(k => k.StartsWith("AI__") || k.StartsWith("ConnectionStrings") || k.Contains("KEY", StringComparison.OrdinalIgnoreCase) || k.Contains("SECRET", StringComparison.OrdinalIgnoreCase));
            psi.Environment.Values.Should().NotContain(v => v != null && (v.Contains("sk-secret") || v.Contains("Password=secret")));
            psi.Environment["HOME"].Should().Be("/tmp/w");
            psi.Environment.Should().ContainKey("PATH");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI__Anthropic__ApiKey", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__LiliaCore", null);
        }
    }

    [Fact]
    public void Running_as_another_user_wraps_the_command_in_setpriv_only_for_root()
    {
        var withIds = TexProcessRunner.Build("pdflatex", "doc.tex", "/tmp/w", runAs: "65534:65533", isRoot: true);
        withIds.FileName.Should().Be("setpriv");
        withIds.Arguments.Should().Be("--reuid=65534 --regid=65533 --clear-groups --no-new-privs --inh-caps=-all -- pdflatex -no-shell-escape doc.tex");

        var notRoot = TexProcessRunner.Build("pdflatex", "doc.tex", "/tmp/w", runAs: "nobody", isRoot: false);
        notRoot.FileName.Should().Be("pdflatex", "only root can change user; it falls back rather than failing");

        TexProcessRunner.Build("pdflatex", "doc.tex", "/tmp/w", runAs: null, isRoot: true).FileName.Should().Be("pdflatex");
    }

    // ── 16 Oct: the bubblewrap sandbox (LILIA_TEX_SANDBOX=bwrap) ──

    [Fact]
    public void The_sandbox_wraps_the_engine_in_bwrap_with_only_the_work_directory_writable()
    {
        var psi = TexProcessRunner.Build("pdflatex", "-interaction=nonstopmode doc.tex", "/tmp/lilia-latex-1", sandbox: true,
            readOnly: ["/opt/texlive/2026", "/usr/share/texmf", "relative/ignored"]);
        psi.FileName.Should().Be("bwrap");
        psi.Arguments.Should().Contain("--unshare-all").And.Contain("--die-with-parent").And.Contain("--cap-drop ALL")
            .And.Contain("--tmpfs /tmp").And.Contain("--ro-bind /usr /usr")
            .And.Contain("--ro-bind-try \"/opt/texlive/2026\" \"/opt/texlive/2026\"")
            .And.Contain("--bind \"/tmp/lilia-latex-1\" \"/tmp/lilia-latex-1\"")
            .And.EndWith("-- pdflatex -no-shell-escape -interaction=nonstopmode doc.tex");
        psi.Arguments.Should().NotContain("/usr/share/texmf\"", "it is under /usr already").And.NotContain("relative/ignored");
        psi.Arguments.IndexOf("--tmpfs /tmp", StringComparison.Ordinal).Should()
            .BeLessThan(psi.Arguments.IndexOf("--bind \"/tmp/lilia-latex-1\"", StringComparison.Ordinal), "a later /tmp would hide the work directory");
        psi.Arguments.Should().NotContain("/home", "nothing of the API's home is bound unless it is the TeX tree");
    }

    [Fact]
    public void A_precompiled_format_elsewhere_is_bound_read_only_and_an_outside_output_directory_writable()
    {
        var psi = TexProcessRunner.Build("pdflatex", "\"-fmt=/var/cache/lilia/fmt/tikz-abc\" -output-directory /tmp/out doc.tex", "/tmp/w", sandbox: true, readOnly: []);
        psi.Arguments.Should().Contain("--ro-bind-try \"/var/cache/lilia/fmt\" \"/var/cache/lilia/fmt\"")
            .And.Contain("--bind \"/tmp/out\" \"/tmp/out\"");
        TexProcessRunner.Build("pdflatex", "-output-directory /tmp/w doc.tex", "/tmp/w", sandbox: true, readOnly: [])
            .Arguments.Split("--bind \"/tmp/w\"").Length.Should().Be(2, "the work directory is bound once");
    }

    [Fact]
    public void The_trees_lua_font_database_is_shown_read_only_where_LuaLaTeX_looks_for_it()
    {
        var tree = Path.Combine(Path.GetTempPath(), $"lilia-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tree, "luatex-cache", "generic", "names"));
        try
        {
            var args = TexProcessRunner.Build("lualatex", "doc.tex", "/tmp/w", sandbox: true, readOnly: [tree]).Arguments;
            args.Should().Contain($"--ro-bind \"{tree}/luatex-cache/generic/names\" \"/tmp/w/.texmf-var/luatex-cache/generic/names\"");
            args.IndexOf("--bind \"/tmp/w\"", StringComparison.Ordinal).Should()
                .BeLessThan(args.IndexOf("/tmp/w/.texmf-var", StringComparison.Ordinal), "it is mounted inside the work directory, so after it");
        }
        finally { Directory.Delete(tree, true); }
    }

    [Fact]
    public void Without_the_sandbox_nothing_changes()
    {
        TexProcessRunner.Build("pdflatex", "doc.tex", "/tmp/w").FileName.Should().Be("pdflatex");
    }

    [Trait("Category", "RequiresTeX")]
    [Theory]
    [InlineData("pdflatex")]
    [InlineData("xelatex")]
    [InlineData("lualatex")]
    public async Task In_the_sandbox_a_document_compiles_but_cannot_read_the_API_files(string engine)
    {
        if (!File.Exists("/usr/bin/bwrap")) return; // no bubblewrap on this machine: nothing to test
        var canary = Path.Combine(AppContext.BaseDirectory, $"canary-{Guid.NewGuid():N}.txt");
        var work = Path.Combine(Path.GetTempPath(), $"lilia-sandbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            await File.WriteAllTextAsync(canary, "CANARY-SECRET");
            // The verdict goes to the log (a PDF's text is compressed): RESULT:READ:<canary> or RESULT:BLOCKED.
            var read = engine == "lualatex"
                ? $"\\directlua{{local f=io.open(\"{canary}\",\"r\") if f then texio.write_nl(\"RESULT:READ:\"..f:read(\"*l\")) else texio.write_nl(\"RESULT:BLOCKED\") end}}"
                : $"\\newread\\r\\openin\\r={canary} \\ifeof\\r\\typeout{{RESULT:BLOCKED}}\\else\\read\\r to\\x\\typeout{{RESULT:READ:\\x}}\\fi";
            await File.WriteAllTextAsync(Path.Combine(work, "doc.tex"),
                $"\\documentclass{{article}}\\begin{{document}}Compiled. {read}\\end{{document}}\n");

            async Task<(string Log, string Err)> Compile(bool sandbox)
            {
                foreach (var f in Directory.EnumerateFiles(work).Where(f => !f.EndsWith("doc.tex"))) File.Delete(f);
                var psi = TexProcessRunner.Build(engine, "-interaction=nonstopmode doc.tex", work, sandbox: sandbox, readOnly: TexProcessRunner.SandboxReadOnly);
                using var p = System.Diagnostics.Process.Start(psi)!;
                var errTask = p.StandardError.ReadToEndAsync();
                await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                var logPath = Path.Combine(work, "doc.log");
                return (File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "", await errTask);
            }

            // Control: outside the sandbox the same document does read the file, so the test can see a leak.
            (await Compile(sandbox: false)).Log.Should().Contain("RESULT:READ:CANARY-SECRET");

            var (log, err) = await Compile(sandbox: true);
            if (err.Contains("bwrap:") && err.Contains("Permission denied")) return; // user namespaces blocked (no AppArmor profile)
            File.Exists(Path.Combine(work, "doc.pdf")).Should().BeTrue($"{engine} compiles in the sandbox: {err}");
            log.Should().Contain("RESULT:BLOCKED").And.NotContain("CANARY-SECRET");
        }
        finally
        {
            File.Delete(canary);
            Directory.Delete(work, true);
        }
    }

    // ── review of 5 Oct: there is no group called "nobody" on Debian or Ubuntu ──

    [Fact]
    public void A_user_name_resolves_to_its_own_uid_and_primary_group()
    {
        var passwd = Path.GetTempFileName();
        File.WriteAllLines(passwd, ["root:x:0:0:root:/root:/bin/bash", "nobody:x:65534:65534:nobody:/nonexistent:/usr/sbin/nologin", "texrun:x:1500:1501::/home/texrun:/bin/false"]);
        try
        {
            TexProcessRunner.ResolveRunAs("nobody", passwd).Should().Be(("65534", "65534"));
            TexProcessRunner.ResolveRunAs("texrun", passwd).Should().Be(("1500", "1501"));
            TexProcessRunner.ResolveRunAs("1500", passwd).Should().Be(("1500", "1501"), "a numeric uid finds its group too");
            TexProcessRunner.ResolveRunAs("ghost", passwd).Should().Be(("ghost", "65534"));
            TexProcessRunner.ResolveRunAs("1:2", passwd).Should().Be(("1", "2"));
        }
        finally { File.Delete(passwd); }
    }

    [Fact]
    public void On_this_machine_nobody_resolves_to_a_group_that_exists()
    {
        var (_, gid) = TexProcessRunner.ResolveRunAs("nobody");
        gid.Should().NotBe("nobody");
        int.TryParse(gid, out _).Should().BeTrue();
    }
}
