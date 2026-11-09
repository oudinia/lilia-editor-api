using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Drafts read "Section~4" (a LaTeX tie, shown raw in the editor) and a red "(storage-review)" (a reference to a
/// label nothing defined): the launch bot's sessions, 9 Nov 2026. The output rule says where a tie belongs and that
/// references must resolve.
/// </summary>
public class AskTextAndReferencesTests
{
    [Fact]
    public void Ties_belong_only_in_cross_references() =>
        AskLiliaService.OutputNote.Should().Contain("no LaTeX tie (~) in plain text").And.Contain("Kind~(prefix:label)");

    [Fact]
    public void References_must_point_at_something_that_exists() =>
        AskLiliaService.OutputNote.Should().Contain("Refer only to labels and cite keys that exist")
            .And.Contain("never leave a placeholder key");
}
