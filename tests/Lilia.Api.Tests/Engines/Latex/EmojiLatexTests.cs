using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The document sweep (E-015, 16 Nov 2026): an emoji the shared list doesn't map (🚀) stopped pdflatex. Emoji are now
/// drawn by twemojis, matched to the longest sequence the package knows.
/// </summary>
public class EmojiLatexTests
{
    private static readonly IReadOnlySet<string> Known = EmojiLatex.Parse("""
        \defineTwemoji{1}{1f680}%
        \defineTwemoji{1}{rocket}%
        \defineTwemoji{2}{1f44d}%
        \defineTwemoji{3}{1f44d-1f3fd}%
        \defineTwemoji{4}{1f468}%
        \defineTwemoji{5}{1f4bb}%
        \defineTwemoji{6}{1f468-200d-1f4bb}%
        \defineTwemoji{7}{1f1eb-1f1f7}%
        \defineTwemoji{8}{2764}%
        \defineTwemoji{9}{1f3c3-200d-2640-fe0f}%
        \defineTwemoji{10}{2705}%
        """);

    private static readonly IReadOnlySet<int> Mapped = new HashSet<int> { 0x2705 }; // ✅, mapped by the shared list

    private static string Doc(string body) => "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n";

    private static string Body(string latex) => latex[(latex.IndexOf(@"\begin{document}", StringComparison.Ordinal) + 16)..];

    [Fact]
    public void Reads_the_sequences_and_skips_the_names() =>
        Known.Should().Contain("1f680").And.Contain("1f468-200d-1f4bb").And.NotContain("rocket");

    [Fact]
    public void An_emoji_is_drawn_and_the_package_loaded_once()
    {
        var latex = EmojiLatex.Apply(Doc("Launch 🚀 and 🚀."), Known, Mapped);
        Body(latex).Should().Contain(@"Launch \texttwemoji{1f680} and \texttwemoji{1f680}.");
        latex.Split(@"\usepackage{twemojis}").Length.Should().Be(2);
        latex.IndexOf(@"\usepackage{twemojis}", StringComparison.Ordinal).Should().BeLessThan(latex.IndexOf(@"\begin{document}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("👍🏽", "1f44d-1f3fd")]          // a skin tone stays with its hand
    [InlineData("👨‍💻", "1f468-200d-1f4bb")]     // a joined emoji is one picture
    [InlineData("🇫🇷", "1f1eb-1f1f7")]          // a flag is its two letters
    [InlineData("❤️", "2764")]                 // an older symbol asking for its emoji form; the name drops U+FE0F
    [InlineData("🏃‍♀️", "1f3c3-200d-2640-fe0f")] // a name that keeps U+FE0F
    public void Sequences_stay_whole(string emoji, string key) =>
        Body(EmojiLatex.Apply(Doc($"a {emoji} b"), Known, Mapped)).Should().Contain($@"a \texttwemoji{{{key}}} b");

    [Fact]
    public void A_skin_tone_the_package_lacks_falls_back_to_the_plain_emoji()
    {
        // 👍🏿 isn't in this list: the hand is drawn, and the lone tone left for the shared list's handling.
        Body(EmojiLatex.Apply(Doc("👍🏿"), Known, Mapped)).Should().StartWith("\n\\texttwemoji{1f44d}");
    }

    [Fact]
    public void Left_alone_mapped_characters_unknown_emoji_older_symbols_without_FE0F_and_code()
    {
        var src = Doc("✅ done, 🦖 unknown, ❤ plain heart\n\\begin{lstlisting}\nprint('🚀')\n\\end{lstlisting}\n\\begin{verbatim}🚀\\end{verbatim}");
        EmojiLatex.Apply(src, Known, Mapped).Should().Be(src);
    }

    [Fact]
    public void Code_is_left_alone_while_the_text_around_it_is_drawn()
    {
        var latex = EmojiLatex.Apply(Doc("🚀\n\\begin{lstlisting}\nx = '🚀'\n\\end{lstlisting}"), Known, Mapped);
        Body(latex).Should().Contain(@"\texttwemoji{1f680}").And.Contain("x = '🚀'");
    }

    [Fact]
    public void Without_the_package_nothing_changes()
    {
        var src = Doc("🚀");
        EmojiLatex.Apply(src, new HashSet<string>(), Mapped).Should().Be(src);
    }

    [Fact]
    public void A_chapter_file_is_drawn_and_says_the_package_is_needed()
    {
        EmojiLatex.DrawText("Launch 🚀.\n\\begin{lstlisting}\n'🚀'\n\\end{lstlisting}", Known, Mapped, out var replaced)
            .Should().Be("Launch \\texttwemoji{1f680}.\n\\begin{lstlisting}\n'🚀'\n\\end{lstlisting}");
        replaced.Should().BeTrue();
        EmojiLatex.DrawText("No emoji here, ✅ only.", Known, Mapped, out var none).Should().Be("No emoji here, ✅ only.");
        none.Should().BeFalse();
    }

    [Fact]
    public void The_shared_list_mapped_set_holds_its_ticks_and_stars() =>
        EmojiLatex.SharedListMapped.Should().Contain(0x2705).And.Contain(0x2605);

    // 16 Nov 2026: the downloaded .tex carried a raw 🚀, which stops pdflatex on Overleaf. It now carries the drawing.
    [Fact]
    public void The_downloaded_tex_carries_the_drawn_emoji()
    {
        if (EmojiLatex.Installed.Count == 0) return; // no TeX Live here: nothing to draw with
        var doc = new Document { Id = Guid.NewGuid(), Title = "Emoji", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article" };
        var para = new Block { Id = Guid.NewGuid(), Type = "paragraph", SortOrder = 0, Content = JsonDocument.Parse("""{"text":"Launch 🚀 now."}""") };
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance).BuildSingleFileLatex(doc, [para], [], new LaTeXExportOptions());
        tex.Should().Contain(@"\texttwemoji{1f680}").And.NotContain("🚀");
        tex.IndexOf(@"\usepackage{twemojis}", StringComparison.Ordinal).Should().BeLessThan(tex.IndexOf(@"\begin{document}", StringComparison.Ordinal));
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_paper_with_emoji_compiles_with_pdflatex()
    {
        EmojiLatex.Installed.Should().Contain("1f680", "TeX Live's twemojis package is installed on the server");
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Emoji", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
        };
        var para = new Block { Id = Guid.NewGuid(), Type = "paragraph", SortOrder = 0, Content = JsonDocument.Parse("""{"text":"Launch 🚀, 👍🏽, 👨‍💻, 🇫🇷 and ❤️ done."}""") };
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance).BuildSingleFileLatex(doc, [para], [], new LaTeXExportOptions());
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);
        pdf.Length.Should().BeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
