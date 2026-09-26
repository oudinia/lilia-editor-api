using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// A duplicate is the same paper, owned by whoever made it.
///
/// <para>Measured 26 Sep: duplicate copied language, paper, font and size and
/// dropped every other setting — document class and options, packages, the
/// custom preamble (so a copy that uses the author's macros did not compile),
/// columns, margins, headers, spacing, the engine — and flattened nested
/// blocks. It also named the copy "(Copy)"; the design says "(copy)", a suffix
/// so the two sort together (Olivia, documents-actions handoff).</para>
/// </summary>
[Collection("Integration")]
public class DuplicateKeepsTheDocumentTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";

    public DuplicateKeepsTheDocumentTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Document> SeedPaperAsync()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Thesis");
        await using var db = CreateDbContext();
        var d = await db.Documents.FirstAsync(x => x.Id == doc.Id);
        d.LatexDocumentClass = "report";
        d.LatexDocumentClassOptions = "twoside";
        d.LatexPackages = "siunitx";
        d.CustomPreamble = @"\newcommand{\R}{\mathbb{R}}";
        d.Columns = 2;
        d.ColumnGap = 1.2;
        d.Orientation = "landscape";
        d.MarginTop = "2cm";
        d.LineSpacing = 1.5;
        d.PageNumbering = "roman";
        d.HeaderCenter = "Draft";
        d.LatexEngine = "xelatex";
        d.IsPublic = true;
        d.ShareLink = "abcdefghijklmnopqrstuv";
        d.ShareSlug = "thesis";
        d.ValidationErrorCount = 3;
        await db.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task The_copy_keeps_every_setting_and_is_private_to_its_maker()
    {
        var original = await SeedPaperAsync();

        var response = await Client.PostAsync($"/api/documents/{original.Id}/duplicate", null);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var copyId = (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;

        await using var db = CreateDbContext();
        var copy = await db.Documents.FirstAsync(d => d.Id == copyId);
        copy.Title.Should().Be("Thesis (copy)");
        copy.OwnerId.Should().Be(OwnerId);
        copy.TeamId.Should().BeNull("a duplicate is private to whoever made it");

        copy.LatexDocumentClass.Should().Be("report");
        copy.LatexDocumentClassOptions.Should().Be("twoside");
        copy.LatexPackages.Should().Be("siunitx");
        copy.CustomPreamble.Should().Be(@"\newcommand{\R}{\mathbb{R}}");
        copy.Columns.Should().Be(2);
        copy.ColumnGap.Should().Be(1.2);
        copy.Orientation.Should().Be("landscape");
        copy.MarginTop.Should().Be("2cm");
        copy.LineSpacing.Should().Be(1.5);
        copy.PageNumbering.Should().Be("roman");
        copy.HeaderCenter.Should().Be("Draft");
        copy.LatexEngine.Should().Be("xelatex");

        copy.IsPublic.Should().BeFalse("the public link belongs to the original");
        copy.ShareLink.Should().BeNull();
        copy.ShareSlug.Should().BeNull();
        copy.ValidationErrorCount.Should().Be(0, "the copy has not been validated");
        copy.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Copies_count_on_rather_than_stacking_the_suffix()
    {
        var original = await SeedPaperAsync();
        async Task<(Guid Id, string Title)> Duplicate(Guid id)
        {
            var r = await Client.PostAsync($"/api/documents/{id}/duplicate", null);
            var dto = (await r.Content.ReadFromJsonAsync<DocumentDto>())!;
            return (dto.Id, dto.Title);
        }

        var first = await Duplicate(original.Id);
        var second = await Duplicate(original.Id);
        var ofTheCopy = await Duplicate(first.Id);

        first.Title.Should().Be("Thesis (copy)");
        second.Title.Should().Be("Thesis (copy 2)");
        ofTheCopy.Title.Should().Be("Thesis (copy 3)", "a copy of a copy counts on, never \"(copy) (copy)\"");
    }

    [Fact]
    public async Task Nested_blocks_stay_nested_under_their_own_copies()
    {
        var original = await SeedPaperAsync();
        var parent = await SeedBlockAsync(original.Id, "list", """{"items":["a"]}""", 1);
        await using (var db = CreateDbContext())
        {
            db.Blocks.Add(new Block
            {
                Id = Guid.NewGuid(), DocumentId = original.Id, Type = "paragraph",
                Content = JsonDocument.Parse("""{"text":"child"}"""), SortOrder = 2, Depth = 1, ParentId = parent.Id,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsync($"/api/documents/{original.Id}/duplicate", null);
        var copyId = (await response.Content.ReadFromJsonAsync<DocumentDto>())!.Id;

        await using var check = CreateDbContext();
        var blocks = await check.Blocks.Where(b => b.DocumentId == copyId).ToListAsync();
        var copiedParent = blocks.Single(b => b.Type == "list");
        var child = blocks.Single(b => b.Type == "paragraph");
        child.ParentId.Should().Be(copiedParent.Id, "the child hangs under the copy of its parent, not the original");
        copiedParent.Id.Should().NotBe(parent.Id);
    }
}

/// <summary>
/// The document says what its reader may do, as the list does, so the editor's
/// ⋯ can show only actions that will succeed (documents-actions handoff §4).
/// </summary>
[Collection("Integration")]
public class DocumentCarriesTheReadersRoleTests : IntegrationTestBase
{
    public DocumentCarriesTheReadersRoleTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_owner_reads_owner_and_an_invited_editor_reads_editor()
    {
        await SeedUserAsync("test_user_001", "ada@lilia.test", "Ada Lovelace");
        await SeedUserAsync("test_user_002");
        var doc = await SeedDocumentAsync("test_user_001", "Shared");
        await using (var db = CreateDbContext())
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "editor")
                ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "editor" }).Entity;
            db.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = "test_user_002", RoleId = role.Id,
                InvitedBy = "test_user_001", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var mine = await Client.GetFromJsonAsync<DocumentDto>($"/api/documents/{doc.Id}");
        mine!.Role.Should().Be("owner");

        using var editor = CreateClientAs("test_user_002");
        var theirs = await editor.GetFromJsonAsync<DocumentDto>($"/api/documents/{doc.Id}");
        theirs!.Role.Should().Be("editor");
        // …and whose it is: the viewer's top-bar chip names the owner.
        theirs.OwnerName.Should().Be("Ada Lovelace");
        theirs.OwnerEmail.Should().Be("ada@lilia.test");
    }
}
