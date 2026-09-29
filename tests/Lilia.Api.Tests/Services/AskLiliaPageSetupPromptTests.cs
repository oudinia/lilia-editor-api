using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Ask Lilia told authors that margins, columns, headers and spacing were
/// unsupported because nothing in its prompt or tools said otherwise.
/// </summary>
public class AskLiliaPageSetupPromptTests
{
    [Fact]
    public void The_note_says_page_setup_is_supported_and_names_every_setting()
    {
        var note = AskLiliaService.PageSetupNote(editMode: true);
        note.Should().Contain("DOES support page setup").And.Contain("never tell the author");
        foreach (var w in new[] { "margins", "columns", "font family", "line spacing", "paragraph indent", "page numbering", "header and footer", "custom preamble", "orientation", "paper" })
            note.Should().Contain(w);
        note.Should().Contain("set_document_settings").And.Contain("get_document_settings");
    }

    [Fact]
    public void The_note_maps_a_tex_preamble_to_settings_with_single_backslashes()
    {
        var note = AskLiliaService.PageSetupNote(true);
        note.Should().Contain(@"\documentclass").And.Contain(@"\fancyhead").And.Contain(@"\pagenumbering{roman}")
            .And.Contain(@"\linespread").And.Contain(@"\setstretch").And.Contain(@"\setlength{\parindent}{0pt}")
            .And.Contain("geometry").And.Contain("familydefault");
        note.Should().NotContain(@"\\newcommand", "the model must see one backslash, not an escaped pair");
    }

    [Fact]
    public void A_pasted_tex_is_sent_to_the_importer_and_its_gaps_are_reported()
    {
        var note = AskLiliaService.PageSetupNote(true);
        note.Should().Contain("import_latex").And.Contain("notApplied");
    }

    [Fact]
    public void The_note_is_honest_about_what_is_not_supported()
    {
        var note = AskLiliaService.PageSetupNote(true);
        note.Should().Contain("NOT supported").And.Contain("automatic page number is not printed").And.Contain("watermarks").And.Contain("OpenType")
            .And.Contain(@"\tiny").And.Contain("do not drop it silently");
    }

    [Fact]
    public void Without_edit_mode_it_points_at_edit_mode_instead_of_saying_unsupported()
    {
        var read = AskLiliaService.PageSetupNote(false);
        read.Should().Contain("Edit mode").And.NotContain("Set them with set_document_settings");
        read.Should().Contain("DOES support page setup");
    }
}
