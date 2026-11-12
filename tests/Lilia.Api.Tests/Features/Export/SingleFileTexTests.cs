using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Lilia.Api.Controllers;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// The bot's Level 3 notes (12 Nov 2026): "Single file" LaTeX downloaded a .zip. When the project is
/// main.tex alone (a README aside), the export sends the .tex itself.
/// </summary>
public class SingleFileTexTests
{
    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Main_tex_alone_comes_back_as_the_tex()
    {
        using var zip = Zip(("main.tex", "\\documentclass{article}"), ("README.txt", "read me"));
        Encoding.UTF8.GetString(ExportController.SoleMainTex(zip)!).Should().Be("\\documentclass{article}");
        zip.Position.Should().Be(0); // the zip can still be sent
    }

    [Theory]
    [InlineData("figures/plot.png")]
    [InlineData("references.bib")]
    public void A_project_with_more_than_main_tex_stays_a_zip(string other)
    {
        using var zip = Zip(("main.tex", "x"), (other, "y"));
        ExportController.SoleMainTex(zip).Should().BeNull();
    }
}
