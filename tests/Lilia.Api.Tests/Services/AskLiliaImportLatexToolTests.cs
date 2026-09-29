using FluentAssertions;
using Lilia.Core.DTOs;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Services.AskLiliaHarness;

namespace Lilia.Api.Tests.Services;

public class AskLiliaImportLatexToolTests
{
    private static (AskLiliaHarness H, DocumentDto Doc, List<BatchUpdateBlockDto> Batch, List<UpdateDocumentDto> Updates) Setup(params BlockDto[] existing)
    {
        var h = new AskLiliaHarness();
        var doc = Doc(existing);
        var batch = new List<BatchUpdateBlockDto>();
        var updates = new List<UpdateDocumentDto>();
        h.Documents.Setup(d => d.UpdateDocumentAsync(doc.Id, "u1", It.IsAny<UpdateDocumentDto>()))
            .Callback<Guid, string, UpdateDocumentDto>((_, _, dto) => updates.Add(dto))
            .ReturnsAsync(doc);
        h.Blocks.Setup(b => b.BatchUpdateBlocksAsync(doc.Id, It.IsAny<List<BatchUpdateBlockDto>>(), It.IsAny<int?>()))
            .Callback<Guid, List<BatchUpdateBlockDto>, int?>((_, l, _) => { batch.Clear(); batch.AddRange(l); })
            .ReturnsAsync(() => new BatchUpdateResultDto(
                batch.Select((b, i) => new BlockDto(b.Id, doc.Id, b.Type ?? existing.FirstOrDefault(e => e.Id == b.Id)?.Type ?? "paragraph",
                    b.Content ?? System.Text.Json.JsonDocument.Parse("{}").RootElement, i, null, 0, DateTime.UtcNow, DateTime.UtcNow)).ToList(), 1));
        return (h, doc, batch, updates);
    }

    [Fact]
    public void Import_latex_is_a_write_tool_with_replace_optional()
    {
        var (h, doc, _, _) = Setup();
        h.Tools(doc, false).OfType<AIFunction>().Select(f => f.Name).Should().NotContain("import_latex");
        var f = Fn(h.Tools(doc, true), "import_latex");
        var req = f.JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        req.Should().Contain("text").And.NotContain("replace");
    }

    [Fact]
    public async Task A_whole_tex_gives_blocks_and_settings_and_is_reported_for_undo()
    {
        var (h, doc, batch, updates) = Setup();
        var changed = new List<string>();
        var meta = 0;
        var tools = h.Tools(doc, true, changed, () => meta++);

        var r = await Call(Fn(tools, "import_latex"), ("text", "```latex\n" + LatexPageSetupExtractorTests.Lecture + "\n```"));

        r.GetProperty("ok").GetBoolean().Should().BeTrue(r.ToString());
        r.GetProperty("mode").GetString().Should().Be("replaced");
        r.GetProperty("importedBlocks").GetInt32().Should().BeGreaterThan(0);

        // settings, through the update the dialog uses
        updates.Should().ContainSingle();
        var u = updates[0];
        u.FontSize.Should().Be(12);
        u.MarginLeft.Should().Be("3cm");
        u.PageNumbering.Should().Be("roman");
        u.HeaderLeft.Should().Be("Lecture 3");
        u.LineSpacing.Should().Be(1.5);
        u.FontFamily.Should().Be("palatino");
        u.Sides.Should().Be("two");
        u.LatexDocumentClass.Should().Be("article");
        u.LatexPackages.Should().Contain("amsmath");
        u.CustomPreamble.Should().Contain(@"\newcommand{\R}");

        // blocks: title block first (from \title/\author), then the body
        batch[0].Type.Should().Be("title");
        batch.Select(b => b.Type).Should().Contain("heading");
        changed.Should().NotBeEmpty();
        meta.Should().Be(1, "settings changed, so the client refreshes and offers Undo");

        r.GetProperty("settingsApplied").EnumerateArray().Select(e => e.GetString()).Should().Contain(a => a!.StartsWith("headerLeft"));
        r.GetProperty("notApplied").EnumerateArray().Select(e => e.GetString()).Should().Contain(n => n!.Contains(@"\thepage"));
    }

    [Fact]
    public async Task Replace_keeps_the_existing_title_block_when_the_file_has_no_title()
    {
        var titleId = Guid.NewGuid();
        var oldBody = Guid.NewGuid();
        var (h, doc, batch, _) = Setup(Block(titleId, "title"), Block(oldBody));
        var tools = h.Tools(doc, true);
        var r = await Call(Fn(tools, "import_latex"),
            ("text", "\\documentclass{article}\n\\begin{document}\n\\section{One}\nText.\n\\end{document}"));
        r.GetProperty("ok").GetBoolean().Should().BeTrue(r.ToString());
        batch[0].Id.Should().Be(titleId);
        batch.Select(b => b.Id).Should().NotContain(oldBody, "the old body is replaced");
    }

    [Fact]
    public async Task Append_adds_blocks_and_leaves_settings_alone()
    {
        var keep = Guid.NewGuid();
        var (h, doc, batch, updates) = Setup(Block(keep));
        var tools = h.Tools(doc, true);
        var r = await Call(Fn(tools, "import_latex"),
            ("text", "\\documentclass[12pt]{article}\n\\begin{document}\n\\section{More}\nText.\n\\end{document}"), ("replace", false));
        r.GetProperty("mode").GetString().Should().Be("appended");
        updates.Should().BeEmpty();
        batch[0].Id.Should().Be(keep);
        batch.Count.Should().BeGreaterThan(1);
        r.GetProperty("notApplied").EnumerateArray().Select(e => e.GetString()).Should().Contain(n => n!.Contains("replace=false"));
    }

    [Fact]
    public async Task Nothing_is_written_for_empty_or_contentless_input()
    {
        var (h, doc, batch, updates) = Setup();
        var tools = h.Tools(doc, true);
        (await Call(Fn(tools, "import_latex"), ("text", "  "))).GetProperty("error").GetString().Should().Contain("empty");
        (await Call(Fn(tools, "import_latex"), ("text", new string('x', 100_001)))).GetProperty("error").GetString().Should().Contain("limit");
        var none = await Call(Fn(tools, "import_latex"), ("text", "\\documentclass{article}\n\\begin{document}\n\\end{document}"));
        none.GetProperty("error").GetString().Should().Contain("Nothing was changed");
        updates.Should().BeEmpty();
        batch.Should().BeEmpty();
    }

    [Fact]
    public void Fences_are_stripped()
    {
        Lilia.Api.Services.AskLiliaService.StripLatexFences("```latex\n\\section{a}\n```").Should().Be("\\section{a}");
        Lilia.Api.Services.AskLiliaService.StripLatexFences("\\section{a}").Should().Be("\\section{a}");
    }
}
