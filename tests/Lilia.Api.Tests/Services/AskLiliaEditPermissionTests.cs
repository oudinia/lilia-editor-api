using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Ask Lilia's edit mode writes through tools that call the block service
/// with no user — so the mode itself is the permission check. A viewer asking
/// for it gets a chat, not write tools.
/// </summary>
public class AskLiliaEditPermissionTests
{
    private static DocumentDto WithRole(string? role) =>
        (DocumentDto)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DocumentDto)) with { Role = role };

    [Theory]
    [InlineData("owner", true)]
    [InlineData("editor", true)]
    [InlineData("viewer", false)]
    [InlineData(null, false)]
    public void Only_someone_who_may_write_gets_edit_mode(string? role, bool expected)
    {
        AskLiliaService.MayEditWithAi(true, WithRole(role)).Should().Be(expected);
    }

    [Fact]
    public void Nobody_gets_it_without_asking_or_without_a_document()
    {
        AskLiliaService.MayEditWithAi(false, WithRole("owner")).Should().BeFalse();
        AskLiliaService.MayEditWithAi(true, null).Should().BeFalse();
    }
}
