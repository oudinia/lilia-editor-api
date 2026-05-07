using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// LILIA-136 slice 2 — TypstExportService groups contiguous block runs
/// by their effective column count and emits one `#columns(N)[...]`
/// wrapper per run. Document-level <c>doc.Columns</c> is the default;
/// layout-dimension groups override per-block.
/// </summary>
public class TypstExportColumnGroupingTests
{
    private static readonly TypstExportService Sut = new();

    private static Block ParaBlock(Guid id, int order, string text)
        => new()
        {
            Id = id,
            Type = "paragraph",
            Content = JsonDocument.Parse($$"""{"text":{{JsonSerializer.Serialize(text)}}}"""),
            SortOrder = order,
        };

    private static BlockGroup LayoutGroup(Guid docId, int columns, params Guid[] blockIds)
    {
        var groupId = Guid.NewGuid();
        return new BlockGroup
        {
            Id = groupId,
            DocumentId = docId,
            Dimension = BlockGroupDimensions.Layout,
            Attributes = JsonDocument.Parse($$"""{"columns":{{columns}}}"""),
            Memberships = blockIds.Select(b => new BlockGroupMembership
            {
                BlockId = b,
                GroupId = groupId,
            }).ToList(),
        };
    }

    [Fact]
    public void NoGroups_DocColumns1_EmitsInline_NoColumnsWrapper()
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "T", Columns = 1, PaperSize = "a4" };
        var blocks = new List<Block>
        {
            ParaBlock(Guid.NewGuid(), 0, "First."),
            ParaBlock(Guid.NewGuid(), 1, "Second."),
        };

        var output = Sut.BuildTypstDocument(doc, blocks);

        output.Should().Contain("First.").And.Contain("Second.");
        output.Should().NotContain("#columns(");
    }

    [Fact]
    public void NoGroups_DocColumns2_WrapsWholeBodyInColumns2()
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "T", Columns = 2, PaperSize = "a4" };
        var blocks = new List<Block>
        {
            ParaBlock(Guid.NewGuid(), 0, "Body 1."),
            ParaBlock(Guid.NewGuid(), 1, "Body 2."),
        };

        var output = Sut.BuildTypstDocument(doc, blocks);

        output.Should().Contain("#columns(2)[");
        // Single wrapper, single closing bracket on its own line.
        var wrapperOpenCount = output.Split("#columns(2)[").Length - 1;
        wrapperOpenCount.Should().Be(1);
    }

    [Fact]
    public void LayoutGroup_OverridesDocDefault_OnlyForGroupMembers()
    {
        var docId = Guid.NewGuid();
        var b0 = ParaBlock(Guid.NewGuid(), 0, "Abstract."); // outside group, doc default
        var b1 = ParaBlock(Guid.NewGuid(), 1, "Body para 1."); // in 2-col group
        var b2 = ParaBlock(Guid.NewGuid(), 2, "Body para 2."); // in 2-col group
        var b3 = ParaBlock(Guid.NewGuid(), 3, "Closing.");    // outside group, doc default

        var doc = new Document { Id = docId, Title = "T", Columns = 1, PaperSize = "a4" };
        var blocks = new List<Block> { b0, b1, b2, b3 };
        var groups = new List<BlockGroup> { LayoutGroup(docId, 2, b1.Id, b2.Id) };

        var output = Sut.BuildTypstDocument(doc, blocks, groups);

        // The 2-col run wraps b1 and b2 only.
        output.Should().Contain("#columns(2)[");
        output.IndexOf("Abstract.").Should().BeLessThan(output.IndexOf("#columns(2)["));
        output.IndexOf("Closing.").Should().BeGreaterThan(output.LastIndexOf("]"));
    }

    [Fact]
    public void LayoutGroup_FlipsBackToOneColumnForUngroupedFollower()
    {
        // Standard academic-paper shape: 1-col abstract, 2-col body.
        // doc.Columns = 1 (default), group makes the body 2-col. Output
        // should NOT wrap the abstract in any #columns block.
        var docId = Guid.NewGuid();
        var abs = ParaBlock(Guid.NewGuid(), 0, "Abstract text.");
        var body1 = ParaBlock(Guid.NewGuid(), 1, "Body 1.");
        var body2 = ParaBlock(Guid.NewGuid(), 2, "Body 2.");

        var doc = new Document { Id = docId, Title = "T", Columns = 1, PaperSize = "a4" };
        var blocks = new List<Block> { abs, body1, body2 };
        var groups = new List<BlockGroup> { LayoutGroup(docId, 2, body1.Id, body2.Id) };

        var output = Sut.BuildTypstDocument(doc, blocks, groups);

        var col2OpenIdx = output.IndexOf("#columns(2)[");
        col2OpenIdx.Should().BeGreaterThan(0);
        // Abstract appears before any column wrapper.
        output.IndexOf("Abstract text.").Should().BeLessThan(col2OpenIdx);
        // No #columns(1) wrapper (single-column runs are emitted inline).
        output.Should().NotContain("#columns(1)[");
    }

    [Fact]
    public void TwoAdjacentLayoutGroups_SameColumnCount_AreMergedIntoOneRun()
    {
        // Two distinct layout groups, both 2-col, on adjacent blocks.
        // For v1 the exporter merges them into a single #columns(2)
        // wrapper (visually identical, source noise reduced).
        var docId = Guid.NewGuid();
        var b1 = ParaBlock(Guid.NewGuid(), 0, "G1 block.");
        var b2 = ParaBlock(Guid.NewGuid(), 1, "G2 block.");

        var doc = new Document { Id = docId, Title = "T", Columns = 1, PaperSize = "a4" };
        var blocks = new List<Block> { b1, b2 };
        var groups = new List<BlockGroup>
        {
            LayoutGroup(docId, 2, b1.Id),
            LayoutGroup(docId, 2, b2.Id),
        };

        var output = Sut.BuildTypstDocument(doc, blocks, groups);

        var openCount = output.Split("#columns(2)[").Length - 1;
        openCount.Should().Be(1);
    }

    [Fact]
    public void GroupAttribute_MissingOrInvalid_FallsBackToDocDefault()
    {
        var docId = Guid.NewGuid();
        var b = ParaBlock(Guid.NewGuid(), 0, "Text.");
        var doc = new Document { Id = docId, Title = "T", Columns = 2, PaperSize = "a4" };
        // Group with no `columns` attribute — should fall back to doc default of 2.
        var group = new BlockGroup
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Dimension = BlockGroupDimensions.Layout,
            Attributes = JsonDocument.Parse("""{}"""),
            Memberships = new List<BlockGroupMembership>
            {
                new() { BlockId = b.Id }
            },
        };

        var output = Sut.BuildTypstDocument(doc, new List<Block> { b }, new[] { group });

        // Doc default is 2 → still emits #columns(2).
        output.Should().Contain("#columns(2)[");
    }

    [Fact]
    public void NonLayoutDimensionGroups_AreIgnored()
    {
        var docId = Guid.NewGuid();
        var b = ParaBlock(Guid.NewGuid(), 0, "Text.");
        var doc = new Document { Id = docId, Title = "T", Columns = 1, PaperSize = "a4" };
        var reviewGroup = new BlockGroup
        {
            Id = Guid.NewGuid(),
            DocumentId = docId,
            Dimension = "review",
            Attributes = JsonDocument.Parse("""{"flag":"todo"}"""),
            Memberships = new List<BlockGroupMembership> { new() { BlockId = b.Id } },
        };

        var output = Sut.BuildTypstDocument(doc, new List<Block> { b }, new[] { reviewGroup });

        // Review-dimension group must not influence layout — single-col output.
        output.Should().NotContain("#columns(");
    }
}
