using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Edit mode writes; it doesn't show (9 Nov 2026). The skill guidance and the OUTPUT rule said to put LML in a fenced
/// block, before the edit-mode rule, so 1 draft in 7 came back as a chapter of text in the reply and the paper stayed empty.
/// </summary>
public class AskEditModeWritesTests
{
    [Fact]
    public void The_edit_mode_rule_sends_drafts_to_the_tools_and_overrides_fenced_lml()
    {
        AskLiliaService.EditModeWritesNote.Should().Contain("apply_lml")
            .And.Contain("never as an LML block in your reply")
            .And.Contain("overrides any instruction above");
    }

    [Fact]
    public void The_architect_skill_tells_edit_mode_to_apply_rather_than_emit()
    {
        var guidance = AiSkillGuidance.Get("lilia-document-architect");
        guidance.Should().Contain("In Ask Lilia's edit mode, write the draft into the document with `apply_lml`");
    }
}
