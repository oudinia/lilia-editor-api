using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>MCP-52: \end{document} typed in text cut the PDF there, with a clean check.</summary>
public class DocumentFenceTests
{
    [Fact]
    public void End_document_in_a_paragraph_is_printed_not_obeyed()
    {
        var render = new RenderService(null!, new Mock<ILogger<RenderService>>().Object);
        var latex = render.RenderBlockToLatex(new Block
        {
            Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "paragraph",
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text = @"Before \end{document} after." })),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        latex.Should().Contain(@"\textbackslash{}end\{document\}").And.Contain("after.");
        latex.Should().NotContain(@" \end{document}");
    }
}
