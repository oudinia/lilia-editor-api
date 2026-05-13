using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// LaTeX engine auto-detection — pdflatex is the floor, lualatex only
/// when the source actually needs it (fontspec / system fonts /
/// unicode-math / lua callouts). Per-block detection drives validation
/// (each block validates with the engine it needs); doc-level detection
/// drives export (max engine across blocks + preamble).
/// </summary>
public class EngineDetectorTests
{
    [Fact]
    public void Empty_or_null_falls_back_to_pdflatex()
    {
        EngineDetector.Detect(null).Should().Be(LatexEngine.Pdflatex);
        EngineDetector.Detect("").Should().Be(LatexEngine.Pdflatex);
    }

    [Fact]
    public void Plain_paragraph_text_is_pdflatex_safe()
    {
        EngineDetector.Detect("Hello \\textbf{world}, math $x^2$ here.")
            .Should().Be(LatexEngine.Pdflatex);
    }

    [Theory]
    [InlineData(@"\setmainfont{Charter}")]
    [InlineData(@"\setsansfont{Inter}")]
    [InlineData(@"\setmonofont{JetBrains Mono}")]
    [InlineData(@"\setmathfont{XITS Math}")]
    [InlineData(@"\newfontfamily\heading{Inter}")]
    [InlineData(@"\fontspec{Times New Roman}")]
    public void Fontspec_family_commands_require_lualatex(string snippet)
    {
        EngineDetector.Detect(snippet).Should().Be(LatexEngine.Lualatex);
    }

    [Theory]
    [InlineData(@"\usepackage{fontspec}")]
    [InlineData(@"\usepackage{unicode-math}")]
    [InlineData(@"\usepackage{polyglossia}")]
    public void Engine_specific_packages_require_lualatex(string snippet)
    {
        EngineDetector.Detect(snippet).Should().Be(LatexEngine.Lualatex);
    }

    [Theory]
    [InlineData(@"\directlua{tex.print('hi')}")]
    [InlineData(@"\luaexec{print(1+1)}")]
    public void Lua_callouts_require_lualatex(string snippet)
    {
        EngineDetector.Detect(snippet).Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void DetectDocument_returns_max_across_blocks()
    {
        var blocks = new[]
        {
            "Plain paragraph one.",
            @"Block with \setmainfont{Charter} mid-text.",
            "Plain paragraph three.",
        };
        EngineDetector.DetectDocument(blocks).Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void DetectDocument_all_pdflatex_safe_stays_pdflatex()
    {
        var blocks = new[]
        {
            "Section one prose.",
            @"Bold \textbf{title} and italic \textit{phrase}.",
            "Math $E = mc^2$.",
        };
        EngineDetector.DetectDocument(blocks).Should().Be(LatexEngine.Pdflatex);
    }

    [Fact]
    public void DetectDocument_preamble_signal_alone_bumps_engine()
    {
        // No per-block fontspec, but the doc-level preamble adds
        // \setmainfont{...} from the font-picker — engine still needs lua.
        var blocks = new[] { "Plain text only.", "Another plain block." };
        EngineDetector.DetectDocument(blocks, preambleExtras: @"\setmainfont{ForestOTF}")
            .Should().Be(LatexEngine.Lualatex);
    }

    [Fact]
    public void Engine_string_round_trip_via_extensions()
    {
        LatexEngine.Pdflatex.ToCli().Should().Be("pdflatex");
        LatexEngine.Lualatex.ToCli().Should().Be("lualatex");
        LatexEngine.Xelatex.ToCli().Should().Be("xelatex");

        "pdflatex".ParseEngine().Should().Be(LatexEngine.Pdflatex);
        "lualatex".ParseEngine().Should().Be(LatexEngine.Lualatex);
        "xelatex".ParseEngine().Should().Be(LatexEngine.Xelatex);
        "PDFLaTeX".ParseEngine().Should().Be(LatexEngine.Pdflatex); // case-insensitive
        "unknown".ParseEngine().Should().Be(LatexEngine.Pdflatex); // graceful fallback
        ((string?)null).ParseEngine().Should().Be(LatexEngine.Pdflatex);
    }

    [Fact]
    public void Lualatex_ordering_is_max_capability()
    {
        // Pdflatex < Xelatex < Lualatex so DetectDocument's > comparison
        // promotes correctly when a mixed-engine doc surfaces in future.
        ((int)LatexEngine.Lualatex).Should().BeGreaterThan((int)LatexEngine.Xelatex);
        ((int)LatexEngine.Xelatex).Should().BeGreaterThan((int)LatexEngine.Pdflatex);
    }
}
