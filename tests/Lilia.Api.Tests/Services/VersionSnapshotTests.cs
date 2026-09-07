using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services.Versioning;
using Lilia.Core.Entities;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Versioning round-trip.
///
/// Every test here corresponds to something restore actually got wrong: block
/// ids were regenerated, nesting was dropped, and most of the document's
/// settings were never in the snapshot at all — so "restore" returned yesterday's
/// text under today's layout, with every cross-reference broken.
///
/// These are pure functions on purpose. Blocks and snapshots map to
/// <c>JsonDocument</c>, which the in-memory EF provider throws on, so the rules
/// have to live outside the DbContext to be testable at all.
/// </summary>
public class VersionSnapshotTests
{
    private static Block Blk(
        Guid id, string type, string json, int sort, Guid? parent = null, int depth = 0) => new()
    {
        Id = id,
        Type = type,
        Content = JsonDocument.Parse(json),
        SortOrder = sort,
        ParentId = parent,
        Depth = depth,
        Path = parent is null ? null : $"{parent}/{id}",
        Status = "draft",
    };

    private static Document Doc() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Wave mechanics",
        Language = "en",
        PaperSize = "a4",
        Orientation = "portrait",
        FontFamily = "serif",
        FontSize = 11,
        Columns = 2,
        ColumnSeparator = "rule",
        BalancedColumns = true,
        MarginTop = "25mm",
        MarginLeft = "20mm",
        HeaderText = "Draft",
        PageNumbering = "roman",
        DocumentCategory = "report",
        LatexDocumentClass = "report",
        LatexDocumentClassOptions = "11pt,twoside",
        CustomPreamble = @"\usepackage{amsmath}",
        LatexEngine = "lualatex",
    };

    private static JsonElement Snap(Document d, IEnumerable<Block> blocks,
        IEnumerable<BibliographyEntry>? bib = null) =>
        VersionSnapshot.Serialise(d, blocks, bib ?? []).RootElement;

    [Fact]
    public void Block_identity_survives_the_round_trip()
    {
        // Restore used to mint a new Guid per block. The LaTeX emitter writes
        // \label{blk-<id>}, so that silently broke every cross-reference, and
        // restoring the same version twice produced two different documents.
        var id = Guid.NewGuid();
        var snapshot = Snap(Doc(), [Blk(id, "paragraph", """{"text":"hello"}""", 0)]);

        VersionSnapshot.ReadBlocks(snapshot).Single().Id.Should().Be(id);
    }

    [Fact]
    public void Nesting_survives_the_round_trip()
    {
        // parentId was written into the snapshot and then ignored on the way
        // back, so nested blocks were restored as top-level siblings.
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var snapshot = Snap(Doc(), [
            Blk(parent, "list", """{"ordered":true}""", 0),
            Blk(child, "listItem", """{"text":"one"}""", 1, parent, depth: 1),
        ]);

        var read = VersionSnapshot.ReadBlocks(snapshot);
        read.Should().HaveCount(2);
        read[1].ParentId.Should().Be(parent);
        read[1].Depth.Should().Be(1);
    }

    [Fact]
    public void Blocks_come_back_in_sort_order_regardless_of_array_order()
    {
        var snapshot = Snap(Doc(), [
            Blk(Guid.NewGuid(), "paragraph", """{"text":"third"}""", 2),
            Blk(Guid.NewGuid(), "paragraph", """{"text":"first"}""", 0),
            Blk(Guid.NewGuid(), "paragraph", """{"text":"second"}""", 1),
        ]);

        VersionSnapshot.ReadBlocks(snapshot)
            .Select(b => b.SortOrder).Should().ContainInOrder(0, 1, 2);
    }

    [Fact]
    public void Content_is_preserved_verbatim()
    {
        var snapshot = Snap(Doc(), [
            Blk(Guid.NewGuid(), "equation", """{"latex":"E = mc^2","mode":"display"}""", 0),
        ]);

        var content = VersionSnapshot.ReadBlocks(snapshot).Single().Content;
        content.GetProperty("latex").GetString().Should().Be("E = mc^2");
        content.GetProperty("mode").GetString().Should().Be("display");
    }

    [Fact]
    public void Every_setting_that_changes_the_output_round_trips()
    {
        // The old snapshot carried five of these, so restoring an older version
        // left today's document class and column layout on yesterday's content.
        var original = Doc();
        var snapshot = Snap(original, []);

        var target = new Document { Id = original.Id };
        VersionSnapshot.ApplySettings(snapshot, target);

        target.Title.Should().Be(original.Title);
        target.PaperSize.Should().Be(original.PaperSize);
        target.Orientation.Should().Be(original.Orientation);
        target.FontSize.Should().Be(original.FontSize);
        target.Columns.Should().Be(original.Columns);
        target.ColumnSeparator.Should().Be(original.ColumnSeparator);
        target.BalancedColumns.Should().Be(original.BalancedColumns);
        target.MarginTop.Should().Be(original.MarginTop);
        target.HeaderText.Should().Be(original.HeaderText);
        target.PageNumbering.Should().Be(original.PageNumbering);
        target.DocumentCategory.Should().Be(original.DocumentCategory);
        target.LatexDocumentClass.Should().Be(original.LatexDocumentClass);
        target.LatexDocumentClassOptions.Should().Be(original.LatexDocumentClassOptions);
        target.CustomPreamble.Should().Be(original.CustomPreamble);
        target.LatexEngine.Should().Be(original.LatexEngine);
    }

    [Fact]
    public void A_setting_the_snapshot_does_not_carry_is_left_alone()
    {
        // An old snapshot must not blank a setting that did not exist when it
        // was taken.
        var snapshot = JsonDocument.Parse("""{"title":"Old title"}""").RootElement;
        var target = Doc();
        target.LatexEngine = "xelatex";

        VersionSnapshot.ApplySettings(snapshot, target);

        target.Title.Should().Be("Old title");
        target.LatexEngine.Should().Be("xelatex");
    }

    [Fact]
    public void A_setting_explicitly_null_is_restored_as_null()
    {
        // Clearing a margin is a real edit, and restoring must reproduce it —
        // "absent" and "present but null" are different answers.
        var d = Doc();
        d.MarginTop = null;
        var snapshot = Snap(d, []);

        var target = Doc();
        VersionSnapshot.ApplySettings(snapshot, target);

        target.MarginTop.Should().BeNull();
    }

    [Fact]
    public void Bibliography_round_trips_with_its_keys()
    {
        var entry = new BibliographyEntry
        {
            Id = Guid.NewGuid(),
            CiteKey = "nielsen2010",
            EntryType = "book",
            Data = JsonDocument.Parse("""{"title":"Quantum Computation"}"""),
            FormattedText = "Nielsen, M.",
        };
        var snapshot = Snap(Doc(), [], [entry]);

        var read = VersionSnapshot.ReadBibliography(snapshot).Single();
        read.Id.Should().Be(entry.Id);
        read.CiteKey.Should().Be("nielsen2010");
        read.EntryType.Should().Be("book");
        read.Data.GetProperty("title").GetString().Should().Be("Quantum Computation");
    }

    [Fact]
    public void A_schema_1_snapshot_still_reads()
    {
        // Documents versioned before this fix have no schema marker and fewer
        // fields. They must degrade, not throw.
        var legacy = JsonDocument.Parse("""
        {
          "title": "Legacy",
          "fontSize": 12,
          "blocks": [ { "type": "paragraph", "content": {"text":"kept"}, "sortOrder": 0 } ]
        }
        """).RootElement;

        VersionSnapshot.SchemaOf(legacy).Should().Be(1);

        var blocks = VersionSnapshot.ReadBlocks(legacy);
        blocks.Single().Type.Should().Be("paragraph");
        blocks.Single().Content.GetProperty("text").GetString().Should().Be("kept");
        // No id in the snapshot — one is minted rather than throwing.
        blocks.Single().Id.Should().NotBe(Guid.Empty);

        var target = Doc();
        VersionSnapshot.ApplySettings(legacy, target);
        target.Title.Should().Be("Legacy");
        target.FontSize.Should().Be(12);
    }

    [Fact]
    public void An_empty_snapshot_reads_as_empty_rather_than_throwing()
    {
        var empty = JsonDocument.Parse("{}").RootElement;
        VersionSnapshot.ReadBlocks(empty).Should().BeEmpty();
        VersionSnapshot.ReadBibliography(empty).Should().BeEmpty();
    }

    [Fact]
    public void Fingerprint_ignores_how_the_json_was_written()
    {
        // Restore compares the current state against the newest version to
        // decide whether to preserve it first. If that comparison were textual,
        // key ordering alone would add a redundant version on every restore.
        var id = Guid.NewGuid();
        var a = Snap(Doc(), [Blk(id, "paragraph", """{"text":"same","bold":true}""", 0)]);
        var b = Snap(Doc(), [Blk(id, "paragraph", """{"bold":true,"text":"same"}""", 0)]);

        VersionSnapshot.Fingerprint(a).Should().Be(VersionSnapshot.Fingerprint(b));
    }

    [Fact]
    public void Fingerprint_notices_a_real_change()
    {
        var id = Guid.NewGuid();
        var a = Snap(Doc(), [Blk(id, "paragraph", """{"text":"before"}""", 0)]);
        var b = Snap(Doc(), [Blk(id, "paragraph", """{"text":"after"}""", 0)]);

        VersionSnapshot.Fingerprint(a).Should().NotBe(VersionSnapshot.Fingerprint(b));
    }

    [Fact]
    public void Fingerprint_notices_a_settings_change_with_identical_content()
    {
        // Switching engine or document class is a change worth preserving even
        // when not a character of the text moved.
        var blocks = new[] { Blk(Guid.NewGuid(), "paragraph", """{"text":"x"}""", 0) };
        var before = Doc();
        var after = Doc();
        after.LatexEngine = "xelatex";

        VersionSnapshot.Fingerprint(Snap(before, blocks))
            .Should().NotBe(VersionSnapshot.Fingerprint(Snap(after, blocks)));
    }

    [Fact]
    public void Restoring_the_same_snapshot_twice_gives_the_same_document()
    {
        // The regenerated-Guid bug meant this was false: two restores of one
        // version produced two documents that differed in every block id.
        var snapshot = Snap(Doc(), [
            Blk(Guid.NewGuid(), "paragraph", """{"text":"one"}""", 0),
            Blk(Guid.NewGuid(), "paragraph", """{"text":"two"}""", 1),
        ]);

        var first = VersionSnapshot.ReadBlocks(snapshot).Select(b => b.Id);
        var second = VersionSnapshot.ReadBlocks(snapshot).Select(b => b.Id);

        first.Should().Equal(second);
    }
}
