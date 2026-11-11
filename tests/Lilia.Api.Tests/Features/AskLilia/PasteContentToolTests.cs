using FluentAssertions;
using Lilia.Core.DTOs;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Features.AskLilia.AskLiliaHarness;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// paste_content (paste → LaTeX, phase 3, 11 Nov 2026): text read as Import → Paste reads it, its blocks put where
/// asked, in one versioned write that keeps every other block, and the reply naming the new blocks. The connector's
/// paste_content runs this tool.
/// </summary>
public class PasteContentToolTests
{
    private static (AskLiliaHarness H, DocumentDto Doc, List<BatchUpdateBlockDto> Batch) Setup(params BlockDto[] existing)
    {
        var h = new AskLiliaHarness();
        var doc = Doc(existing);
        var batch = new List<BatchUpdateBlockDto>();
        h.Documents.Setup(d => d.GetDocumentAsync(doc.Id, "u1")).ReturnsAsync(() => doc);
        h.Blocks.Setup(b => b.BatchUpdateBlocksAsync(doc.Id, It.IsAny<List<BatchUpdateBlockDto>>(), It.IsAny<int?>()))
            .Callback<Guid, List<BatchUpdateBlockDto>, int?>((_, l, _) => { batch.Clear(); batch.AddRange(l); })
            .ReturnsAsync(() => new BatchUpdateResultDto(
                batch.Select((b, i) => new BlockDto(b.Id, doc.Id, b.Type ?? existing.FirstOrDefault(e => e.Id == b.Id)?.Type ?? "paragraph",
                    b.Content ?? System.Text.Json.JsonDocument.Parse("{}").RootElement, i, null, 0, DateTime.UtcNow, DateTime.UtcNow)).ToList(), 1));
        return (h, doc, batch);
    }

    [Fact]
    public void It_is_a_write_tool_with_only_the_text_required()
    {
        var (h, doc, _) = Setup();
        h.Tools(doc, false).OfType<AIFunction>().Select(f => f.Name).Should().NotContain("paste_content");
        var req = Fn(h.Tools(doc, true), "paste_content").JsonSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        req.Should().Equal("content");
    }

    [Fact]
    public async Task Markdown_pasted_after_a_block_goes_between_and_keeps_the_rest()
    {
        var first = Guid.NewGuid();
        var last = Guid.NewGuid();
        var (h, doc, batch) = Setup(Block(first, "heading"), Block(last));
        var changed = new List<string>();

        var r = await Call(Fn(h.Tools(doc, true, changed), "paste_content"),
            ("content", "## Method\n\nWe measure $x$.\n\n- one\n- two\n"), ("afterId", first.ToString()));

        r.GetProperty("format").GetString().Should().Be("MARKDOWN");
        r.GetProperty("added").EnumerateArray().Select(a => a.GetProperty("type").GetString()).Should().Equal("heading", "paragraph", "list");
        batch.Select(b => b.Id).First().Should().Be(first);
        batch.Select(b => b.Id).Last().Should().Be(last, "the block after the paste stays after it");
        batch.Should().HaveCount(5);
        batch.Skip(1).Take(3).Should().OnlyContain(b => b.Type != null, "the pasted blocks are new");
        changed.Should().HaveCount(3).And.NotContain(first.ToString()).And.NotContain(last.ToString());
    }

    [Fact]
    public async Task Without_afterId_it_adds_at_the_end()
    {
        var only = Guid.NewGuid();
        var (h, doc, batch) = Setup(Block(only));

        await Call(Fn(h.Tools(doc, true), "paste_content"), ("content", "\\section{Results}\nIt works."), ("format", "LATEX"));

        batch.First().Id.Should().Be(only);
        batch.Skip(1).Select(b => b.Type).Should().Contain("heading");
    }

    [Fact]
    public async Task An_unknown_block_changes_nothing_and_says_where_to_find_ids()
    {
        var (h, doc, _) = Setup(Block(Guid.NewGuid()));

        var r = await Call(Fn(h.Tools(doc, true), "paste_content"), ("content", "Hello."), ("afterId", Guid.NewGuid().ToString()));

        r.GetProperty("error").GetString().Should().Contain("get_outline");
        h.Blocks.Verify(b => b.BatchUpdateBlocksAsync(It.IsAny<Guid>(), It.IsAny<List<BatchUpdateBlockDto>>(), It.IsAny<int?>()), Times.Never);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("x", "Unknown format")]
    public async Task A_bad_request_changes_nothing(string content, string said)
    {
        var (h, doc, _) = Setup();
        var r = await Call(Fn(h.Tools(doc, true), "paste_content"), ("content", content), ("format", content == "x" ? "DOCX" : null));
        r.GetProperty("error").GetString().Should().Contain(said);
        h.Blocks.Verify(b => b.BatchUpdateBlocksAsync(It.IsAny<Guid>(), It.IsAny<List<BatchUpdateBlockDto>>(), It.IsAny<int?>()), Times.Never);
    }
}
