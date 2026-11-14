using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// check_document's note says what each kind of reference problem prints (MCP-126): a reference to a
/// label with no numbered item before it prints nothing ("see also Listing ."), not "?? or ?".
/// </summary>
public class ReferenceNoteTests
{
    [Fact]
    public void An_empty_reference_prints_nothing_not_question_marks()
    {
        var note = DocumentCheckService.ReferenceNote(dangling: false, missingCitations: false, empty: true, duplicate: false);
        note.Should().Contain("print nothing").And.NotContain("??");
    }

    [Fact]
    public void Each_kind_found_is_named()
    {
        DocumentCheckService.ReferenceNote(true, true, false, false)
            .Should().Contain("print as ??").And.Contain("print as ?").And.NotContain("nothing");
        DocumentCheckService.ReferenceNote(false, false, false, true)
            .Should().Contain("wrong number").And.NotContain("??");
    }
}
