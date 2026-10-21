using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data.Seeds;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Templates;

/// <summary>Every launch template (first run 5b) prints: a new person's first PDF must not fail.</summary>
public class LaunchTemplateCompileTests
{
    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Every_launch_template_compiles()
    {
        foreach (var doc in LaunchTemplateSeeder.Build())
        {
            var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
            var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
                .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions());
            var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);
            pdf.Should().NotBeEmpty(doc.TemplateName);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", doc.TemplateName);
        }
    }
}
