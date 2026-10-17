using System.Net;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>
/// The document page for support: blocks, shapes and errors from block_shapes; no title and not a
/// word of the author's text, whatever the block type.
/// </summary>
[Collection("Admin")]
public class RedactedDocumentTests(AdminFixture fx)
{
    [Fact]
    public async Task Support_sees_the_blocks_and_their_errors_but_not_the_title_or_a_word_of_the_text()
    {
        var owner = $"red-{Guid.NewGuid():N}"[..24];
        var docId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Guid paragraphId = Guid.NewGuid(), formulaId = Guid.NewGuid();
        await using (var db = fx.CreateDbContext())
        {
            db.Users.Add(new User { Id = owner, Email = $"{owner}@lilia.test", CreatedAt = now, UpdatedAt = now });
            db.Documents.Add(new Document { Id = docId, OwnerId = owner, Title = "MY-PRIVATE-TITLE divorce letter", CreatedAt = now, UpdatedAt = now });
            db.Blocks.AddRange(
                new Block { Id = Guid.NewGuid(), DocumentId = docId, Type = "heading", SortOrder = 0, Content = System.Text.Json.JsonDocument.Parse("""{"text":"PRIVATE-HEADING","level":1}""") },
                new Block { Id = paragraphId, DocumentId = docId, Type = "paragraph", SortOrder = 1, Content = System.Text.Json.JsonDocument.Parse("""{"text":"PRIVATE-WORDS that nobody else should read"}""") },
                new Block { Id = formulaId, DocumentId = docId, Type = "equation", SortOrder = 2, Content = System.Text.Json.JsonDocument.Parse("""{"source":"\\PRIVATEMACRO{x}"}""") },
                new Block { Id = Guid.NewGuid(), DocumentId = docId, Type = "table", SortOrder = 3, Content = System.Text.Json.JsonDocument.Parse("""{"headers":["PRIVATE-H","b"],"rows":[["PRIVATE-CELL","2"]]}""") });
            db.BlockValidations.Add(new BlockValidation
            {
                Id = Guid.NewGuid(), BlockId = formulaId, DocumentId = docId, ContentHash = "h", Status = "error",
                ErrorMessage = "Undefined control sequence.\nl.1 PRIVATE-CONTEXT \\PRIVATEMACRO", RuleVersion = "v1", Validator = "pdflatex", ValidatedAt = now,
            });
            await db.SaveChangesAsync();
        }
        await using (var db = fx.CreateDbContext())
            while (await BlockShapeProcessor.ProcessAsync(db, 500) > 0) { }

        var r = await fx.Client(AdminFixture.Token(AdminFixture.AdminEmail)).GetAsync($"/documents/{docId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

        html.Should().Contain("Heading · level 1").And.Contain("Paragraph · 6 words").And.Contain("Table · 1 × 2")
            .And.Contain("Formula").And.Contain("Undefined control sequence");
        html.Should().NotContain("PRIVATE-").And.NotContain("divorce").And.NotContain("PRIVATE-CONTEXT");

        var user = await fx.Client(AdminFixture.Token(AdminFixture.AdminEmail)).GetStringAsync($"/users/{owner}");
        user.Should().NotContain("MY-PRIVATE-TITLE").And.Contain($"documents/{docId}");
    }
}
