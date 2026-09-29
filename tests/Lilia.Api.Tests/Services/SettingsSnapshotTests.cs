using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services.Versioning;
using Lilia.Core.Entities;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Ask Lilia's "Undo AI changes" is a version restore, and a restore only
/// returns what the snapshot holds. Header/footer slots, column gap, line
/// spacing and paragraph indent were not in it, so undoing a set_document_settings
/// left them changed.
/// </summary>
public class SettingsSnapshotTests
{
    private static Document Styled() => new()
    {
        Id = Guid.NewGuid(), OwnerId = "u", Title = "T",
        Orientation = "landscape", Columns = 2, ColumnGap = 0.8, ColumnSeparator = "rule",
        MarginTop = "2cm", LineSpacing = 1.5, ParagraphIndent = "none", PageNumbering = "roman",
        HeaderLeft = "Lecture", HeaderCenter = "3", HeaderRight = "Fall", FooterLeft = "a", FooterCenter = "b", FooterRight = "c",
        PaginationPolicy = "flush",
    };

    private static JsonElement Snap(Document d) =>
        VersionSnapshot.Serialise(d, Array.Empty<Block>(), Array.Empty<BibliographyEntry>()).RootElement;

    [Fact]
    public void Restoring_the_snapshot_taken_before_a_settings_change_undoes_it()
    {
        var doc = Styled();
        var before = Snap(doc); // what CreateVersionAsync stores before Ask Lilia writes

        // set_document_settings changes a bit of everything.
        doc.HeaderLeft = null; doc.HeaderCenter = "Changed"; doc.FooterRight = "zzz";
        doc.ColumnGap = 2; doc.LineSpacing = 2; doc.ParagraphIndent = "1em";
        doc.Orientation = "portrait"; doc.MarginTop = "5cm"; doc.Columns = 1; doc.PageNumbering = "none";
        doc.PaginationPolicy = "ragged";

        VersionSnapshot.ApplySettings(before, doc);

        doc.HeaderLeft.Should().Be("Lecture");
        doc.HeaderCenter.Should().Be("3");
        doc.FooterRight.Should().Be("c");
        doc.ColumnGap.Should().Be(0.8);
        doc.LineSpacing.Should().Be(1.5);
        doc.ParagraphIndent.Should().Be("none");
        doc.Orientation.Should().Be("landscape");
        doc.MarginTop.Should().Be("2cm");
        doc.Columns.Should().Be(2);
        doc.PageNumbering.Should().Be("roman");
        doc.PaginationPolicy.Should().Be("flush");
    }

    [Fact]
    public void A_setting_that_was_unset_before_is_unset_again()
    {
        var doc = new Document { Id = Guid.NewGuid(), OwnerId = "u", Title = "T" };
        var before = Snap(doc);
        doc.HeaderLeft = "added"; doc.LineSpacing = 2; doc.ParagraphIndent = "2em";
        VersionSnapshot.ApplySettings(before, doc);
        doc.HeaderLeft.Should().BeNull();
        doc.LineSpacing.Should().BeNull();
        doc.ParagraphIndent.Should().BeNull();
    }

    [Fact]
    public void An_old_snapshot_without_these_keys_leaves_todays_layout_alone()
    {
        var old = JsonDocument.Parse("{\"schema\":2,\"title\":\"T\",\"columns\":1,\"blocks\":[]}").RootElement;
        var doc = Styled();
        VersionSnapshot.ApplySettings(old, doc);
        doc.HeaderLeft.Should().Be("Lecture");
        doc.ColumnGap.Should().Be(0.8);
        doc.LineSpacing.Should().Be(1.5);
    }

    [Fact]
    public void An_old_snapshot_and_a_default_document_still_match()
    {
        // Otherwise every existing version would read as "changed" and restore
        // would preserve a redundant "Unsaved work" row for each.
        var plain = new Document { Id = Guid.NewGuid(), OwnerId = "u", Title = "T" };
        var now = Snap(plain);
        var oldJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(now.GetRawText())!;
        foreach (var k in new[] { "columnGap", "headerLeft", "headerCenter", "headerRight", "footerLeft", "footerCenter", "footerRight", "lineSpacing", "paragraphIndent", "paginationPolicy" })
            oldJson.Remove(k);
        var old = JsonSerializer.SerializeToElement(oldJson);
        VersionSnapshot.Fingerprint(old).Should().Be(VersionSnapshot.Fingerprint(now));
    }

    [Fact]
    public void A_changed_layout_setting_changes_the_fingerprint()
    {
        var a = Styled();
        var fa = VersionSnapshot.Fingerprint(Snap(a));
        a.HeaderCenter = "different";
        VersionSnapshot.Fingerprint(Snap(a)).Should().NotBe(fa);
    }
}
