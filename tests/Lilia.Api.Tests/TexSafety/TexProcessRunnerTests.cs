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
