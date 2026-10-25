using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>The LaTeX diff's pieces (phase 3, 25 Oct 2026).</summary>
public class LatexDiffTests
{
    [Fact]
    public async Task Compares_line_by_line_with_counts_and_hunks()
    {
        var before = "a\nb\nc\nd\n";
        var after = "a\nB\nc\nd\ne\n";
        var diff = await LatexDiff.CompareAsync(before, after);
        diff.Added.Should().Be(2);
        diff.Removed.Should().Be(1);
        diff.Hunks.Should().ContainSingle();
        diff.Hunks[0].Lines.Select(l => $"{l.Kind}{l.Text}").Should().Equal([" a", "-b", "+B", " c", " d", "+e"]);
    }

    [Fact]
    public void A_reference_removed_since_the_submission_still_resolves_in_the_marked_up_PDF()
    {
        var submitted = "@article{old2020,\n  title={Old},\n}\n\n@book{kept,\n  title={Kept},\n}";
        var now = "@book{kept,\n  title={Kept again},\n}\n\n@misc{new2026,\n  title={New},\n}";
        var merged = LatexDiff.MergeBib(submitted, now);
        merged.Should().Contain("old2020").And.Contain("new2026").And.Contain("Kept again");
        merged.Should().NotContain("title={Kept},", "the current entry wins");
    }
}

/// <summary>Kept submissions, the diff since one, the marked-up PDF, and who may change the venue.</summary>
[Collection("Integration")]
public class SubmissionHistoryTests : IntegrationTestBase
{
    private readonly string _owner = $"subhist-{Guid.NewGuid():N}"[..28];
    private readonly string _coauthor = $"subhist-co-{Guid.NewGuid():N}"[..28];
    public SubmissionHistoryTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_owner, name: "Ada Owner");
        await SeedUserAsync(_coauthor, name: "Alan Coauthor");
    }

    private async Task<(Guid Doc, Guid Paragraph)> AcmPaperAsync()
    {
        var api = CreateClientAs(_owner);
        var doc = await SeedDocumentAsync(_owner, "Paper");
        await SeedBlockAsync(doc.Id, "title", """{"title":"Paper","author":"Ada Owner\\\\Engines\\\\UK"}""", 0);
        var para = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The first result."}""", 1);
        (await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "acm-sigconf-review" })).EnsureSuccessStatusCode();
        return (doc.Id, para.Id);
    }

    private async Task ShareAsync(Guid docId)
    {
        await using var db = CreateDbContext();
        var editor = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Editor)
            ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = RoleNames.Editor, Permissions = [Permissions.Read, Permissions.Write] }).Entity;
        db.DocumentCollaborators.Add(new DocumentCollaborator { Id = Guid.NewGuid(), DocumentId = docId, UserId = _coauthor, RoleId = editor.Id, InvitedBy = _owner });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_submission_is_kept_and_the_diff_shows_what_changed_since()
    {
        var (doc, para) = await AcmPaperAsync();
        var api = CreateClientAs(_owner);
        (await api.GetAsync($"/api/documents/{doc}/submissions/latest/diff")).StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing submitted yet");

        (await api.GetAsync($"/api/documents/{doc}/export/latex?submission=true")).EnsureSuccessStatusCode();
        var list = await api.GetFromJsonAsync<JsonElement>($"/api/documents/{doc}/submissions");
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("by").GetString().Should().Be("Ada Owner");
        list[0].GetProperty("profile").GetString().Should().Be("acm-sigconf-review");

        var same = await api.GetFromJsonAsync<JsonElement>($"/api/documents/{doc}/submissions/latest/diff");
        same.GetProperty("added").GetInt32().Should().Be(0);
        same.GetProperty("removed").GetInt32().Should().Be(0);

        await using (var db = CreateDbContext())
        {
            var block = await db.Blocks.FirstAsync(b => b.Id == para);
            block.Content = JsonDocument.Parse("""{"text":"The first result, revised."}""");
            await db.SaveChangesAsync();
        }
        var diff = await api.GetFromJsonAsync<JsonElement>($"/api/documents/{doc}/submissions/latest/diff");
        diff.GetProperty("added").GetInt32().Should().BeGreaterThan(0);
        var lines = diff.GetProperty("hunks").EnumerateArray().SelectMany(h => h.GetProperty("lines").EnumerateArray())
            .Select(l => l.GetProperty("kind").GetString() + l.GetProperty("text").GetString()).ToList();
        lines.Should().Contain(l => l.StartsWith('-') && l.Contains("The first result."));
        lines.Should().Contain(l => l.StartsWith('+') && l.Contains("The first result, revised."));

        // The co-author sees it too; a stranger doesn't.
        await ShareAsync(doc);
        (await CreateClientAs(_coauthor).GetAsync($"/api/documents/{doc}/submissions")).StatusCode.Should().Be(HttpStatusCode.OK);
        var stranger = $"subhist-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        (await CreateClientAs(stranger).GetAsync($"/api/documents/{doc}/submissions")).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_owner_chooses_the_venue_or_its_stage()
    {
        var (doc, _) = await AcmPaperAsync();
        await ShareAsync(doc);
        var co = CreateClientAs(_coauthor);
        var res = await co.PutAsJsonAsync($"/api/documents/{doc}", new { publisherProfile = "acm-sigconf-final" });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadAsStringAsync()).Should().Contain("publisher_owner_only");
        // Sending the same venue back (a settings save that touches nothing) is fine, and other edits go through.
        (await co.PutAsJsonAsync($"/api/documents/{doc}", new { publisherProfile = "acm-sigconf-review", title = "Paper (co-author edit)" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateClientAs(_owner).PutAsJsonAsync($"/api/documents/{doc}", new { publisherProfile = "acm-sigconf-final" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_marked_up_PDF_shows_the_changes()
    {
        var (doc, para) = await AcmPaperAsync();
        var api = CreateClientAs(_owner);
        (await api.GetAsync($"/api/documents/{doc}/export/latex?submission=true")).EnsureSuccessStatusCode();
        await using (var db = CreateDbContext())
        {
            var block = await db.Blocks.FirstAsync(b => b.Id == para);
            block.Content = JsonDocument.Parse("""{"text":"The first result, revised."}""");
            await db.SaveChangesAsync();
        }
        var res = await api.GetAsync($"/api/documents/{doc}/submissions/latest/marked-up.pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var pdf = await res.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }
}
