using FluentAssertions;
using Lilia.Engines.TexSafety;
using Xunit;

namespace Lilia.Api.Tests.TexSafety;

/// <summary>
/// What a document may ask the compiler to do (A6 of the authz audit, 5 Oct 2026). On this machine
/// <c>\input{/etc/hostname}</c> printed the host name into the PDF, and the engine's own path policy
/// (<c>openin_any=p</c>) did not stop it. The guard is not a sandbox (see <see cref="TexSourceGuard"/>),
/// so these tests pin both halves: what it refuses, and the ordinary documents it must let through.
/// </summary>
public class TexSourceGuardTests
{
    private static string Doc(string body) => "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n";

    // ── refused ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\input{/etc/hostname}")]
    [InlineData(@"\input{../../etc/passwd}")]
    [InlineData(@"\input /etc/hostname ")]
    [InlineData(@"\input{|cat /etc/passwd}")]
    [InlineData(@"\include{/etc/passwd}")]
    [InlineData(@"\InputIfFileExists{/etc/passwd}{}{}")]
    [InlineData(@"\openin5=/etc/hostname")]
    [InlineData(@"\openin5=../secret")]
    [InlineData(@"\verbatiminput{/etc/passwd}")]
    [InlineData(@"\lstinputlisting{/proc/self/environ}")]
    [InlineData(@"\input{\string/etc/hostname}")]
    [InlineData(@"\input\x")]
    [InlineData(@"\input{/proc/self/environ}")]
    [InlineData(@"\input{~/.ssh/id_rsa}")]
    public void File_reads_of_server_paths_are_refused(string body) =>
        TexSourceGuard.Violation(Doc(body)).Should().NotBeNull();

    [Theory]
    [InlineData(@"\immediate\write18{id}")]
    [InlineData(@"\write18{id}")]
    [InlineData(@"\ShellEscape{id}")]
    [InlineData(@"\directlua{os.execute('id')}")]
    [InlineData(@"\scantokens{\input{x}}")]
    [InlineData(@"\readline5 to \x")]
    public void Shell_lua_and_text_to_code_are_refused(string body) =>
        TexSourceGuard.Violation(Doc(body)).Should().NotBeNull();

    [Fact]
    public void A_server_path_is_refused_even_when_the_command_name_is_hidden()
    {
        // \csname builds the command; the path is still there to see.
        TexSourceGuard.Violation(Doc(@"\csname input\endcsname{/etc/hostname}")).Should().NotBeNull();
    }

    [Fact]
    public void A_path_in_a_package_or_class_is_refused()
    {
        TexSourceGuard.Violation("\\documentclass{/tmp/evil}\\begin{document}\\end{document}").Should().NotBeNull();
        TexSourceGuard.Violation("\\documentclass{article}\\usepackage{../../evil}\\begin{document}\\end{document}").Should().NotBeNull();
    }

    [Fact]
    public void The_message_names_the_problem_for_the_author()
    {
        TexSourceGuard.Violation(Doc(@"\input{/etc/hostname}")).Should().Contain("server");
        TexSourceGuard.Violation(Doc(@"\write18{id}")).Should().Contain("write18");
    }

    [Fact]
    public void ThrowIfUnsafe_throws_the_exception_the_API_shows_to_the_author()
    {
        var act = () => TexSourceGuard.ThrowIfUnsafe(Doc(@"\input{/etc/hostname}"));
        act.Should().Throw<UnsafeLatexException>();
    }

    // ── allowed: ordinary documents ──────────────────────────────────────

    [Theory]
    [InlineData(@"\input{chapter1}")]
    [InlineData(@"\input{sections/intro.tex}")]
    [InlineData(@"\include{appendix}")]
    [InlineData(@"\includegraphics[width=3cm]{figure1.png}")]
    [InlineData(@"\includegraphics{example-image-a}")]
    [InlineData(@"\bibliography{sources}")]
    [InlineData(@"\usepackage{amsmath, graphicx}")]
    [InlineData(@"\verbatiminput{listing.txt}")]
    public void Plain_relative_names_are_fine(string body) =>
        TexSourceGuard.Violation(Doc(body)).Should().BeNull();

    [Theory]
    [InlineData("\\documentclass[12pt]{article}\\usepackage[utf8]{inputenc}\\usepackage{lipsum}\\begin{document}\\lipsum[1]\\end{document}")]
    [InlineData("\\documentclass{beamer}\\begin{document}\\begin{frame}A\\end{frame}\\end{document}")]
    [InlineData("\\documentclass{standalone}\\usepackage{tikz}\\begin{document}\\begin{tikzpicture}\\draw (0,0)--(1,1);\\end{tikzpicture}\\end{document}")]
    public void Ordinary_documents_pass(string source) =>
        TexSourceGuard.Violation(source).Should().BeNull();

    [Fact]
    public void Macros_that_use_csname_and_input_in_the_ordinary_way_pass()
    {
        TexSourceGuard.Violation(Doc(@"\newcommand{\sec}[1]{\csname section\endcsname{#1}}\sec{A}")).Should().BeNull();
    }

    [Fact]
    public void Documentation_about_a_unix_system_may_name_paths_in_code_and_urls()
    {
        var body = "\\begin{verbatim}\n/etc/hosts and /usr/bin/python\n\\end{verbatim}\n" +
                   "\\begin{lstlisting}\ncat /var/log/syslog\n\\end{lstlisting}\n" +
                   "Run \\verb|/usr/bin/env| first, see \\url{https://example.org/home/page}.";
        TexSourceGuard.Violation(Doc(body)).Should().BeNull();
    }

    [Fact]
    public void A_path_after_a_verbatim_block_is_still_scanned()
    {
        TexSourceGuard.Violation(Doc("\\begin{verbatim}x\\end{verbatim}\\input{/etc/passwd}")).Should().NotBeNull();
    }

    [Fact]
    public void A_commented_out_command_is_not_a_command()
    {
        TexSourceGuard.Violation(Doc("% \\input{/etc/passwd}\nHello")).Should().BeNull();
        TexSourceGuard.Violation(Doc("100\\% \\input{/etc/passwd}")).Should().NotBeNull("\\% is a percent sign, not a comment");
    }

    [Fact]
    public void Empty_source_is_not_a_violation()
    {
        TexSourceGuard.Violation(null).Should().BeNull();
        TexSourceGuard.Violation("").Should().BeNull();
    }
}
